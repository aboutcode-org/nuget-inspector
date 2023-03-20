using System.Diagnostics;
using System.Net;
using System.Net.Cache;
using Newtonsoft.Json.Linq;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// See https://learn.microsoft.com/en-us/nuget/api/overview
/// </summary>
public class NugetApi
{
    private readonly Dictionary<(string, bool), List<PackageSearchMetadataRegistration>> lookupCache = new();
    private readonly Dictionary<PackageIdentity, PackageDownload> download_by_identity = new();
    private readonly List<SourceRepository> source_repositories = new();
    private readonly List<PackageMetadataResource> MetadataResourceList = new();
    private readonly List<DependencyInfoResource> DependencyInfoResourceList = new();

    private readonly SourceCacheContext cache_context = new();
    private readonly GatherCache gather_cache = new();
    private readonly Dictionary<string, JObject> catalog_cache = new();


    public NugetApi(string nugetApiFeedUrl, string nugetConfig)
    {
        var providers = new List<Lazy<INuGetResourceProvider>>();
        providers.AddRange(collection: Repository.Provider.GetCoreV3());
        CreateResourceLists(
            providers: providers,
            nuget_api_feed_url: nugetApiFeedUrl,
            nuget_config_path: nugetConfig);
    }

    /// <summary>
    /// Return PackageSearchMetadataRegistration querying the API
    /// </summary>
    /// <param name="id"></pa   ram>
    /// <param name="versionRange"></param>
    /// <param name="use_cache">use_cache</param>
    /// <param name="include_prerelease">include_prerelease</param>
    /// <returns>PackageSearchMetadataRegistration or null</returns>
    public PackageSearchMetadataRegistration? FindPackageVersion(
        string? id,
        VersionRange? versionRange,
        bool use_cache = true,
        bool include_prerelease = false)
    {
        var package_versions = FindPackageVersionsThroughCache(
            id: id,
            use_cache: use_cache,
            include_prerelease: include_prerelease);

        // TODO: we may need to error out if version is not known/existing upstream
        if (package_versions.Count == 0)
            return null;
        var versions = package_versions.Select(selector: package => package.Identity.Version);
        var best_version = versionRange?.FindBestMatch(versions: versions);
        return package_versions.Find(package => package.Identity.Version == best_version);
    }

    /// <summary>
    /// Return an PackageSearchMetadataRegistration querying the API using a name and version, or null.
    /// Return the latest version when no version is provided.
    /// Bypass the cache if no_cache is true.
    /// Include prereleases if include_prerelease is true.
    /// </summary>
    /// <param name="name">name</param>
    /// <param name="version">version</param>
    /// <param name="use_cache">use_cache</param>
    /// <param name="include_prerelease">include_prerelease</param>
    /// <returns>PackageSearchMetadataRegistration or null</returns>
    public PackageSearchMetadataRegistration? FindPackageVersion(
        string name,
        string? version,
        bool use_cache = true,
        bool include_prerelease = false)
    {
        PackageSearchMetadataRegistration? last_package = null;
        List<PackageSearchMetadataRegistration> packages = FindPackageVersionsThroughCache(
            id: name,
            use_cache: use_cache,
            include_prerelease: include_prerelease);

        foreach (var package in packages)
        {
            last_package = package;
            if (package.Identity.Version.ToString() == version)
                return package;
        }

        if (last_package != null && version == null)
            return last_package;

        return null;
    }

    /// <summary>
    /// Return an PackageSearchMetadataRegistration querying the API using aPackageIdentity, or null.
    /// Return the latest version when no version is provided.
    /// Bypass the cache if no_cache is true.
    /// Include prereleases if include_prerelease is true.
    /// </summary>
    /// <param name="identity">identity  as</param>
    /// <param name="use_cache">use_cache</param>
    /// <param name="include_prerelease">include_prerelease</param>
    /// <returns>PackageSearchMetadataRegistration or null</returns>
    public PackageSearchMetadataRegistration? FindPackageVersion(
        PackageIdentity identity,
        bool use_cache = true,
        bool include_prerelease = false)
    {
        return FindPackageVersion(
           name: identity.Id,
           version: identity.Version.ToString(),
           use_cache: use_cache,
           include_prerelease: include_prerelease);
    }

