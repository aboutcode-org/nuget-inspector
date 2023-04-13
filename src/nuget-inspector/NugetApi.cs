using System.Diagnostics;
using System.Net;
using System.Net.Cache;
using Newtonsoft.Json.Linq;
using NuGet.Commands;
using NuGet.Configuration;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.RuntimeModel;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// See https://learn.microsoft.com/en-us/nuget/api/overview
/// </summary>
public class NugetApi
{
    private readonly SourceCacheContext source_cache_context = new(){
        NoCache=false,
        DirectDownload=false,
        MaxAge= new DateTimeOffset(DateTime.Now.AddDays(5))
    };
    private readonly GatherCache gather_cache = new();

    private readonly Dictionary<string, JObject?> catalog_entry_by_catalog_url = new();
    private readonly Dictionary<string, List<PackageSearchMetadataRegistration>> psmrs_by_package_name = new();
    private readonly Dictionary<PackageIdentity, PackageSearchMetadataRegistration?> psmr_by_identity = new();
    private readonly Dictionary<PackageIdentity, PackageDownload?> download_by_identity = new();
    private readonly Dictionary<PackageIdentity, SourcePackageDependencyInfo> spdi_by_identity = new();

    private readonly List<SourceRepository> source_repositories = new();
    private readonly List<PackageMetadataResource> metadata_resources = new();
    private readonly List<DependencyInfoResource> dependency_info_resources = new();

        private readonly ISettings settings;
    private readonly List<Lazy<INuGetResourceProvider>> providers = new();
    private readonly NuGetFramework project_framework;
    private readonly List<PackageSource> package_sources = new();
    private readonly NugetLogger nuget_logger = new();

    public NugetApi(
        string nuget_config_path,
        string project_root_path,
        NuGetFramework project_framework,
        bool with_nuget_org)
    {
        this.project_framework = project_framework;
        this.providers.AddRange(Repository.Provider.GetCoreV3());

        settings = LoadNugetConfigSettings(
            nuget_config_path: nuget_config_path,
            project_root_path: project_root_path);

        PopulateResources(
            providers: this.providers,
            settings: settings,
            with_nuget_org: with_nuget_org);
    }

    /// <summary>
    /// Return PackageSearchMetadataRegistration querying the API
    /// </summary>
    /// <param name="name"></pa   ram>
    /// <param name="version_range"></param>
    /// <returns>PackageSearchMetadataRegistration or null</returns>
    public PackageSearchMetadataRegistration? FindPackageVersion(string name, VersionRange? version_range)
    {
        if (Config.TRACE_NET)
            Console.WriteLine($"FindPackageVersion for {name} range: {version_range}");

        if (name == null)
            return null;

        var package_versions = FindPackageVersionsThroughCache(name: name);
        // TODO: we may need to error out if version is not known/existing upstream
        if (!package_versions.Any())
            return null;
        IEnumerable<NuGetVersion> versions = package_versions.Select(selector: package => package.Identity.Version);
        var best_version = version_range?.FindBestMatch(versions: versions);
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
            Console.WriteLine($"Fetching package metadata for: {pid}");

        if (psmr_by_identity.TryGetValue(key: pid, out PackageSearchMetadataRegistration? psmr))
        {
            if (Config.TRACE)
                Console.WriteLine($"  Metadata Cache hit for '{pid}'");
            return psmr;
        }

        var exceptions = new List<Exception>();

        foreach (var metadata_resource in metadata_resources)
        {
            try
            {
                psmr = (PackageSearchMetadataRegistration)metadata_resource.GetMetadataAsync(
                    package: pid,
                    sourceCacheContext: source_cache_context,
                    log: nuget_logger,
                    token: CancellationToken.None
                ).Result;

                if (psmr != null)
                {
                    if (Config.TRACE)
                        Console.WriteLine($"  Found metadata for '{pid}' from: {metadata_resource}");
                    psmr_by_identity[pid] = psmr;
                    return psmr;
                }
            }
            catch (Exception ex)
            {
                if (Config.TRACE)
                    Console.WriteLine($"    FAILED to Fetch metadata for '{pid}' with: {ex.StackTrace}");

                exceptions.Add(item: ex);
            }
        }

        var error_message = $"No package metadata found for {pid}.";
        foreach (var ex in exceptions)
            error_message += $"\n    {ex}";

        if (Config.TRACE)
            Console.WriteLine(error_message);

        // cache this null too
        psmr_by_identity[pid] = null;

        throw new Exception(error_message);
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
                        sourceCacheContext: source_cache_context,
                        log: new NugetLogger(),
                        token: CancellationToken.None
                    ).Result;

