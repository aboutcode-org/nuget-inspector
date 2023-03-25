using Microsoft.Build.Evaluation;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Text;
using System.Xml;

namespace NugetInspector;

/// <summary>
/// See https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files
/// This handler reads a *.*proj file using MSBuild readers and calls the NuGet API for resolution.
/// </summary>
internal class ProjectFileProcessor : IDependencyProcessor
{
    public const string DatasourceId = "dotnet-project-reference";
    public NuGetFramework? ProjectTargetFramework;
    public NugetApi nugetApi;
    public string ProjectPath;

    public ProjectFileProcessor(
        string projectPath,
        NugetApi nugetApi,
        NuGetFramework? project_target_framework)
    {
        ProjectPath = projectPath;
        this.nugetApi = nugetApi;
        ProjectTargetFramework = project_target_framework;
    }

    public List<Dependency> GetDependenciesFromReferences(List<PackageReference> references)
    {
        var dependencies = new List<Dependency>();
        foreach (var reference in references)
        {
            var rpid = reference.PackageIdentity;
            var dep = new Dependency(
                name: rpid.Id,
                version_range: reference.AllowedVersions ?? new VersionRange(rpid.Version),
                framework: ProjectTargetFramework,
                is_direct: true);
            dependencies.Add(item: dep);
        }

        return dependencies;
    }

    /// <summary>
    /// Return a deduplicated list of PackageReference, selecting the first of each
    /// duplicated package names in the original order. This is the dotnet behaviour.
    /// </summary>
    public static List<PackageReference> DeduplicateReferences(List<PackageReference> references)
    {
        var by_name = new Dictionary<string, List<PackageReference>>();

        List<PackageReference> refs;
        foreach (var reference in references)
        {
            var pid = reference.PackageIdentity;
            if (by_name.ContainsKey(pid.Id))
            {
                refs = by_name[pid.Id];
            }
            else
            {
                refs = new List<PackageReference>();
                by_name[pid.Id] = refs;
            }
            refs.Add(reference);
        }

        var deduped = new List<PackageReference>();
        foreach(var dupes in by_name.Values)
        {
            if (Config.TRACE)
            {
                if (dupes.Count != 1)
                {
                    string duplicated = string.Join("; ", dupes.Select(d => string.Join(", ", $"{d.PackageIdentity}")));

                    Console.WriteLine(
                        "DeduplicateReferences: Remove the duplicate items to ensure a consistent dotnet restore behavior. "
                        + $"The duplicate 'PackageReference' items are: {duplicated}");
                }
            }
            deduped.Add(dupes[0]);
        }
        return deduped;
    }

    /// <summary>
    /// Copied from NuGet.Client/src/NuGet.Core/NuGet.Build.Tasks.Console/MSBuildStaticGraphRestore.cs
    /// Copyright (c) .NET Foundation. All rights reserved.
    /// Licensed under the Apache License, Version 2.0.
    /// Gets the <see cref="LibraryIncludeFlags" /> for the specified value.
    /// </summary>
    /// <param name="value">A semicolon delimited list of include flags.</param>
    /// <param name="defaultValue">The default value ot return if the value contains no flags.</param>
    /// <returns>The <see cref="LibraryIncludeFlags" /> for the specified value, otherwise the <paramref name="defaultValue" />.</returns>
    private static LibraryIncludeFlags GetLibraryIncludeFlags(string value, LibraryIncludeFlags defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        string[] parts = MSBuildStringUtility.Split(value);

        return parts.Length > 0 ? LibraryIncludeFlagUtils.GetFlags(parts) : defaultValue;
    }