    /// <summary>
    /// Return a list of NuGet package metadata and cache if use_cache is true.
    /// </summary>
    /// <param name="id">id (e.g., the name)</param>
    /// <param name="use_cache">use_cache</param>
    /// <param name="include_prerelease">include_prerelease</param>
    private List<PackageSearchMetadataRegistration> FindPackageVersionsThroughCache(
        string? id,
        bool use_cache = true,
        bool include_prerelease = true)
    {
        if (id == null)
        {
            return new List<PackageSearchMetadataRegistration>();
        }

        if (use_cache)
        {
            if (!lookupCache.ContainsKey(key: (id, include_prerelease)))
            {
                if (Config.TRACE_NET)
                    Console.WriteLine($"API Cache miss package '{id}'");

                List<PackageSearchMetadataRegistration> metadatas = FindPackagesOnline(
                    name: id,
                    include_prerelease: include_prerelease);
                lookupCache[key: (id, include_prerelease)] = metadatas;
                return metadatas;
            }
            else
            {
                return lookupCache[key: (id, include_prerelease)];
            }
        }
        else
        {
            return FindPackagesOnline(name: id, include_prerelease: include_prerelease);
        }
    }

    /// <summary>
    /// Find NuGet packages online using the configured NuGet APIs
    /// </summary>
    /// <param name="name"></param>
    /// <param name="include_prerelease">include_prerelease</param>
    /// <returns>List of PackageSearchMetadataRegistration</returns>
    private List<PackageSearchMetadataRegistration> FindPackagesOnline(
        string? name,
        bool include_prerelease = false)
    {
        if (Config.TRACE_NET)
            Console.WriteLine($"FindPackagesOnline: {name}");

        var matching_packages = new List<PackageSearchMetadataRegistration>();
        var exceptions = new List<Exception>();

        Stopwatch? stop_watch = null;

        foreach (PackageMetadataResource metadata_resource in MetadataResourceList)
        {
            try
            {
                if (Config.TRACE)
                    stop_watch = Stopwatch.StartNew();

                IEnumerable<PackageSearchMetadataRegistration>? package_metadata =
                    (IEnumerable<PackageSearchMetadataRegistration>)metadata_resource.GetMetadataAsync(
                        packageId: name,
                        includePrerelease: include_prerelease,
                        includeUnlisted: include_prerelease,
                        sourceCacheContext: cache_context,
                        log: new NugetLogger(),
                        token: CancellationToken.None
                    ).Result;

                if (Config.TRACE_NET)
                    Console.WriteLine($"Fetch metadata for '{name}' in: {stop_watch!.ElapsedMilliseconds} ms");

                if (package_metadata != null)
                {
                    List<PackageSearchMetadataRegistration> metadata = package_metadata.ToList();
                    if (metadata.Any())
                        matching_packages.AddRange(collection: metadata);
                }
            }
            catch (Exception ex)
            {
                if (Config.TRACE_NET)
                    Console.WriteLine($"FAILED to Fetch metadata for '{name}' with: {ex.StackTrace}");

                exceptions.Add(item: ex);
            }
        }

        if (matching_packages.Count > 0)
            return matching_packages;

        if (exceptions.Count > 0)
        {
            if (Config.TRACE_NET)
            {
                Console.WriteLine($"No packages were found for {name}, and an exception occured in one or more metadata resources.");
                foreach (var ex in exceptions)
                {
                    Console.WriteLine($"Failed to fetch metadata for packages: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Error: {ex.InnerException.Message}");
                    }
                }
            }

            return new List<PackageSearchMetadataRegistration>();
        }

