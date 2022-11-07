using System.Diagnostics;
using NuGet.Common;
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
        providers.AddRange(Repository.Provider.GetCoreV3()); // Add v3 API support
        //TODO:
        // providers.AddRange(Repository.Provider.GetCoreV2());  // Add v2 API support
        CreateResourceLists(providers, nugetApiFeedUrl, nugetConfig);
    }

    /// <summary>
    /// Return IPackageSearchMetadata querying the API
    /// </summary>
    /// <param name="id"></param>
    /// <param name="versionRange"></param>
    /// <returns></returns>
    public IPackageSearchMetadata? FindPackageVersion(string? id, VersionRange? versionRange)
    {
        var matchingPackages = FindPackages(id);
        if (matchingPackages == null) return null;
        var versions = matchingPackages.Select(package => package.Identity.Version);
        var bestVersion = versionRange?.FindBestMatch(versions);
        return matchingPackages.FirstOrDefault(package => package.Identity.Version == bestVersion);
    }

    private List<IPackageSearchMetadata> FindPackages(string? id)
    {
        if (lookupCache.ContainsKey(id))
        {
            if (Config.TRACE) Console.WriteLine("Already looked up package '" + id + "', using the cache.");
        }
        else
        {
            if (Config.TRACE) Console.WriteLine("Have not looked up package '" + id + "', using metadata resources.");
            lookupCache[id] = FindPackagesOnline(id);
        }

        return lookupCache[id];
    }

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
                    .GetMetadataAsync(id, true, true, context, new ApiLogger(), CancellationToken.None).Result;
                if (Config.TRACE)
                    Console.WriteLine("Took " + stopWatch.ElapsedMilliseconds +
                                      " ms to communicate with metadata resource about '" + id + "'");
                if (metaResult.Any()) matchingPackages.AddRange(metaResult);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }

        if (matchingPackages.Count > 0) return matchingPackages;

        if (exceptions.Count > 0)
        {
            if (Config.TRACE)
            {
                Console.WriteLine(
                    $"No packages were found for {id}, and an exception occured in one or more meta data resources.");
                foreach (var ex in exceptions)
                {
                    Console.WriteLine("A meta data resource was unable to load it's packages: " + ex.Message);
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine("The reason: " + ex.InnerException.Message);
                    }
                }
            }

            return null;
        }

        if (Config.TRACE) Console.WriteLine($"No packages were found for {id} in any meta data resources.");
        return null;
    }

    private void CreateResourceLists(
        List<Lazy<INuGetResourceProvider>> providers, 
        string nugetApiFeedUrl,
        string nugetConfig)
    {
        if (!string.IsNullOrWhiteSpace(nugetConfig))
        {
            if (File.Exists(nugetConfig))
            {
                var parent = Directory.GetParent(nugetConfig).FullName;
                var nugetFile = Path.GetFileName(nugetConfig);

                if (Config.TRACE) Console.WriteLine($"Loading nuget config {nugetFile} at {parent}.");
                var setting = Settings.LoadSpecificSettings(parent, nugetFile);

                var packageSourceProvider = new PackageSourceProvider(setting);
                var sources = packageSourceProvider.LoadPackageSources();
                if (Config.TRACE) Console.WriteLine($"Loaded {sources.Count()} package sources from nuget config.");
                foreach (var source in sources)
                {
                    if (Config.TRACE) Console.WriteLine($"Found package source: {source.Source}");
                    AddPackageSource(providers, source);
                }
            }
            else
            {
                if (Config.TRACE) Console.WriteLine("Nuget config path did not exist.");
            }
        }


        var splitRepoUrls = nugetApiFeedUrl.Split(new[] { ',' });
        foreach (var repoUrl in splitRepoUrls)
        {
            var url = repoUrl.Trim();
            if (!string.IsNullOrWhiteSpace(url))
            {
                var packageSource = new PackageSource(url);
                AddPackageSource(providers, packageSource);
            }
        }
    }

    private void AddPackageSource(List<Lazy<INuGetResourceProvider>> providers, PackageSource packageSource)
    {
        var sourceRepository = new SourceRepository(packageSource, providers);
        try
        {
            var packageMetadataResource = sourceRepository.GetResource<PackageMetadataResource>();
            MetadataResourceList.Add(packageMetadataResource);
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine(
                    $"Error loading NuGet PackageMetadataResource resource from url: {packageSource.SourceUri}");
                if (e.InnerException != null) Console.WriteLine(e.InnerException.Message);
            }
        }

        try
        {
            var dependencyInfoResource = sourceRepository.GetResource<DependencyInfoResource>();
            DependencyInfoResourceList.Add(dependencyInfoResource);
            if (Config.TRACE)
                Console.WriteLine($"Successfully added dependency info resource: {sourceRepository.PackageSource.SourceUri}");
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine($"Error loading NuGet Dependency Resource resource from url: {packageSource.SourceUri}");
                if (e.InnerException != null) Console.WriteLine(e.InnerException.Message);
            }
        }
    }

    public IEnumerable<PackageDependency> DependenciesForPackage(PackageIdentity identity, NuGetFramework? framework)
    {
        foreach (var dependencyInfoResource in DependencyInfoResourceList)
            try
            {
                var context = new SourceCacheContext();
                var infoTask = dependencyInfoResource.ResolvePackage(identity, framework, context, new ApiLogger(),
                    CancellationToken.None);
                var result = infoTask.Result;
                return result.Dependencies;
            }
            catch (Exception e)
            {
                if (Config.TRACE)
                {
                    Console.WriteLine("A dependency resource was unable to load for package: " + identity);
                    if (e.InnerException != null) Console.WriteLine(e.InnerException.Message);
                }
            }

        return new List<PackageDependency>();
    }

    private bool FrameworksMatch(PackageDependencyGroup framework1, NugetFramework framework2)
    {
        if (framework1.TargetFramework.IsAny) return true;

        if (framework1.TargetFramework.IsAgnostic) return true;

        if (framework1.TargetFramework.IsSpecificFramework)
        {
            var majorMatch = framework1.TargetFramework.Version.Major == framework2.Major;
            var minorMatch = framework1.TargetFramework.Version.Minor == framework2.Minor;
            return majorMatch && minorMatch;
        }

        if (framework1.TargetFramework.IsUnsupported)
            return false;
        return true;
    }
}