    /// <summary>
    /// Return a list of PackageReference extracted from the project file
    /// using a project model.
    /// </summary>
    public virtual List<PackageReference> GetPackageReferences()
    {
        if (Config.TRACE)
            Console.WriteLine($"ProjectFileProcessor.GetPackageReferences: ProjectPath {ProjectPath}");

        var references = new List<PackageReference>();

        // TODO: consider reading global.json if present?
        Dictionary<string, string> properties = new();
        if (ProjectTargetFramework != null)
            properties["TargetFramework"] = ProjectTargetFramework.GetShortFolderName();

        var project = new Microsoft.Build.Evaluation.Project(
            projectFile: ProjectPath,
            globalProperties: properties,
            toolsVersion: null);

        foreach (ProjectItem reference in project.GetItems(itemType: "PackageReference"))
        {
            if (Config.TRACE_DEEP)
            {
                Console.WriteLine($"    Project reference: EvaluatedInclude: {reference.EvaluatedInclude}");
                foreach (var meta in reference.Metadata)
                    Console.WriteLine($"        Metadata: name: '{meta.Name}' value: '{meta.EvaluatedValue}'");
            }

            // Compute the include and exclude flags
            LibraryIncludeFlags effective_includes_flag = LibraryIncludeFlags.All;
            LibraryIncludeFlags private_assets = LibraryIncludeFlags.None;

            foreach (var meta in reference.Metadata)
            {
                if (meta.Name == "IncludeAssets")
                    effective_includes_flag &= GetLibraryIncludeFlags(meta.EvaluatedValue, LibraryIncludeFlags.All);
                if (meta.Name == "ExcludeAssets")
                    effective_includes_flag &= ~GetLibraryIncludeFlags(meta.EvaluatedValue, LibraryIncludeFlags.None);
                // Private assets is treated as an exclude
                if (meta.Name == "PrivateAssets")
                    private_assets = GetLibraryIncludeFlags(meta.EvaluatedValue, LibraryIncludeFlagUtils.DefaultSuppressParent);
            }
            // Skip fully private assets for package references
            effective_includes_flag &= ~private_assets;
            if (effective_includes_flag == LibraryIncludeFlags.None || private_assets == LibraryIncludeFlags.All)
            {
                if (Config.TRACE)
                    Console.WriteLine($"    Skipping private or excluded asset reference for {reference.EvaluatedInclude}");
                continue;
            }

            var version_metadata = reference.Metadata.FirstOrDefault(predicate: meta => meta.Name == "Version");
            VersionRange? version_range;
            if (version_metadata is not null)
            {
                _ = VersionRange.TryParse(
                    value: version_metadata.EvaluatedValue,
                    allowFloating: true,
                    versionRange: out version_range);
            }
            else
            {
                if (Config.TRACE_DEEP)
                    Console.WriteLine($"    Project reference without version: {reference.EvaluatedInclude}");
                version_range = null;
            }

            PackageReference packref;
            var name = reference.EvaluatedInclude;

            if (version_range == null)
            {
                packref = new PackageReference(
                    identity: new PackageIdentity(id: name, version: null),
                    targetFramework: ProjectTargetFramework);
            }
            else
            {
                packref = new PackageReference(
                    identity: new PackageIdentity(id: name, version: (NuGetVersion?)version_range.MinVersion),
                    targetFramework: ProjectTargetFramework,
                    userInstalled: false,
                    developmentDependency: false,
                    requireReinstallation: false,
                    allowedVersions: version_range);
            }
            references.Add(item: packref);

            if (Config.TRACE)
            {
                Console.WriteLine(
                    $"    Add Direct dependency from PackageReference: id: {packref.PackageIdentity} "
                    + $"version_range: {packref.AllowedVersions}");
            }
        }

        // Also fetch "legacy" versioned references
        foreach (var reference in project.GetItems(itemType: "Reference"))
        {
            if (reference.Xml != null && !string.IsNullOrWhiteSpace(value: reference.Xml.Include) &&
                reference.Xml.Include.Contains("Version="))
            {
                var packageInfo = reference.Xml.Include;

                var comma_pos = packageInfo.IndexOf(",", comparisonType: StringComparison.Ordinal);
                var artifact = packageInfo[..comma_pos];

                const string versionKey = "Version=";
                var versionKeyIndex = packageInfo.IndexOf(value: versionKey, comparisonType: StringComparison.Ordinal);
                var versionStartIndex = versionKeyIndex + versionKey.Length;
                var packageInfoAfterVersionKey = packageInfo[versionStartIndex..];

                string version;
                if (packageInfoAfterVersionKey.Contains(','))
                {
                    var firstSep =
                        packageInfoAfterVersionKey.IndexOf(",", comparisonType: StringComparison.Ordinal);
                    version = packageInfoAfterVersionKey[..firstSep];
                }
                else
                {
                    version = packageInfoAfterVersionKey;
                }

                VersionRange? version_range = null;
                NuGetVersion? vers = null;

                if (!string.IsNullOrWhiteSpace(version))
                {
                    _ = VersionRange.TryParse(
                        value: version,
                        allowFloating: true,
                        versionRange: out version_range);

                    if (version_range != null)
                        vers = version_range.MinVersion;
                }

                PackageReference plainref = new (
                    identity: new PackageIdentity(id: artifact, version: vers),
                    targetFramework: ProjectTargetFramework,
                    userInstalled: false,
                    developmentDependency: false,
                    requireReinstallation: false,
                    allowedVersions: version_range);

                references.Add(plainref);

                if (Config.TRACE)
                {
                    Console.WriteLine(
                        $"    Add Direct dependency from Reference: id: {plainref.PackageIdentity} "
                        + $"version_range: {plainref.AllowedVersions}");
                }
            }
        }
        ProjectCollection.GlobalProjectCollection.UnloadProject(project: project);
        return references;
    }