                if (psmrs != null)
                {
                    List<PackageSearchMetadataRegistration> psmrs2 = psmrs.ToList();
                    if (Config.TRACE)
                    {
                        PackageMetadataResourceV3 mr = (PackageMetadataResourceV3)metadata_resource;
                        Console.WriteLine($"    Fetched #{psmrs2.Count} metadata for '{name}' from: {mr.ToJson}");
                    }

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
        if (Config.TRACE_DEEP)
        {
            Console.WriteLine("\nLoadNugetConfigSettings");
            var section_names = new List<string> {
                "packageSources",
                "disabledPackageSources",
                "activePackageSource",
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
    private void PopulateResources(
        List<Lazy<INuGetResourceProvider>> providers,
        ISettings settings,
        bool with_nuget_org = false)
    {
        PackageSourceProvider package_source_provider = new(settings: settings);
        package_sources.AddRange(package_source_provider.LoadPackageSources());

        if (Config.TRACE)
            Console.WriteLine($"\nPopulateResources: Loaded {package_sources.Count} package sources from nuget.config");

        if (with_nuget_org || !package_sources.Any())
        {
            // Use nuget.org as last resort
            var nuget_source = new PackageSource(
                source: "https://api.nuget.org/v3/index.json",
                name : "nuget.org");
            package_sources.Add(nuget_source);
        }

        HashSet<string> seen = new();
        foreach (PackageSource package_source in package_sources)
        {
            var source_url = package_source.SourceUri.ToString();
            if (seen.Contains(source_url))
                continue;
            SourceRepository source_repository = new(source: package_source, providers: providers);
            AddSourceRepo(source_repo: source_repository);
            seen.Add(source_url);
        }
    }

    /// <summary>
    /// Add package_source (e.g., a NuGet repo API URL, aka. PackageSource) to the list of known NuGet APIs.
    /// Also keep track of SourceRepository in source_repositories.
    /// </summary>
    /// <param name="providers">providers</param>
    /// <param name="source_repo">package_source</param>
    private void AddSourceRepo(SourceRepository source_repo)
    {
        if (Config.TRACE)
            Console.WriteLine($"    AddSourceRepo: adding new {source_repo.PackageSource.SourceUri}");

        try
        {
            source_repositories.Add(item: source_repo);

            PackageMetadataResource package_metadata_endpoint = source_repo.GetResource<PackageMetadataResource>();
            metadata_resources.Add(item: package_metadata_endpoint);

            DependencyInfoResource dependency_info_endpoint = source_repo.GetResource<DependencyInfoResource>();
            dependency_info_resources.Add(item: dependency_info_endpoint);
        }
        catch (Exception e)
        {
            string message = $"Error loading NuGet API Resource from url: {source_repo.PackageSource.SourceUri}";
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

        if (spdi_by_identity.TryGetValue(key: identity, out SourcePackageDependencyInfo? spdi))
        {
            return spdi;
        }

        if (Config.TRACE)
            Console.WriteLine($"  GetPackageInfo: {identity} framework: {framework}");

        foreach (var dir in dependency_info_resources)
        {
            try
            {
                Task<SourcePackageDependencyInfo>? infoTask = dir.ResolvePackage(
                    package: identity,
                    projectFramework: framework,
                    cacheContext: source_cache_context,
                    log: nuget_logger,
                    token: CancellationToken.None);

                spdi = infoTask.Result;

                if (Config.TRACE && spdi != null)
                    Console.WriteLine($"    Found download URL: {spdi.DownloadUri} hash: {spdi.PackageHash}");

                if (spdi != null)
                    {
                        spdi_by_identity[identity] = spdi;
                        return spdi;
                    }
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
    /// Return the download URL of the package or null.
    /// This is based on the logic of private NuGet code.
    /// 1. Try the SourcePackageDependencyInfo.DownloadUri if present.
    /// 2. Try the registration metadata JObject, since there is no API
    /// 3. FUTURE: fall back to craft a URL by hand.
    /// </summary>
    public string? GetDownloadUrl(PackageIdentity identity)
    {
        if (spdi_by_identity.TryGetValue(key: identity, out SourcePackageDependencyInfo? spdi))
        {
            var du = spdi.DownloadUri;
            if (du != null && !string.IsNullOrWhiteSpace(du.ToString()))
                return du.ToString();

            RegistrationResourceV3 rrv3 = spdi.Source.GetResource<RegistrationResourceV3>(CancellationToken.None);
            if (rrv3 != null)
            {
               var meta = rrv3.GetPackageMetadata(
                   identity: identity,
                   cacheContext: source_cache_context,
                   log: nuget_logger,
                   token: CancellationToken.None).Result;
                JToken? content = meta?["packageContent"];
                if (content != null)
                    return content.ToString();
            }
        }
        // TODO last resort: Try if we have package base address URL
        // var base = "TBD";
        // var name = identity.Id.ToLowerInvariant();
        // var version = identity.Version.ToNormalizedString().ToLowerInvariant();
        // return $"{base}/{name}/{version}/{name}.{version}.nupkg";

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

        if (Config.TRACE_NET)
            Console.WriteLine($"    GetPackageDownload: {identity}, with_details: {with_details} project_framework: {project_framework}");

        PackageDownload? download = null;
        // try the cache
        if (download_by_identity.TryGetValue(identity, out download))
        {
            if (Config.TRACE_NET)
                Console.WriteLine($"        Caching hit for package '{identity}'");
            if (download != null)
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
            if (Config.TRACE)
                Console.WriteLine($"        Info available for package '{spdi}'");

            if (spdi != null)
            {
                download = PackageDownload.FromSpdi(spdi);
            }
        }

        if (download != null && string.IsNullOrWhiteSpace(download.download_url))
            download.download_url = GetDownloadUrl(identity) ?? "";
            download_by_identity[identity] = download;

        if (Config.TRACE_NET)
            Console.WriteLine($"       Found download: {download}'");

        if (!with_details || (with_details && download?.IsEnhanced() == true))
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
        if (registration == null)
            return download;

        var package_catalog_url = registration.CatalogUri.ToString();
        if (string.IsNullOrWhiteSpace(package_catalog_url))
            return download;

        if (Config.TRACE_NET)
            Console.WriteLine($"       Fetching catalog for package_catalog_url: {package_catalog_url}");

        JObject? catalog_entry;
        if (catalog_entry_by_catalog_url.ContainsKey(package_catalog_url))
        {
            catalog_entry = catalog_entry_by_catalog_url[package_catalog_url];
        }
        else
        {
            // note: this is caching accross runs 
            try
            {
                RequestCachePolicy policy = new(RequestCacheLevel.Default);
                WebRequest request = WebRequest.Create(package_catalog_url);
                request.CachePolicy = policy;
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                string catalog = new StreamReader(response.GetResponseStream()).ReadToEnd();
                catalog_entry = JObject.Parse(catalog);
                // note: this is caching accross calls in a run 
                catalog_entry_by_catalog_url[package_catalog_url] = catalog_entry;
            }
            catch (Exception ex)
            {
                if (Config.TRACE_NET)
                    Console.WriteLine($"        failed to fetch metadata details for: {package_catalog_url}: {ex}");
                catalog_entry_by_catalog_url[package_catalog_url] = null;
                return download;
            }
        }
        if (catalog_entry != null)
        {
            string hash = catalog_entry["packageHash"]
            !.ToString();
            if (download != null)
            {
                download.hash = Convert.ToHexString(Convert.FromBase64String(hash));
                download.hash_algorithm = catalog_entry["packageHashAlgorithm"]!.ToString();
                download.size = (int)catalog_entry["packageSize"]!;
            }
            if (Config.TRACE_NET)
                Console.WriteLine($"        download: {download}");
            return download;
        }
        return download;
    }

    /// <summary>
    /// Gather all possible dependencies given a list of primary target
    /// identities. Use the configured source_repositories for gathering.
    /// </summary>
    public ISet<SourcePackageDependencyInfo> GatherPotentialDependencies(
        List<PackageIdentity> direct_dependencies,
        NuGetFramework framework)
    {
        if (Config.TRACE)
        {
            Console.WriteLine("\nNugetApi.GatherPotentialDependencies:");
            Console.WriteLine("    direct_dependencies");
            foreach (var pid in direct_dependencies)
                Console.WriteLine($"        {pid} IsPrerelease: {pid.Version.IsPrerelease}");
        }
        var resolution_context = new ResolutionContext(
            dependencyBehavior: DependencyBehavior.Lowest,
            includePrelease: true,
            includeUnlisted: true,
            versionConstraints: VersionConstraints.None,
            gatherCache: gather_cache,
            sourceCacheContext: source_cache_context
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
            PrimaryTargets = direct_dependencies.ToList(),

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
            spdi_by_identity[identity] = spdi;
        }

        return gathered_dependencies;
    }

    /// <summary>
    /// Resolve the primary direct_references against all available_dependencies to an
    /// effective minimal set of dependencies
    /// </summary>
    public HashSet<SourcePackageDependencyInfo> ResolveDependenciesForPackageConfig(
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
            log: nuget_logger);

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

    /// <summary>
    /// Resolve the primary direct_references against all available_dependencies to an
    /// effective minimal set of dependencies using the PackageReference approach.
    /// </summary>
    public HashSet<SourcePackageDependencyInfo> ResolveDependenciesForPackageReference(
        IEnumerable<PackageReference> target_references)
    {
        var psm = PackageSourceMapping.GetPackageSourceMapping(settings);
        var walk_context = new RemoteWalkContext(
            cacheContext: source_cache_context,
            packageSourceMapping: psm,
            logger: nuget_logger);

        var packages = new List<PackageId>();
        foreach (var targetref in target_references)
        {
            try
            {
                packages.Add(PackageId.FromReference(targetref));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   FAILED: targetref: {targetref}");
                throw new Exception(targetref.ToString(), ex);
            }
        }

        walk_context.ProjectLibraryProviders.Add(new ProjectLibraryProvider(packages));

        foreach (var source_repo in source_repositories)
        {
            var provider = new SourceRepositoryDependencyProvider(
                sourceRepository: source_repo,
                logger: nuget_logger,
                cacheContext: source_cache_context,
                ignoreFailedSources: true,
                ignoreWarning: true);

            walk_context.RemoteLibraryProviders.Add(provider);
        }

        // We need a fake root lib as there is only one allowed input
        // This represents the project
        var rootlib = new LibraryRange(
            name: "root_project",
            versionRange: VersionRange.Parse("1.0.0"),
            typeConstraint: LibraryDependencyTarget.Project);

        var walker = new RemoteDependencyWalker(walk_context);

        var results = walker.WalkAsync(
            library: rootlib,
            framework: project_framework,
            runtimeIdentifier: null,
            runtimeGraph: RuntimeGraph.Empty,
            recursive: true);
        var resolved_graph = results.Result;
        CheckGraphForErrors(resolved_graph);

        var resolved_package_info_by_package_id = new Dictionary<PackageId, ResolvedPackageInfo>();

        // we iterate only inner nodes, because we have only one outer node: the "fake" root project 
        foreach (GraphNode<RemoteResolveResult> inner in resolved_graph.InnerNodes)
        {
            if (Config.TRACE_DEEP)
                Console.WriteLine($"    Resolved direct dependency: {inner.Item.Key.Name}@{inner.Item.Key.Version}");

            FlattenGraph(inner, resolved_package_info_by_package_id);
        }

        HashSet<SourcePackageDependencyInfo> flat_dependencies = new();
        foreach (KeyValuePair<PackageId, ResolvedPackageInfo> item in resolved_package_info_by_package_id)
        {
            var dependency = item.Key;
            if (Config.TRACE_DEEP)
            {
                var dpi = item.Value;
                var source_repo = dpi.remote_match?.Provider?.Source;
                Console.WriteLine($"          flat_dependency: {dependency.Name} {dependency.Version} repo: {source_repo?.SourceUri}");
            }

            var spdi = new SourcePackageDependencyInfo(
                id: dependency.Name,
                version: new NuGetVersion(dependency.Version),
                dependencies: new List<PackageDependency>(),
                listed: true,
                source: null
            );
            flat_dependencies.Add(spdi);
        }
        return flat_dependencies;
    }

    /// <summary>
    /// Flatten the graph and populate the result a mapping recursively
    /// </summary>
    public static void FlattenGraph(
        GraphNode<RemoteResolveResult> node,
        Dictionary<PackageId, ResolvedPackageInfo> resolved_package_info_by_package_id)
    {
        if (node.Key.TypeConstraint != LibraryDependencyTarget.Package &&
            node.Key.TypeConstraint != LibraryDependencyTarget.PackageProjectExternal)
        {
            throw new ArgumentException($"Package {node.Key.Name} cannot be resolved from the sources");
        }

        try
        {
            GraphItem<RemoteResolveResult> item = node.Item;
            if (item == null)
            {
                string message = $"      FlattenGraph: node Item is null '{node}'";
                if (Config.TRACE)
                {
                    Console.WriteLine($"        {message}");
                }
                throw new Exception(message);
            }
            LibraryIdentity key = item.Key;
            string name = key.Name;
            string version = key.Version.ToNormalizedString();
            bool isPrerelease = key.Version.IsPrerelease;
            if (Config.TRACE_DEEP)
                Console.WriteLine($"      FlattenGraph: node.Item {node.Item} LibraryId: {key}");

            var pid = new PackageId(
                id: name,
                version: version,
                allow_prerelease_versions: isPrerelease);

            var resolved_package_info = new ResolvedPackageInfo
            {
                package_id = pid,
                remote_match = (RemoteMatch?)item.Data.Match
            };

            if (Config.TRACE_DEEP)
                Console.WriteLine($"        FlattenGraph: {pid} Library: {item.Data.Match.Library}");

            if (!resolved_package_info_by_package_id.ContainsKey(resolved_package_info.package_id))
                resolved_package_info_by_package_id.Add(resolved_package_info.package_id, resolved_package_info);

            foreach (var nd in node.InnerNodes)
            {
                FlattenGraph(nd, resolved_package_info_by_package_id);
            }
        }
        catch (Exception ex)
        {
            var message = $"Failed to resolve graph with: {node}";
            if (Config.TRACE)
                Console.WriteLine($"        FlattenGraph: {message}: {ex}");
            throw new Exception(message, ex);
        }
    }

    /// <summary>
    /// Check the dependency for errors, raise exceptions if these are found
    /// </summary>
    public static void CheckGraphForErrors(GraphNode<RemoteResolveResult> resolved_graph)
    {
        var analysis = resolved_graph.Analyze();
        const bool allow_downgrades = false;
        if (analysis.Downgrades.Any())
        {
            if (Config.TRACE)
            {
                foreach (var item in analysis.Downgrades)
                    Console.WriteLine($"Downgrade from {item.DowngradedFrom.Key} to {item.DowngradedTo.Key}");
            }

            if (!allow_downgrades)
            {
                var name = analysis.Downgrades[0].DowngradedFrom.Item.Key.Name;
                throw new InvalidOperationException($"Downgrade not allowed: {name}");
            }
        }

        if (analysis.Cycles.Any())
        {
            if (Config.TRACE)
            {
                foreach (var item in analysis.Cycles)
                    Console.WriteLine($"Cycle in dependencies: {item.Item.Key.Name},{item.Item.Key.Version.ToNormalizedString()}");
            }

            var name = analysis.Cycles[0].Key.Name;
            throw new InvalidOperationException($"One package has dependency cycle: {name}");
        }

        if (analysis.VersionConflicts.Any())
        {
            if (Config.TRACE)
            {
                foreach (var itm in analysis.VersionConflicts)
                {
                    Console.WriteLine(
                        $"Conflict for {itm.Conflicting.Key.Name},{itm.Conflicting.Key.VersionRange.ToNormalizedString()} resolved as "
                        + $"{itm.Selected.Item.Key.Name},{itm.Selected.Item.Key.Version.ToNormalizedString()}");
                }
            }
            var item = analysis.VersionConflicts[0];
            var requested = $"{item.Conflicting.Key.Name},{item.Conflicting.Key.VersionRange.ToNormalizedString()}";
            var selected = $"{item.Selected.Item.Key.Name},{item.Selected.Item.Key.Version.ToNormalizedString()}";
            throw new InvalidOperationException($"One package has version conflict: requested: {requested}, selected: {selected}");
        }
    }

    /// <summary>
    /// Return Nuspec data fetched and extracted from a .nupkg
    /// </summary>
    public NuspecReader? GetNuspecDetails(
        PackageIdentity identity,
        string download_url,
        SourceRepository source_repo
        )
    {
        try
        {
            var httpSource = HttpSource.Create(source_repo);
            var downloader = new FindPackagesByIdNupkgDownloader(httpSource);
            NuspecReader reader = downloader.GetNuspecReaderFromNupkgAsync(
                identity: identity,
                url: download_url,
                cacheContext: source_cache_context,
                logger: nuget_logger,
                token: CancellationToken.None).Result;

            var copyright = reader.GetCopyright();
            if (Config.TRACE)
                Console.WriteLine($"    Nuspec copyright: {copyright}");

            var repometa = reader.GetRepositoryMetadata();
            if (Config.TRACE)
            {
                Console.WriteLine($"    Nuspec repo.type: {repometa.Type}");
                Console.WriteLine($"    Nuspec repo.url: {repometa.Url}");
                Console.WriteLine($"    Nuspec repo.branch: {repometa.Branch}");
                Console.WriteLine($"    Nuspec repo.commit: {repometa.Commit}");
            }
            if (repometa.Type == "git" && repometa.Url.StartsWith("https://github.com"))
            {
                //<repository type="git" url="https://github.com/JamesNK/Newtonsoft.Json" commit="0a2e291c0d9c0c7675d445703e51750363a549ef"/>
            }
            return reader;
        }
        catch (Exception ex)
        {
            if (Config.TRACE)
                Console.WriteLine($"    Failed to fetch Nuspec: {ex}");
        }
        return null;
    }
}

public class ResolvedPackageInfo
{
    public PackageId? package_id;

    /// <summary>
    /// The NuGet package resolution match.
    /// </summary>
    public RemoteMatch? remote_match;
}

/// <summary>
/// A dependency provider that collects only the local package references
/// </summary>
internal class ProjectLibraryProvider : IDependencyProvider
{
    private readonly ICollection<PackageId> package_ids;

    public ProjectLibraryProvider(ICollection<PackageId> package_ids)
    {
        this.package_ids = package_ids;
    }

    public bool SupportsType(LibraryDependencyTarget libraryTypeFlag)
    {
        return libraryTypeFlag == LibraryDependencyTarget.Project;
    }

    public Library GetLibrary(LibraryRange library_range, NuGetFramework framework)
    {
        var dependencies = new List<LibraryDependency>();

        foreach (var package in package_ids)
        {
            var lib = new LibraryDependency
            {
                LibraryRange =
                    new LibraryRange(
                        name: package.Name,
                        versionRange: VersionRange.Parse(package.Version),
                        typeConstraint: LibraryDependencyTarget.Package)
            };

            dependencies.Add(lib);
        }

        var root_project = new LibraryIdentity(
            name: library_range.Name,
            version: NuGetVersion.Parse("1.0.0"),
            type: LibraryType.Project);

        return new Library
        {
            LibraryRange = library_range,
            Identity = root_project,
            Dependencies = dependencies,
            Resolved = true
        };
    }
}

/// <summary>
/// A package with name and version or version range
/// </summary>
public class PackageId
{
    public string Name { get; }

    /// <summary>
    /// Version or version range as a string
    /// </summary>
    public string Version { get; }

    public bool AllowPrereleaseVersions { get; }

    public PackageId(string id, string version, bool allow_prerelease_versions = false)
    {
        Name = id;
        Version = version;
        AllowPrereleaseVersions = allow_prerelease_versions;
    }

    public override string ToString()
    {
        return $"{Name}@{Version}";
    }

    public static PackageId FromReference(PackageReference reference)
    {
        if (reference == null)
            throw new ArgumentNullException(nameof(reference));
        var av = reference.AllowedVersions;
        bool allow_prerel = false;
        string? version = null;
        if (av != null)
        {
            var mv  = reference.AllowedVersions.MinVersion;
            if (mv != null)
                allow_prerel = mv.IsPrerelease;
            version = reference.AllowedVersions.ToNormalizedString();
        }
        if (version == null && reference.PackageIdentity.Version != null)
        {
            version = reference.PackageIdentity.Version.ToString();
        }

        return new PackageId(
            id: reference.PackageIdentity.Id,
            version: version ?? "",
            allow_prerelease_versions: allow_prerel
        );
    }
}