public class NugetFramework
{
    public string Identifier;
    public int Major;
    public int Minor;

    public NugetFramework(string id, int major, int minor)
    {
        Identifier = id;
        Major = major;
        Minor = minor;
    }
}


public class ApiLogger : ILogger
{
    public void LogDebug(string data)
    {
        Trace.WriteLine($"DEBUG: {data}");
    }

    public void LogVerbose(string data)
    {
        Trace.WriteLine($"VERBOSE: {data}");
    }

    public void LogInformation(string data)
    {
        Trace.WriteLine($"INFORMATION: {data}");
    }

    public void LogMinimal(string data)
    {
        Trace.WriteLine($"MINIMAL: {data}");
    }

    public void LogWarning(string data)
    {
        Trace.WriteLine($"WARNING: {data}");
    }

    public void LogError(string data)
    {
        Trace.WriteLine($"ERROR: {data}");
    }

    public void LogInformationSummary(string data)
    {
        Trace.WriteLine($"INFORMATION SUMMARY: {data}");
    }

    public void Log(LogLevel level, string data)
    {
        Trace.WriteLine($"{level.ToString()}: {data}");
    }

    public Task LogAsync(LogLevel level, string data)
    {
        return Task.Run(() => Trace.WriteLine($"{level.ToString()}: {data}"));
    }

    public void Log(ILogMessage message)
    {
        Trace.WriteLine($"{message.Level.ToString()}: {message.Message}");
    }

    public Task LogAsync(ILogMessage message)
    {
        return Task.Run(() => Trace.WriteLine($"{message.Level.ToString()}: {message.Message}"));
    }

    public void LogSummary(string data)
    {
        Trace.WriteLine($"SUMMARY: {data}");
    }

    public void LogErrorSummary(string data)
    {
        Trace.WriteLine($"ERROR SUMMARY: {data}");
    }
}