    /// <summary>
    /// Resolve the dependencies one at a time.
    /// </summary>
    public DependencyResolution Resolve()
    {
        //return ResolveOneAtATime()  ;
        return ResolveManyAtOnce();
    }

    /// <summary>
    /// Resolve the dependencies one at a time.
    /// </summary>
    public DependencyResolution ResolveOneAtATime()
    {
        if (Config.TRACE)
            Console.WriteLine("\nProjectFileResolver.ResolveOneAtATime: starting resolution");
        try
        {
            List<PackageReference> references = GetPackageReferences();
            references = DeduplicateReferences(references);
            List<Dependency> dependencies = GetDependenciesFromReferences(references);

            var deps_helper = new NugetResolverHelper(nugetApi: nugetApi);
            foreach (var dep in dependencies)
            {
                deps_helper.ResolveOne(dependency: dep);
            }
            List<BasePackage> gathered_packages = deps_helper.GetPackageList();
            List<BasePackage> gathered_dependencies = new();

            foreach (BasePackage gathered_package in gathered_packages)
            {
                if (gathered_package == null)
                    continue;

                bool is_referenced = gathered_packages.Any(pkg => pkg.dependencies.Contains(gathered_package));
                if (!is_referenced)
                    gathered_dependencies.Add(item: gathered_package);
            }

            var resolution = new DependencyResolution
            {
                Success = true,
                Dependencies = gathered_dependencies
            };

            return resolution;
        }
        catch (Exception ex)
        {
            if (Config.TRACE)
                Console.WriteLine($"    Failed to resolve: {ex}");

            return new DependencyResolution
            {
                Success = false,
                ErrorMessage = ex.ToString()
            };
        }
    }

    /// <summary>
    /// Resolve the dependencies one at a time, then apply a second pass
    /// resolution to reduce the dependency set to a smaller set.
    /// </summary>
    public DependencyResolution ResolveOneAtATimeEnhanced()
    {
        if (Config.TRACE)
            Console.WriteLine("\nProjectFileResolver.ResolveOneAtATime: starting resolution");
        try
        {
            List<PackageReference> references = GetPackageReferences();
            references = DeduplicateReferences(references);
            List<Dependency> dependencies = GetDependenciesFromReferences(references);
            List<PackageIdentity> direct_deps = CollectDirectDeps(dependencies);

            if (direct_deps.Count == 0)
                return new DependencyResolution(Success: true);

            // Use the one at a time approach to gather all possible deps
            var deps_helper = new NugetResolverHelper(nugetApi: nugetApi);
            foreach (var dep in dependencies)
            {
                deps_helper.ResolveOne(dependency: dep);
            }
            List<BasePackage> gathered_packages = deps_helper.GetPackageList();

            HashSet<BasePackage> available_dependent_packages = new();
            CollectAllDependencies(packages: gathered_packages, dependencies: available_dependent_packages);

            HashSet<SourcePackageDependencyInfo> available_dependencies = new ();
            foreach (var available_dependency in available_dependent_packages)
            {
                var identity = available_dependency.GetPackageIdentity();
                SourcePackageDependencyInfo? spdi = nugetApi.GetResolvedSourcePackageDependencyInfo(
                    identity: identity,
                    framework: ProjectTargetFramework);
                if (spdi != null)
                    available_dependencies.Add(spdi);
            }

            HashSet<SourcePackageDependencyInfo> resolved_deps = nugetApi.ResolveDependencies(
                target_references: references,
                available_dependencies: available_dependencies);

            DependencyResolution resolution = new(Success: true);
            foreach (SourcePackageDependencyInfo resolved_dep in resolved_deps)
            {
                BasePackage dep = new(
                    name: resolved_dep.Id,
                    version: resolved_dep.Version.ToString(),
                    framework: ProjectTargetFramework!.GetShortFolderName());
                resolution.Dependencies.Add(dep);
            }

            return resolution;
        }
        catch (Exception ex)
        {
            if (Config.TRACE)
                Console.WriteLine($"    Failed to resolve: {ex}");

            return new DependencyResolution
            {
                Success = false,
                ErrorMessage = ex.ToString()
            };
        }
    }

