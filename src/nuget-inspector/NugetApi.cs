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
    private readonly SourceCacheContext cache_context = new(){
        NoCache=false,
        DirectDownload=false,
        MaxAge= new DateTimeOffset(DateTime.Now.AddDays(1))
    };
    private readonly GatherCache gather_cache = new();

    private readonly Dictionary<string, JObject> catalog_entry_by_catalog_url = new();
    private readonly Dictionary<string, List<PackageSearchMetadataRegistration>> psmrs_by_package_name = new();
    private readonly Dictionary<PackageIdentity, PackageSearchMetadataRegistration?> psmr_by_identity = new();
    private readonly Dictionary<(PackageIdentity, NuGetFramework), PackageDownload> download_by_identity = new();
    private readonly Dictionary<(PackageIdentity, NuGetFramework), SourcePackageDependencyInfo> spdi_by_identity = new();

    private readonly List<SourceRepository> source_repositories = new();
    private readonly List<PackageMetadataResource> metadata_resources = new();
    private readonly List<DependencyInfoResource> dependency_info_resources = new();

    private readonly ISettings settings;

    private readonly NuGetFramework project_framework;

    public NugetApi(string nuget_config_path, string project_root_path, NuGetFramework project_framework)
    {
        this.project_framework = project_framework;
        List<Lazy<INuGetResourceProvider>> providers = new();
        providers.AddRange(Repository.Provider.GetCoreV3());

        settings = LoadNugetConfigSettings(
            nuget_config_path: nuget_config_path,
            project_root_path: project_root_path);

        PopulateResources(
            providers: providers,
            settings: settings);
    }

    /// <summary>
    /// Return PackageSearchMetadataRegistration querying the API
    /// </summary>
    /// <param name="name"></pa   ram>
    /// <param name="versionRange"></param>
    /// <returns>PackageSearchMetadataRegistration or null</returns>
    public PackageSearchMetadataRegistration? FindPackageVersion(string name, VersionRange? versionRange)
    {
        if (name == null)
            return null;

        var package_versions = FindPackageVersionsThroughCache(name: name);
        // TODO: we may need to error out if version is not known/existing upstream
        if (!package_versions.Any())
            return null;
        IEnumerable<NuGetVersion> versions = package_versions.Select(selector: package => package.Identity.Version);
        var best_version = versionRange?.FindBestMatch(versions: versions);
        return package_versions.Find(package => package.Identity.Version == best_version);
    }

    /// <summary>
    /// Return a list of NuGet package metadata and cache it.
    /// </summary>
    /// <param name="name">id (e.g., the name)</param>
    private List<PackageSearchMetadataRegistration> FindPackageVersionsThroughCache(string name)
    {
        if (psmrs_by_package_name.TryGetValue(key: name, out var psmrs))
        {
            if (Config.TRACE_NET)
                Console.WriteLine($"Metadata Cache hit for '{name}'");
            return psmrs;
        }

        if (Config.TRACE_NET)
            Console.WriteLine($"Metadata Cache miss for '{name}'");

        psmrs = FindPackagesOnline(name: name);
        // Update caches
        psmrs_by_package_name[name] = psmrs;
        foreach (var psmr in psmrs)
            psmr_by_identity[psmr.Identity] = psmr;
        return psmrs;
    }

    /// <summary>
    /// Return a single NuGet package PackageSearchMetadataRegistration querying the API
    /// using a PackageIdentity, or null. Cache calls.
    /// </summary>
    /// <param name="pid">identity</param>
    /// <returns>PackageSearchMetadataRegistration or null</returns>
    public PackageSearchMetadataRegistration? FindPackageVersion(PackageIdentity pid)
    {
        if (Config.TRACE)
            Console.WriteLine($"FindPackageVersion: {pid}");

        if (psmr_by_identity.TryGetValue(key: pid, out PackageSearchMetadataRegistration? psmr))
        {
            if (Config.TRACE_NET)
                Console.WriteLine($"Metadata Cache hit for '{pid}'");
            return psmr;
        }

        var exceptions = new List<Exception>();

        foreach (PackageMetadataResource metadata_resource in metadata_resources)
        {
            try
            {
                psmr = (PackageSearchMetadataRegistration)metadata_resource.GetMetadataAsync(
                    package: pid,
                    sourceCacheContext: cache_context,
                    log: new NugetLogger(),
                    token: CancellationToken.None
                ).Result;

                if (psmr != null)
                {
                    if (Config.TRACE) Console.WriteLine($"    Found metadata for '{pid}' from: {metadata_resources}");
                    psmr_by_identity[pid] = psmr;
                    return psmr;
                }
            }
            catch (Exception ex)
            {
                if (Config.TRACE_NET)
                    Console.WriteLine($"FAILED to Fetch metadata for '{pid}' with: {ex.StackTrace}");

                exceptions.Add(item: ex);
            }
        }

        if (Config.TRACE_NET && exceptions.Any())
        {
            Console.WriteLine($"ERROR: No package found for {pid}.");
            foreach (var ex in exceptions)
                Console.WriteLine($"    {ex}");
        }
        // cache this null too
        psmr_by_identity[pid] = null;
        return null;
    }

    /// <summary>
    /// Find NuGet packages online using the configured NuGet APIs
    /// </summary>
    /// <param name="name"></param>
    /// <returns>List of PackageSearchMetadataRegistration</returns>
    private List<PackageSearchMetadataRegistration> FindPackagesOnline(string name)
    {
        if (Config.TRACE)
            Console.WriteLine($"Find package versions online for: {name}");

        var found_psrms = new List<PackageSearchMetadataRegistration>();
        var exceptions = new List<Exception>();

        foreach (PackageMetadataResource metadata_resource in metadata_resources)
        {
            try
            {
                IEnumerable<PackageSearchMetadataRegistration>? psmrs =
                    (IEnumerable<PackageSearchMetadataRegistration>) metadata_resource.GetMetadataAsync(
                        packageId: name,
                        includePrerelease: true,
                        includeUnlisted: true,
                        sourceCacheContext: cache_context,
                        log: new NugetLogger(),
                        token: CancellationToken.None
                    ).Result;

                if (psmrs != null)
                {
                    List<PackageSearchMetadataRegistration> psmrs2 = psmrs.ToList();
                    if (Config.TRACE)
                        Console.WriteLine($"    Fetched metadata for '{name}' from: {metadata_resource}");
                    found_psrms.AddRange(psmrs2);
                    if (Config.TRACE_NET)
                    {
                        foreach (var psmr in psmrs2)
                            Console.WriteLine($"        Fetched: {psmr.PackageId}@{psmr.Version}");
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                if (Config.TRACE)
                    Console.WriteLine($"        FAILED to Fetch metadata for '{name}' with: {ex.StackTrace}");

                exceptions.Add(item: ex);
            }
        }

        if (Config.TRACE && exceptions.Any())
        {
            Console.WriteLine($"ERROR: No package found for {name}.");
            foreach (var ex in exceptions)
                Console.WriteLine($"    {ex}");
        }
        return found_psrms;
    }

    /// <summary>
    /// Return settings loaded from a specific nuget.config file if provided or from the
    /// nuget.config in the code tree, using the NuGet search procedure otherwise.
    /// </summary>
    public static ISettings LoadNugetConfigSettings(
        string nuget_config_path,
        string project_root_path)
    {
        ISettings settings;
        if (!string.IsNullOrWhiteSpace(value: nuget_config_path))
        {
            if (!File.Exists(path: nuget_config_path))
                throw new FileNotFoundException(message: "Missing requested nuget.config", fileName: nuget_config_path);

            if (Config.TRACE)
                Console.WriteLine($"Loading nuget.config: {nuget_config_path}");

            string root = Directory.GetParent(path: nuget_config_path)!.FullName;
            string nuget_config_file_name = Path.GetFileName(path: nuget_config_path);
            settings = Settings.LoadSpecificSettings(root: root, configFileName: nuget_config_file_name);
        }
        else
        {
            // Load defaults settings the NuGet way.
            // Note that we ignore machine-wide settings by design as they are not be relevant to
            // the resolution at hand.
            settings = Settings.LoadDefaultSettings(
                root: project_root_path,
                configFileName: null,
                machineWideSettings: null);
        }
        if (Config.TRACE)
        {
            Console.WriteLine("\nLoadNugetConfigSettings");
            var section_names = new List<string> {
                "packageSources",
                "disabledPackageSources", "activePackageSource",
                "packageSourceMapping",
                "packageManagement"};

            if (Config.TRACE_DEEP)
            {
                foreach (var sn in section_names)
                {
                    SettingSection section =  settings.GetSection(sectionName: sn);
                    if (section == null)
                        continue;

                    Console.WriteLine($"    section: {section.ElementName}");
                    foreach (var item in section.Items)
                        Console.WriteLine($"        item:{item}, ename: {item.ElementName} {item.ToJson()}");
                }
            }
        }
        return settings;
    }

    /// <summary>
    /// Populate the NuGet repositories "Resources" lists using PackageSource
    /// from a feed URL and a nuget.config file path.
    /// These are MetadataResourceList and DependencyInfoResourceList attributes.
    /// </summary>
    private void PopulateResources(List<Lazy<INuGetResourceProvider>> providers, ISettings settings)
    {
        PackageSourceProvider package_source_provider = new(settings: settings);
        List<PackageSource> package_sources = package_source_provider.LoadPackageSources().ToList();
        if (Config.TRACE)
            Console.WriteLine($"\nPopulateResources: Loaded {package_sources.Count} package sources from nuget.config");

        foreach (PackageSource package_source in package_sources)
            AddPackageSource(providers: providers, package_source: package_source);
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
            Console.WriteLine($"    AddPackageSource: adding new {package_source.SourceUri}");

        SourceRepository nuget_repository = new(source: package_source, providers: providers);
        try
        {
            source_repositories.Add(item: nuget_repository);

            PackageMetadataResource package_metadata_endpoint = nuget_repository.GetResource<PackageMetadataResource>();
            metadata_resources.Add(item: package_metadata_endpoint);

            DependencyInfoResource dependency_info_endpoint = nuget_repository.GetResource<DependencyInfoResource>();
            dependency_info_resources.Add(item: dependency_info_endpoint);
        }
        catch (Exception e)
        {
            string message = $"Error loading NuGet API Resource from url: {package_source.SourceUri}";
            if (Config.TRACE)
            {
                Console.WriteLine($"    {message}");
                if (e.InnerException != null)
                    Console.WriteLine(e.InnerException.Message);
            }
            throw new Exception (message, e);
        }
    }

    /// <summary>
    /// Return a list of PackageDependency for a given package PackageIdentity and framework.
    /// </summary>
    public IEnumerable<PackageDependency> GetPackageDependenciesForPackage(PackageIdentity identity, NuGetFramework? framework)
    {
        if (framework == null)
            framework = project_framework;

        SourcePackageDependencyInfo? spdi = GetResolvedSourcePackageDependencyInfo(identity: identity, framework: framework);
        if (spdi != null)
        {
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
        if (framework == null)
            framework = project_framework;

        if (spdi_by_identity.TryGetValue(key: (identity, framework), out SourcePackageDependencyInfo? spdi))
        {
            return spdi;
        }

        if (Config.TRACE)
            Console.WriteLine($"    GetPackageInfo: {identity} framework: {framework}");

        foreach (var dir in dependency_info_resources)
        {
            try
            {
                Task<SourcePackageDependencyInfo>? infoTask = dir.ResolvePackage(
                    package: identity,
                    projectFramework: framework,
                    cacheContext: cache_context,
                    log: new NugetLogger(),
                    token: CancellationToken.None);

                spdi = infoTask.Result;

                if (Config.TRACE && spdi != null)
                    Console.WriteLine($"         url: {spdi.DownloadUri} hash: {spdi.PackageHash}");

                if (spdi != null)
                    spdi_by_identity[(identity, project_framework)] = spdi;
            }
            catch (Exception e)
            {
                if (Config.TRACE)
                {
                    Console.WriteLine($"        Failed to collect SourcePackageDependencyInfo for package: {identity} from source: {dir}");
                    if (e.InnerException != null)
                        Console.WriteLine(e.InnerException.Message);
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
    public PackageDownload? GetPackageDownload(PackageIdentity identity, bool with_details = false)
    {
        // Get download with download URL and checksum (not always there per https://github.com/NuGet/NuGetGallery/issues/9433)

        if (Config.TRACE)
            Console.WriteLine($"    GetPackageDownload: {identity}, with_details: {with_details} project_framework: {project_framework}");


        // try the cache
        if (download_by_identity.TryGetValue((identity, project_framework), out PackageDownload? download))
        {
            if (Config.TRACE_NET)
                Console.WriteLine($"        Caching hit for package '{identity}'");
            return download;
        }
        else
        {
            // fetch from the API otherwise: this is the dependency info that contains these details
            if (Config.TRACE_NET)
                Console.WriteLine($"        Caching miss: Fetching SPDI for package '{identity}'");
            var spdi = GetResolvedSourcePackageDependencyInfo(
                identity: identity,
                framework: project_framework);

            if (spdi != null)
            {
                download = PackageDownload.FromSpdi(spdi);
            }
            else
            {
                if (Config.TRACE_NET)
                    Console.WriteLine($"        Crafting plain download for package '{identity}'");

                // Last resort we craft a synthetic download URL
                string name_lower = identity.Id.ToLower();
                string version_lower = identity.Version.ToString().ToLower();
                download = new()
                {
                    download_url = $"https://api.nuget.org/v3-flatcontainer/{name_lower}/{version_lower}/{name_lower}.{version_lower}.nupkg"
                };
            }
            download_by_identity[(identity, project_framework)] = download;
        }

        if (!with_details || (with_details && download.IsEnhanced()))
            return download;

        // We need to fetch the SHA512 (and the size)
        // Note: we fetch catalog-only data, such as the SHA512, but the "catalog" is not used by
        // the NuGet client and typically not available with other NuGet servers beyond nuget.org
        // For now we do this ugly cooking. We could use instead the NuGet.Protocol.Catalog experimental library
        // we should instead consider a HEAD request as explained at
        // https://github.com/NuGet/NuGetGallery/issues/9433#issuecomment-1472286080
        // which is going to be lighter weight! but will NOT work anywhere but on NuGet.org 
        if (Config.TRACE_NET)
            Console.WriteLine($"       Fetching registration for package '{identity}'");

        PackageSearchMetadataRegistration? registration = FindPackageVersion(identity);
        if (registration != null)
        {
            var package_catalog_url = registration.CatalogUri.ToString();
            if (Config.TRACE_NET)
                Console.WriteLine($"       Fetching catalog for package_catalog_url: {package_catalog_url}");

            JObject catalog_entry;
            if (catalog_entry_by_catalog_url.ContainsKey(package_catalog_url))
            {
                catalog_entry = catalog_entry_by_catalog_url[package_catalog_url];
            }
            else
            {
                // note: this is caching accross runs 
                RequestCachePolicy policy = new(RequestCacheLevel.Default);
                WebRequest request = WebRequest.Create(package_catalog_url);
                request.CachePolicy = policy;
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                string catalog = new StreamReader(response.GetResponseStream()).ReadToEnd();
                catalog_entry = JObject.Parse(catalog);
                // note: this is caching accross calls in a run 
                catalog_entry_by_catalog_url[package_catalog_url] = catalog_entry;
            }

            string hash = catalog_entry["packageHash"]!.ToString();
            download.hash = Convert.ToHexString(Convert.FromBase64String(hash));
            download.hash_algorithm = catalog_entry["packageHashAlgorithm"]!.ToString();
            download.size = (int)catalog_entry["packageSize"]!;
        }
        if (Config.TRACE_NET)
            Console.WriteLine($"        download: {download.ToString()}");
        download_by_identity[(identity, project_framework)] = download;
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
        if (Config.TRACE) Console.WriteLine("\nNugetApi.GatherPotentialDependencies:");

        if (Config.TRACE)
        {
            Console.WriteLine("    direct_dependencies");
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

        PackageSourceMapping psm = PackageSourceMapping.GetPackageSourceMapping(settings);
        var context = new GatherContext(psm)
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
        HashSet<SourcePackageDependencyInfo> gathered_dependencies = ResolverGather.GatherAsync(
            context: context,
            token: CancellationToken.None
        ).Result;

        if (Config.TRACE)
        {
            Console.WriteLine($"    all gathered dependencies: {gathered_dependencies.Count}");
            if (Config.TRACE_DEEP)
            {
                foreach (var spdi in gathered_dependencies)
                    Console.WriteLine($"        {spdi.Id}@{spdi.Version}");
            }
        }
        foreach (var spdi in gathered_dependencies)
        {
            PackageIdentity identity = new(id: spdi.Id, version: spdi.Version);
            spdi_by_identity[(identity, project_framework)] = spdi;
        }

        return gathered_dependencies;
    }

    /// <summary>
    /// Resolve the primary direct_references against all available_dependencies to an effective minimal set of dependencies
    /// </summary>
    public HashSet<SourcePackageDependencyInfo> ResolveDependencies(
        IEnumerable<PackageReference> target_references,
        IEnumerable<SourcePackageDependencyInfo> available_dependencies)
    {
        IEnumerable<PackageIdentity> direct_deps = target_references.Select(p => p.PackageIdentity);
        IEnumerable<string> target_names = new HashSet<string>(direct_deps.Select(p => p.Id));

        PackageResolverContext context = new (
            dependencyBehavior: DependencyBehavior.Lowest,
            targetIds: target_names,
            requiredPackageIds: target_names,
            packagesConfig: target_references,
            preferredVersions: direct_deps,
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