using System.Diagnostics;
using NuGet.Configuration;
using NuGet.Frameworks;
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
        providers.AddRange(collection: Repository.Provider.GetCoreV3());
        // TODO: providers.AddRange(Repository.Provider.GetCoreV2());
        CreateResourceLists(providers: providers, nuget_api_feed_url: nugetApiFeedUrl, nuget_config_path: nugetConfig);
    }

    /// <summary>
    /// Return IPackageSearchMetadata querying the API
    /// </summary>
    /// <param name="id"></param>
    /// <param name="versionRange"></param>
    /// <returns></returns>
    public IPackageSearchMetadata? FindPackageVersion(string? id, VersionRange? versionRange)
    {
        var package_versions = FindPackages(id: id);
        // TODO: we may need to error out if version is not known/existing upstream
        if (package_versions.Count == 0)
            return null;
        var versions = package_versions.Select(selector: package => package.Identity.Version);
        var best_version = versionRange?.FindBestMatch(versions: versions);
        return package_versions.FirstOrDefault(predicate: package => package.Identity.Version == best_version);
    }

    /// <summary>
    /// Return an IPackageSearchMetadata querying the API using a name and version, or null.
    /// </summary>
    /// <param name="name">name</param>
    /// <param name="version"></param>
    /// <returns></returns>
    public IPackageSearchMetadata? FindPackageVersion(string name, string? version)
    {
        IPackageSearchMetadata? last_package = null;
        foreach (var package in FindPackages(id: name))
        {
            last_package = package;
            if (package.Identity.Version.ToString() == version)
                return package;
        }

        if (last_package != null && version == null)
            return last_package;

        return null;
    }


    private List<IPackageSearchMetadata> FindPackages(string? id)
    {
        if (id != null && lookupCache.ContainsKey(key: id))
        {
            if (Config.TRACE)
            {
                Console.WriteLine($"API Cache hit for package '{id}'");
            }
        }
        else
        {
            if (Config.TRACE)
            {
                Console.WriteLine($"API Cache miss package '{id}'");
            }

            if (id != null)
            {
                lookupCache[key: id] = FindPackagesOnline(name: id);
            }
        }

        return lookupCache[key: id!];
    }

    /// <summary>
    ///     Find NuGet packages online using the configured NuGet APIs 
    /// </summary>
    /// <param name="name"></param>
    /// <returns>List of IPackageSearchMetadata</returns>
    private List<IPackageSearchMetadata> FindPackagesOnline(string? name)
    {
        if (Config.TRACE)
            Console.WriteLine($"------> FindPackagesOnline: {name}");

        var matching_packages = new List<IPackageSearchMetadata>();
        var exceptions = new List<Exception>();

        foreach (PackageMetadataResource metadata_resource in MetadataResourceList)
            try
            {
                Stopwatch stop_watch = Stopwatch.StartNew();
                SourceCacheContext context = new SourceCacheContext();
                IEnumerable<IPackageSearchMetadata>? package_metadata = metadata_resource
                    .GetMetadataAsync(
                        packageId: name, 
                        includePrerelease: true, 
                        includeUnlisted: true,
                        sourceCacheContext: context, 
                        log: new NugetLogger(), 
                        token: CancellationToken.None
                    ).Result;

                if (Config.TRACE)
                    Console.WriteLine(
                        value:
                        $"Took {stop_watch.ElapsedMilliseconds} ms to fetch metadata resource for '{name}'");

                List<IPackageSearchMetadata> packageSearchMetadatas = package_metadata.ToList();
                if (packageSearchMetadatas.Any()) matching_packages.AddRange(collection: packageSearchMetadatas);
            }
            catch (Exception ex)
            {
                exceptions.Add(item: ex);
            }

        if (matching_packages.Count > 0)
        {
            // if (Config.TRACE)
            //     foreach (var mp in matching_packages)
            //     {
            //         Console.WriteLine("========================================================");
            //         Console.WriteLine(mp.Identity);
            //         Console.WriteLine(mp.ToJson(Formatting.Indented));
            //     }

            return matching_packages;
        }

        if (exceptions.Count > 0)
        {
            if (Config.TRACE)
            {
                Console.WriteLine(
                    value:
                    $"No packages were found for {name}, and an exception occured in one or more metadata resources.");
                foreach (var ex in exceptions)
                {
                    Console.WriteLine($"Failed to fetch metadata for packages: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Error: {ex.InnerException.Message}");
                    }
                }
            }

            return new List<IPackageSearchMetadata>();
        }

        if (Config.TRACE) Console.WriteLine($"No package found for {name} in any meta data resources.");
        return new List<IPackageSearchMetadata>();
    }

    private void CreateResourceLists(
        List<Lazy<INuGetResourceProvider>> providers,
        string nuget_api_feed_url,
        string nuget_config_path)
    {
        if (!string.IsNullOrWhiteSpace(value: nuget_config_path))
        {
            if (File.Exists(path: nuget_config_path))
            {
                string parent = Directory.GetParent(path: nuget_config_path)!.FullName;
                string nugetFile = Path.GetFileName(path: nuget_config_path);

                if (Config.TRACE) Console.WriteLine($"Loading nuget config {nugetFile} at {parent}.");
                ISettings? setting = Settings.LoadSpecificSettings(root: parent, configFileName: nugetFile);

                PackageSourceProvider package_source_provider = new PackageSourceProvider(settings: setting);
                IEnumerable<PackageSource> package_sources = package_source_provider.LoadPackageSources();
                List<PackageSource> packageSources = package_sources.ToList();
                if (Config.TRACE)
                    Console.WriteLine($"Loaded {packageSources.Count()} package sources from nuget config.");
                foreach (var package_source in packageSources)
                {
                    if (Config.TRACE) Console.WriteLine($"Found package source: {package_source.Source}");
                    AddPackageSource(providers: providers, package_source: package_source);
                }
            }
            else
            {
                if (Config.TRACE) Console.WriteLine("Nuget config path did not exist.");
            }
        }


        var splitRepoUrls = nuget_api_feed_url.Split(separator: new[] { ',' });
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

    /// <summary>
    /// Add package_source (e.g., a NuGet repo API URL, aka. PackageSource) to the list of known NuGet APIs
    /// </summary>
    /// <param name="providers">providers</param>
    /// <param name="package_source">package_source</param>
    private void AddPackageSource(List<Lazy<INuGetResourceProvider>> providers, PackageSource package_source)
    {
        if (Config.TRACE)
            Console.WriteLine($"AddPackageSource: adding new {package_source.SourceUri}");

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
                if (e.InnerException != null) Console.WriteLine(e.InnerException.Message);
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
                if (e.InnerException != null) Console.WriteLine(e.InnerException.Message);
            }
        }
    }

    public IEnumerable<PackageDependency> DependenciesForPackage(PackageIdentity identity, NuGetFramework? framework)
    {
        foreach (var dependencyInfoResource in DependencyInfoResourceList)
            try
            {
                SourceCacheContext context = new SourceCacheContext();
                Task<SourcePackageDependencyInfo>? infoTask = dependencyInfoResource.ResolvePackage(
                    package: identity,
                    projectFramework: framework,
                    cacheContext: context,
                    log: new NugetLogger(),
                    token: CancellationToken.None);
                SourcePackageDependencyInfo result = infoTask.Result;
                return result.Dependencies;
            }
            catch (Exception e)
            {
                if (Config.TRACE)
                {
                    Console.WriteLine($"Dependency not found for package: {identity}");
                    if (e.InnerException != null) Console.WriteLine(e.InnerException.Message);
                }
            }

        return new List<PackageDependency>();
    }

}

public class DotNetFramework
{
    public string Identifier { get; set; }
    public int Major { get; set; }
    public int Minor { get; set; }

    public DotNetFramework(string id, int major, int minor)
    {
        if (Config.TRACE)
            Console.WriteLine($"DotNetFramework: creating  id: {id}, major: {major}, minor: {minor}");

        Identifier = id;
        Major = major;
        Minor = minor;
    }
}