    /// <summary>
    /// Update recursively a dependencies set with all the dependencies of the provided packages list, at full depth.
    /// </summary>
    private static void CollectAllDependencies(IEnumerable<BasePackage> packages, ISet<BasePackage> dependencies)
    {
        foreach (var package in packages)
        {
            dependencies.UnionWith(package.dependencies);
            CollectAllDependencies(packages: package.dependencies, dependencies: dependencies);
        }
    }

    /// <summary>
    /// Resolve the dependencies resolving all direct dependencies at once.
    /// </summary>
    public DependencyResolution ResolveManyAtOnce()
    {
        if (Config.TRACE)
            Console.WriteLine("\nProjectFileResolver.ResolveManyAtOnce: starting resolution");
        try
        {
            List<PackageReference> references = GetPackageReferences();
            references = DeduplicateReferences(references);
            List<Dependency> dependencies = GetDependenciesFromReferences(references);
            List<PackageIdentity> direct_deps = CollectDirectDeps(dependencies);

            if (direct_deps.Count == 0)
                return new DependencyResolution(Success: true);

            // Use the gather approach to gather all possible deps
            ISet<SourcePackageDependencyInfo> available_dependencies = nugetApi.GatherPotentialDependencies(
                direct_dependencies: direct_deps,
                framework: ProjectTargetFramework!
            );

            if (Config.TRACE_DEEP)
            {
                foreach (var spdi in available_dependencies)
                    Console.WriteLine($"    potential_dependencies: {spdi.Id}@{spdi.Version} prerel:{spdi.Version.IsPrerelease}");
            }

            // Prune the potential dependencies from prereleases

            var pruned_dependencies = PrunePackageTree.PrunePreleaseForStableTargets(
                packages: available_dependencies,
                targets: direct_deps,
                packagesToInstall: direct_deps
            );

            if (Config.TRACE_DEEP)
                foreach (var spdi in pruned_dependencies) Console.WriteLine($"    pruned_dependencies3: {spdi.Id}@{spdi.Version}");

            pruned_dependencies = PrunePackageTree.PruneDowngrades(pruned_dependencies, references);

            if (Config.TRACE_DEEP)
                foreach (var spdi in pruned_dependencies) Console.WriteLine($"    pruned_dependencies3: {spdi.Id}@{spdi.Version}");

            // Resolve proper
            HashSet<SourcePackageDependencyInfo> resolved_deps = nugetApi.ResolveDependencies(
                target_references: references,
                available_dependencies: pruned_dependencies);

            DependencyResolution resolution = new(Success: true);
            foreach (SourcePackageDependencyInfo resolved_dep in resolved_deps)
            {
                if (Config.TRACE)
                {
                    Console.WriteLine($"    Success resolving: {resolved_dep.Id}@{resolved_dep.Version} with subdeps:");
                    foreach (var subdep in resolved_dep.Dependencies)
                        Console.WriteLine($"        {subdep.Id}@{subdep.VersionRange}");
                }
                BasePackage dep = new(
                    name: resolved_dep.Id,
                    version: resolved_dep.Version.ToString(),
                    framework: ProjectTargetFramework!.GetShortFolderName());

                resolution.Dependencies.Add(dep);
            }

            return resolution;
        }
        catch (Exception ex)
        {
            if (Config.TRACE)
                Console.WriteLine($"    Failed to resolve: {ex}");

            return new DependencyResolution
            {
                Success = false,
                ErrorMessage = ex.ToString()
            };
        }
    }