        if (Config.TRACE)
            Console.WriteLine($"No package found for {name} in any meta data resources.");
        return new List<PackageSearchMetadataRegistration>();
    }

    /// <summary>
    /// Populate the NuGet repositories "Resources" lists using PackageSource
    /// from a feed URL and a nuget.config file path.
    /// These are MetadataResourceList and DependencyInfoResourceList attributes.
    /// </summary>
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

                PackageSourceProvider package_source_provider = new(settings: setting);
                IEnumerable<PackageSource> package_sources = package_source_provider.LoadPackageSources();
                List<PackageSource> packageSources = package_sources.ToList();
                if (Config.TRACE)
                    Console.WriteLine($"Loaded {packageSources.Count} package sources from nuget config.");

                foreach (var package_source in packageSources)
                {
                    if (Config.TRACE) Console.WriteLine($"Found package source: {package_source.Source}");
                    AddPackageSource(providers: providers, package_source: package_source);
                }
            }
            else
            {
                if (Config.TRACE) Console.WriteLine($"nuget.config file missing at path: {nuget_config_path}.");
            }
        }

        foreach (var repoUrl in nuget_api_feed_url.Split(","))
        {
            var url = repoUrl.Trim();
            if (string.IsNullOrWhiteSpace(value: url))
                continue;
            var packageSource = new PackageSource(source: url);
            AddPackageSource(providers: providers, package_source: packageSource);
        }
    }

    /// <summary>
    /// Add package_source (e.g., a NuGet repo API URL, aka. PackageSource) to the list of known NuGet APIs.
    /// Also keep track of SourceRepository in source_repositories.
    /// </summary>
    /// <param name="providers">providers</param>
    /// <param name="package_source">package_source</param>
    private void AddPackageSource(List<Lazy<INuGetResourceProvider>> providers, PackageSource package_source)
    {
        if (Config.TRACE)
            Console.WriteLine($"AddPackageSource: adding new {package_source.SourceUri}");

        SourceRepository nuget_repository = new(source: package_source, providers: providers);
        try
        {
            source_repositories.Add(item: nuget_repository);

            PackageMetadataResource package_metadata_endpoint = nuget_repository.GetResource<PackageMetadataResource>();
            MetadataResourceList.Add(item: package_metadata_endpoint);

            DependencyInfoResource dependency_info_endpoint = nuget_repository.GetResource<DependencyInfoResource>();
            DependencyInfoResourceList.Add(item: dependency_info_endpoint);
        }
        catch (Exception e)
        {
            if (Config.TRACE)
            {
                Console.WriteLine($"AddPackageSource: Error loading Resource from url: {package_source.SourceUri}");
                if (e.InnerException != null)
                    Console.WriteLine(e.InnerException.Message);
            }
        }
        if (Config.TRACE)
            Console.WriteLine("    Added");
    }

    /// <summary>
    /// Return a list of PackageDependency for a given package PackageIdentity and framework.
    /// </summary>
    public IEnumerable<PackageDependency> GetPackageDependenciesForPackage(PackageIdentity identity, NuGetFramework? framework)
    {
        SourcePackageDependencyInfo? spdi = GetResolvedSourcePackageDependencyInfo(identity: identity, framework: framework);
        if (spdi != null)
        {
            PackageDownload download = new()
            {
                download_url = spdi.DownloadUri.ToString(),
                package_info = spdi,
            };
            if (!string.IsNullOrEmpty(spdi.PackageHash))
            {
                download.hash = spdi.PackageHash;
                download.hash_algorithm = "SHA512";
            }

            download_by_identity[identity] = download;

            return spdi.Dependencies;
        }
        else
        {
            return new List<PackageDependency>();
        }
    }

    /// <summary>
    /// Return a SourcePackageDependencyInfo or null for a given package.
    /// </summary>
    public SourcePackageDependencyInfo? GetResolvedSourcePackageDependencyInfo(
        PackageIdentity identity,
        NuGetFramework? framework)
    {
        if (Config.TRACE)
            Console.WriteLine($"    GetPackageInfo: {identity}");
        foreach (var dir in DependencyInfoResourceList)
        {
            try
            {
                Task<SourcePackageDependencyInfo>? infoTask = dir.ResolvePackage(
                    package: identity,
                    projectFramework: framework,
                    cacheContext: cache_context,
                    log: new NugetLogger(),
                    token: CancellationToken.None);

                SourcePackageDependencyInfo spdi = infoTask.Result;

                if (Config.TRACE && spdi != null)
                    Console.WriteLine($"         url: {spdi.DownloadUri} hash: {spdi.PackageHash}");
                return spdi;
            }
            catch (Exception e)
            {
                if (Config.TRACE)
                {
                    Console.WriteLine($"        Failed to collect SourcePackageDependencyInfo for package: {identity}");
                    if (e.InnerException != null) Console.WriteLine(e.InnerException.Message);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Return a PackageDownload for a given package identity.
    /// Cache entries for a given package identity as needed
    /// and reuse previsouly cached entries.
    /// </summary>
    /// <param name="identity">a PackageIdentity</param>
    /// <param name="with_details">if true, all fetches the download size, and SHA512 hash. Very slow!!</param>
    public PackageDownload? GetPackageDownload(PackageIdentity identity, bool with_details = true)
    {
        // Get download with download URL
        PackageDownload download;
        if (download_by_identity.ContainsKey(identity))
        {
            if (Config.TRACE)
                Console.WriteLine($"    GetPackageDownload Cache Hit for package '{identity}'");
            download = download_by_identity[identity];
            if (download.IsEnhanced())
                return download;
        }
        else
        {
            string name_lower = identity.Id.ToLower();
            string version_lower = identity.Version.ToString().ToLower();

            download = new PackageDownload()
            {
                download_url = $"https://api.nuget.org/v3-flatcontainer/{name_lower}/{version_lower}/{name_lower}.{version_lower}.nupkg"
            };

            download_by_identity[identity] = download;
            if (Config.TRACE)
                Console.WriteLine($"    GetPackageDownload Cache miss for package '{identity}'");
        }
        if (!with_details)
            return download;

        /// Fetch catalog-only data
        PackageSearchMetadataRegistration? registration = FindPackageVersion(identity);
        if (registration != null)
        {
            var package_catalog_url = registration.CatalogUri.ToString();
            if (Config.TRACE_NET)
                Console.WriteLine($"    Fetching catalog for package_catalog_url: {package_catalog_url}");

            JObject catalog_entry;
            if (catalog_cache.ContainsKey(package_catalog_url))
            {
                catalog_entry = catalog_cache[package_catalog_url];
            }
            else
            {
                // HttpClient client = new();
                // string catalog = client.GetStringAsync(package_catalog_url).Result;
                // catalog_entry = JObject.Parse(catalog);

                // note: this is caching accross runs 
                RequestCachePolicy policy = new(RequestCacheLevel.Default);
                WebRequest request = WebRequest.Create(package_catalog_url);
                request.CachePolicy = policy;
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                string catalog = new StreamReader(response.GetResponseStream()).ReadToEnd();
                catalog_entry = JObject.Parse(catalog);
                // note: this is caching accross calls in a run 
                catalog_cache[package_catalog_url] = catalog_entry;
            }

            string hash = catalog_entry["packageHash"]!.ToString();
            download.hash = Convert.ToHexString(Convert.FromBase64String(hash));
            download.hash_algorithm = catalog_entry["packageHashAlgorithm"]!.ToString();
            download.size = (int)catalog_entry["packageSize"]!;
        }
        return download;
    }

    /// <summary>
    /// Gather all possible dependencies given a list of primary target
    /// identities. Use the configured source_repositories for gathering.
    /// </summary>
    public ISet<SourcePackageDependencyInfo> GatherPotentialDependencies(
        IEnumerable<PackageIdentity> direct_dependencies,
        NuGetFramework framework)
    {
        Console.WriteLine("\nNugetApi.GatherPotentialDependencies:");

        if (Config.TRACE)
        {
            Console.WriteLine($"    direct_dependencies");
            foreach (var id in direct_dependencies)
                Console.WriteLine($"        {id.Id}@{id.Version}");
        }

        var resolution_context = new ResolutionContext(
            dependencyBehavior: DependencyBehavior.Lowest,
            includePrelease: false,
            includeUnlisted: false,
            versionConstraints: VersionConstraints.None,
            gatherCache: gather_cache,
            sourceCacheContext: cache_context
        );

        var target_names = new HashSet<string>(direct_dependencies.Select(p => p.Id)).ToList();

        var context = new GatherContext
        {
            TargetFramework = framework,
            PrimarySources = source_repositories,
            AllSources = source_repositories,
            // required, but empty: no local source repo used here, so we mock it
            PackagesFolderSource = new SourceRepository(
                source: new NuGet.Configuration.PackageSource("installed"),
                providers: new List<Lazy<INuGetResourceProvider>>()),

            PrimaryTargetIds = target_names,
            PrimaryTargets = new List<PackageIdentity>(), //direct_dependencies.ToList(),

            // skip/ignore any InstalledPackages
            InstalledPackages = new List<PackageIdentity>(),
            AllowDowngrades = false,
            ResolutionContext = resolution_context
        };

        HashSet<SourcePackageDependencyInfo> gathered_dependencies = ResolverGather.GatherAsync(context: context, token: CancellationToken.None).Result;
        //var pruned = PrunePackageTree.PrunePreleaseForStableTargets();
        if (Config.TRACE)
        {
            Console.WriteLine($"    all gathered dependencies");
            foreach (var spdi in gathered_dependencies)
            {
                Console.WriteLine($"        {spdi.Id}@{spdi.Version}");
            }
        }
        return gathered_dependencies;
    }

    /// <summary>
    /// Resolve the primary direct_references against all available_dependencies to an effective minimal set of dependencies
    /// </summary>
    public HashSet<SourcePackageDependencyInfo> ResolveDependencies(
        IEnumerable<string> target_names,
        IEnumerable<PackageReference> target_references,
        IEnumerable<SourcePackageDependencyInfo> available_dependencies)
    {
        PackageResolverContext context = new (
            dependencyBehavior: DependencyBehavior.Lowest,
            targetIds: target_names,
            requiredPackageIds: new List<string>(), //target_ids,
            packagesConfig: target_references,
            preferredVersions: new List<PackageIdentity>(),
            availablePackages: available_dependencies,
            packageSources: source_repositories.Select(s => s.PackageSource),
            log: new NugetLogger());

        var resolver = new PackageResolver();
        resolver.Resolve(context: context, token: CancellationToken.None);

        IEnumerable<PackageIdentity> resolved_dep_identities = resolver.Resolve(
            context: context,
            token: CancellationToken.None);

        if (Config.TRACE)
        {
            Console.WriteLine("    actual dependencies");
            foreach (var pid in resolved_dep_identities)
                Console.WriteLine($"        {pid.Id}@{pid.Version}");
        }

        HashSet<SourcePackageDependencyInfo> effective_dependencies = new();

        var same_packages = PackageIdentityComparer.Default;
        foreach (var dep_id in resolved_dep_identities)
        {
            foreach (var possible_dep_id in available_dependencies)
            {
                if (same_packages.Equals(dep_id, possible_dep_id))
                {
                    effective_dependencies.Add(possible_dep_id);
                    break;
                }
            }
        }
        return effective_dependencies;
    }
}