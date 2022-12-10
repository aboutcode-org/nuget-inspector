using System.Diagnostics;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// See https://learn.microsoft.com/en-us/nuget/api/overview
/// </summary>
public class NugetApi
{
    private readonly List<DependencyInfoResource> DependencyInfoResourceList = new();
    private readonly Dictionary<string, List<IPackageSearchMetadata>> lookupCache = new();
    private readonly List<PackageMetadataResource> MetadataResourceList = new();

    public NugetApi(string nugetApiFeedUrl, string nugetConfig)
    {
        var providers = new List<Lazy<INuGetResourceProvider>>();
        providers.AddRange(collection: Repository.Provider.GetCoreV3()); // Add v3 API support
        //TODO:
        // providers.AddRange(Repository.Provider.GetCoreV2());  // Add v2 API support
        CreateResourceLists(providers: providers, nugetApiFeedUrl: nugetApiFeedUrl, nugetConfig: nugetConfig);
    }

    /// <summary>
    /// Return IPackageSearchMetadata querying the API
    /// </summary>
    /// <param name="id"></param>
    /// <param name="versionRange"></param>
    /// <returns></returns>
    public IPackageSearchMetadata? FindPackageVersion(string? id, VersionRange? versionRange)
    {
        var matchingPackages = FindPackages(id: id);
        if (matchingPackages == null) return null;
        var versions = matchingPackages.Select(selector: package => package.Identity.Version);
        var bestVersion = versionRange?.FindBestMatch(versions: versions);
        return matchingPackages.FirstOrDefault(predicate: package => package.Identity.Version == bestVersion);
    }