    /// <summary>
    /// Return a list of PackageIdentity built from a list of Dependency
    /// </summary>
    private List<PackageIdentity> CollectDirectDeps(List<Dependency> dependencies)
    {
        if (Config.TRACE_DEEP)
            Console.WriteLine("ProjectFileProcessor.CollectDirectDeps for dependencies:");

        var direct_deps = new List<PackageIdentity>();
        foreach (var dep in dependencies)
        {
            if (Config.TRACE_DEEP)
                Console.WriteLine($"    name: {dep.name} version_range: {dep.version_range}");

            PackageSearchMetadataRegistration? psmr = nugetApi.FindPackageVersion(
                id: dep.name,
                versionRange: dep.version_range,
                include_prerelease: false);

            if (Config.TRACE_DEEP)
                Console.WriteLine($"    psmr1: '{psmr}' for dep.name: {dep.name} dep.version_range: {dep.version_range}");

            if (psmr == null)
            {
                // try again using pre-release
                psmr = nugetApi.FindPackageVersion(
                    id: dep.name,
                    versionRange: dep.version_range,
                    include_prerelease: true);

                if (Config.TRACE_DEEP)
                    Console.WriteLine($"    psmr2: '{psmr}' for dep.name: {dep.name} dep.version_range: {dep.version_range}");
            }

            if (psmr != null)
            {
                direct_deps.Add(psmr.Identity);
            }
        }

        return direct_deps;
    }
}

/// <summary>
/// Read the .*proj file directly as XML to extract PackageReference as a last resort
/// This handler reads a *.*proj file as plain XML and calls the NuGet API for resolution.
/// </summary>
internal class ProjectXmlFileProcessor : ProjectFileProcessor
{
    public new const string DatasourceId = "dotnet-project-xml";

    public ProjectXmlFileProcessor(
        string projectPath,
        NugetApi nugetApi,
        NuGetFramework? project_target_framework) : base(projectPath, nugetApi, project_target_framework)
    {
    }

    /// <summary>
    /// Return a list of PackageReference extracted from the raw XML of a project file.
    /// Note that this is used only as a fallback and does not handle the same
    /// breadth of attributes as with an MSBuild-based parsing. In particular
    /// this does not handle frameworks and conditions.    /// using a project model.
    /// </summary>
    public override List<PackageReference> GetPackageReferences()
    {
        if (Config.TRACE)
            Console.WriteLine($"ProjectXmlFileProcessor.GetPackageReferences: ProjectPath {ProjectPath}");

        var references = new List<PackageReference>();

        Encoding.RegisterProvider(provider: CodePagesEncodingProvider.Instance);
        var doc = new XmlDocument();
        doc.Load(filename: ProjectPath);

        var packagesNodes = doc.GetElementsByTagName(name: "PackageReference");
        foreach (XmlNode package in packagesNodes)
        {
            var attributes = package.Attributes;
            string? version_value = null;

            if (attributes == null)
                continue;

            if (Config.TRACE)
                Console.WriteLine($"    attributes {attributes}");

            var include = attributes[name: "Include"];
            if (include == null)
                continue;

            var version = attributes[name: "Version"];
            if (version != null)
            {
                version_value = version.Value;
            }
            else
            {
                // XML is beautfiful: let's try nested element instead of direct attribute  
                foreach (XmlElement versionNode in package.ChildNodes)
                {
                    if (versionNode.Name == "Version")
                    {
                        if (Config.TRACE)
                            Console.WriteLine($"    no version attribute, using Version tag: {versionNode.InnerText}");
                        version_value = versionNode.InnerText;
                    }
                }
            }

            if (Config.TRACE)
                Console.WriteLine($"    version_value: {version_value}");

            PackageReference packref;
            string name = include.Value;

            VersionRange? version_range = null;
            if (version_value != null)
                version_range = VersionRange.Parse(value: version_value);

            PackageIdentity identity = new(id: name, version: null);

            if (version_range == null)
            {
                packref = new PackageReference(
                    identity: identity,
                    targetFramework: ProjectTargetFramework);
            } else {
                packref = new PackageReference(
                    identity: identity,
                    targetFramework: ProjectTargetFramework,
                    userInstalled: false,
                    developmentDependency: false,
                    requireReinstallation: false,
                    allowedVersions: version_range);
            }
            references.Add(packref);
        }
        return references;
    }
}