    private List<IPackageSearchMetadata> FindPackages(string? id)
    {
        if (id != null && lookupCache.ContainsKey(key: id))
        {
            if (Config.TRACE) Console.WriteLine(value: $"Already looked up package '{id}', using the cache.");
        }
        else
        {
            if (Config.TRACE) Console.WriteLine(value: $"Have not looked up package '{id}', using metadata resources.");
            if (id != null) lookupCache[key: id] = FindPackagesOnline(id: id);
        }

        return lookupCache[key: id!];
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    private List<IPackageSearchMetadata> FindPackagesOnline(string? id)
    {
        var matchingPackages = new List<IPackageSearchMetadata>();
        var exceptions = new List<Exception>();

        foreach (var metadataResource in MetadataResourceList)
            try
            {
                var stopWatch = Stopwatch.StartNew();
                var context = new SourceCacheContext();
                var metaResult = metadataResource
                    .GetMetadataAsync(packageId: id, includePrerelease: true, includeUnlisted: true,
                        sourceCacheContext: context, log: new NugetLogger(), token: CancellationToken.None).Result;
                if (Config.TRACE)
                    Console.WriteLine(
                        value:
                        $"Took {stopWatch.ElapsedMilliseconds} ms to communicate with metadata resource about '{id}'");
                if (metaResult.Any()) matchingPackages.AddRange(collection: metaResult);
            }
            catch (Exception ex)
            {
                exceptions.Add(item: ex);
            }

        if (matchingPackages.Count > 0) return matchingPackages;

        if (exceptions.Count > 0)
        {
            if (Config.TRACE)
            {
                Console.WriteLine(
                    value:
                    $"No packages were found for {id}, and an exception occured in one or more meta data resources.");
                foreach (var ex in exceptions)
                {
                    Console.WriteLine(value: $"A meta data resource was unable to load packages: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine(value: $"The reason: {ex.InnerException.Message}");
                    }
                }
            }

            return new List<IPackageSearchMetadata>();
        }

        if (Config.TRACE) Console.WriteLine(value: $"No package found for {id} in any meta data resources.");
        return new List<IPackageSearchMetadata>();
    }

    private void CreateResourceLists(
        List<Lazy<INuGetResourceProvider>> providers,
        string nugetApiFeedUrl,
        string nugetConfig)
    {
        if (!string.IsNullOrWhiteSpace(value: nugetConfig))
        {
            if (File.Exists(path: nugetConfig))
            {
                var parent = Directory.GetParent(path: nugetConfig)!.FullName;
                var nugetFile = Path.GetFileName(path: nugetConfig);

                if (Config.TRACE) Console.WriteLine(value: $"Loading nuget config {nugetFile} at {parent}.");
                var setting = Settings.LoadSpecificSettings(root: parent, configFileName: nugetFile);

                var packageSourceProvider = new PackageSourceProvider(settings: setting);
                var sources = packageSourceProvider.LoadPackageSources();
                if (Config.TRACE)
                    Console.WriteLine(value: $"Loaded {sources.Count()} package sources from nuget config.");
                foreach (var source in sources)
                {
                    if (Config.TRACE) Console.WriteLine(value: $"Found package source: {source.Source}");
                    AddPackageSource(providers: providers, package_source: source);
                }
            }
            else
            {
                if (Config.TRACE) Console.WriteLine(value: "Nuget config path did not exist.");
            }
        }


        var splitRepoUrls = nugetApiFeedUrl.Split(separator: new[] { ',' });
        foreach (var repoUrl in splitRepoUrls)
        {
            var url = repoUrl.Trim();
            if (!string.IsNullOrWhiteSpace(value: url))
            {
                var packageSource = new PackageSource(source: url);
                AddPackageSource(providers: providers, package_source: packageSource);
            }
        }
    }

    private void AddPackageSource(List<Lazy<INuGetResourceProvider>> providers, PackageSource package_source)
    {
        var sourceRepository = new SourceRepository(source: package_source, providers: providers);
        try
        {
            var packageMetadataResource = sourceRepository.GetResource<PackageMetadataResource>();
            MetadataResourceList.Add(item: packageMetadataResource);
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine(
                    value:
                    $"Error loading NuGet PackageMetadataResource resource from url: {package_source.SourceUri}");
                if (e.InnerException != null) Console.WriteLine(value: e.InnerException.Message);
            }
        }

        try
        {
            var dependencyInfoResource = sourceRepository.GetResource<DependencyInfoResource>();
            DependencyInfoResourceList.Add(item: dependencyInfoResource);
            if (Config.TRACE)
                Console.WriteLine(
                    value: $"Successfully added dependency info resource: {sourceRepository.PackageSource.SourceUri}");
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine(
                    value: $"Error loading NuGet Dependency Resource resource from url: {package_source.SourceUri}");
                if (e.InnerException != null) Console.WriteLine(value: e.InnerException.Message);
            }
        }
    }

    public IEnumerable<PackageDependency> DependenciesForPackage(PackageIdentity identity, NuGetFramework? framework)
    {
        foreach (var dependencyInfoResource in DependencyInfoResourceList)
            try
            {
                var context = new SourceCacheContext();
                var infoTask = dependencyInfoResource.ResolvePackage(package: identity, projectFramework: framework,
                    cacheContext: context, log: new NugetLogger(),
                    token: CancellationToken.None);
                var result = infoTask.Result;
                return result.Dependencies;
            }
            catch (Exception e)
            {
                if (Config.TRACE)
                {
                    Console.WriteLine(value: $"A dependency resource was unable to load for package: {identity}");
                    if (e.InnerException != null) Console.WriteLine(value: e.InnerException.Message);
                }
            }

        return new List<PackageDependency>();
    }

    private bool FrameworksMatch(PackageDependencyGroup dependency_group, DotNetFramework framework)
    {
        if (dependency_group.TargetFramework.IsAny) return true;

        if (dependency_group.TargetFramework.IsAgnostic) return true;

        if (dependency_group.TargetFramework.IsSpecificFramework)
        {
            var same_major_version = dependency_group.TargetFramework.Version.Major == framework.Major;
            var same_minor_version = dependency_group.TargetFramework.Version.Minor == framework.Minor;
            return same_major_version && same_minor_version;
        }

        if (dependency_group.TargetFramework.IsUnsupported)
            return false;
        return true;
    }
}

public class DotNetFramework
{
    public string Identifier;
    public int Major;
    public int Minor;

    public DotNetFramework(string id, int major, int minor)
    {
        Identifier = id;
        Major = major;
        Minor = minor;
    }
}