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
    public NuGetFramework? ProjectFramework;
    public NugetApi nugetApi;
    public string ProjectPath;

    public ProjectFileProcessor(
        string projectPath,
        NugetApi nugetApi,
        NuGetFramework? project_framework)
    {
        ProjectPath = projectPath;
        this.nugetApi = nugetApi;
        ProjectFramework = project_framework;
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
                framework: ProjectFramework,
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
        if (ProjectFramework != null)
            properties["TargetFramework"] = ProjectFramework.GetShortFolderName();

        var project = new Microsoft.Build.Evaluation.Project(
            projectFile: ProjectPath,
            globalProperties: properties,
            toolsVersion: null);

        foreach (ProjectItem reference in project.GetItems(itemType: "PackageReference"))
        {
            var name = reference.EvaluatedInclude;

            if (Config.TRACE_DEEP)
            {
                Console.WriteLine($"    Project reference: name: {name}");
                foreach (var meta in reference.Metadata)
                    Console.WriteLine($"        Metadata: name: '{meta.Name}' value: '{meta.EvaluatedValue}'");
            }

            // Skip implicit references
            bool is_implicit = false;
            foreach (var meta in reference.Metadata)
            {
                if  (meta.Name == "IsImplicitlyDefined" && meta.EvaluatedValue=="true")
                    is_implicit = true;
            }
            if (is_implicit)
            {
                if (Config.TRACE)
                    Console.WriteLine($"    Skipping implicit package reference for {name}");
                continue;
            }

            // Compute the include and exclude flags to skip private assets
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
                    Console.WriteLine($"    Skipping private or excluded asset reference for {name}");
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
                if (Config.TRACE)
                    Console.WriteLine($"    Project reference without version: {name}");
                version_range = VersionRange.All;
                // // find the minimum version in the range
                // var psmr = nugetApi.FindPackageVersion(name: name, version_range: version_range);
                // if (psmr != null)
                // {
                //     version_range = new VersionRange(new NuGetVersion(psmr.Version));
                // }
                // else
                // {
                //     continue;
                // }
            }

            PackageReference packref;

            if (version_range == null)
            {
                if (Config.TRACE)
                    Console.WriteLine($"    Project reference without version range: {name}");

                packref = new PackageReference(
                    identity: new PackageIdentity(id: name, version: null),
                    targetFramework: ProjectFramework,
                    userInstalled: false,
                    developmentDependency: false,
                    requireReinstallation: false,
                    allowedVersions: VersionRange.All);
            }
            else
            {
                packref = new PackageReference(
                    identity: new PackageIdentity(id: name, version: null),//(NuGetVersion?)version_range.MinVersion),
                    targetFramework: ProjectFramework,
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
                    targetFramework: ProjectFramework,
                    userInstalled: false,
                    developmentDependency: false,
                    requireReinstallation: false,
                    allowedVersions: version_range);

                references.Add(plainref);

                if (Config.TRACE)
                {
                    Console.WriteLine(
                        $"    Add Direct dependency from plain Reference: id: {plainref.PackageIdentity} "
                        + $"version_range: {plainref.AllowedVersions}");
                }
            }
        }
        ProjectCollection.GlobalProjectCollection.UnloadProject(project: project);
        return references;
    }

    /// <summary>
    /// Resolve the dependencies resolving all direct dependencies at once.
    /// </summary>
    public DependencyResolution Resolve()
    {
        return ResolveUsingLib();
    }

    /// <summary>
    /// Resolve the dependencies resolving all direct dependencies at once.
    /// </summary>
    public DependencyResolution ResolveUseGather()
    {
        if (Config.TRACE)
            Console.WriteLine("\nProjectFileProcssor.Resolve: starting resolution");

        List<PackageReference> references = GetPackageReferences();
        if (references.Count == 0)
        {
            return new DependencyResolution(success: true);
        }
        else if (Config.TRACE)
        {
            foreach (var reference in references)
                Console.WriteLine($"    reference: {reference}");
        }

        references = DeduplicateReferences(references);
        List<Dependency> dependencies = GetDependenciesFromReferences(references);

        // FIXME: was using CollectDirectDeps(dependencies);
        List<PackageIdentity> direct_dependency_pids = references.ConvertAll(r => r.PackageIdentity);

        // Use the gather approach to gather all possible deps
        ISet<SourcePackageDependencyInfo> available_dependencies  = nugetApi.GatherPotentialDependencies(
            direct_dependencies: direct_dependency_pids,
            framework: ProjectFramework!
        );

        if (Config.TRACE_DEEP)
        {
            foreach (var spdi in available_dependencies)
                Console.WriteLine($"    available_dependencies: {spdi.Id}@{spdi.Version} prerel:{spdi.Version.IsPrerelease}");
        }

        IEnumerable<SourcePackageDependencyInfo> pruned_dependencies = available_dependencies.ToList();

        // Prune the potential dependencies from prereleases
        pruned_dependencies = PrunePackageTree.PrunePreleaseForStableTargets(
            packages: pruned_dependencies,
            targets: direct_dependency_pids,
            packagesToInstall: direct_dependency_pids
        );
        if (Config.TRACE_DEEP)
        {
            foreach (var spdi in pruned_dependencies)
                Console.WriteLine($"    After PrunePreleaseForStableTargets: {spdi.Id}@{spdi.Version} IsPrerelease: {spdi.Version.IsPrerelease}");
        }

        // prune prerelease versions
        pruned_dependencies = PrunePackageTree.PrunePrereleaseExceptAllowed(
            packages: pruned_dependencies,
            installedPackages: direct_dependency_pids,
            isUpdateAll: false).ToList();
        if (Config.TRACE_DEEP)
            foreach (var spdi in pruned_dependencies) Console.WriteLine($"    After PrunePrereleaseExceptAllowed: {spdi.Id}@{spdi.Version}");

        // prune versions that do not match version range constraints
        pruned_dependencies = PrunePackageTree.PruneDisallowedVersions(
            packages: pruned_dependencies,
            packageReferences: references);
        if (Config.TRACE_DEEP)
            foreach (var spdi in pruned_dependencies) Console.WriteLine($"    After PruneDisallowedVersions: {spdi.Id}@{spdi.Version}");

        // prune downgrades as we always targetted min versions and no downgrade is OK
        pruned_dependencies = PrunePackageTree.PruneDowngrades(pruned_dependencies, references).ToList();
        if (Config.TRACE_DEEP)
            foreach (var spdi in pruned_dependencies) Console.WriteLine($"    PruneDowngrades: {spdi.Id}@{spdi.Version}");

        pruned_dependencies = pruned_dependencies.ToList();
        if (Config.TRACE)
            Console.WriteLine($"    Resolving: {references.Count} references with {pruned_dependencies.Count()} dependencies");

        HashSet<SourcePackageDependencyInfo> resolved_deps = nugetApi.ResolveDependenciesForPackageConfig(
        target_references: references,
        available_dependencies: pruned_dependencies);

        DependencyResolution resolution = new(success: true);
        foreach (SourcePackageDependencyInfo resolved_dep in resolved_deps)
        {
            if (Config.TRACE)
            {
                Console.WriteLine($"     resolved: {resolved_dep.Id}@{resolved_dep.Version}");
                foreach (var subdep in resolved_dep.Dependencies)
                    Console.WriteLine($"        subdep: {subdep.Id}@{subdep.VersionRange}");
            }
            BasePackage dep = new(
                name: resolved_dep.Id,
                version: resolved_dep.Version.ToString(),
                framework: ProjectFramework!.GetShortFolderName());

            resolution.Dependencies.Add(dep);
        }

        return resolution;
    }
    /// <summary>
    /// Resolve the dependencies resolving all direct dependencies at once using a new resolver
    /// </summary>
    public DependencyResolution ResolveUsingLib()
    {
        if (Config.TRACE)
            Console.WriteLine("\nProjectFileProcessor.ResolveUsingLib: starting resolution");

        List<PackageReference> references = GetPackageReferences();
        if (references.Count == 0)
        {
            if (Config.TRACE)
                Console.WriteLine("     no references");

            return new DependencyResolution(success: true);
        }
        else if (Config.TRACE)
        {
            foreach (var reference in references)
                Console.WriteLine($"    found reference: {reference}");
        }

        references = DeduplicateReferences(references);
        if (Config.TRACE)
        {
            foreach (var reference in references)
                Console.WriteLine($"    found dedup reference: {reference}");
        }
        HashSet<SourcePackageDependencyInfo> resolved_deps = nugetApi.ResolveDependenciesForPackageReference(target_references: references);

        DependencyResolution resolution = new(success: true);
        foreach (SourcePackageDependencyInfo resolved_dep in resolved_deps)
        {
            if (Config.TRACE)
            {
                Console.WriteLine($"     resolved: {resolved_dep.Id}@{resolved_dep.Version}");
                foreach (var subdep in resolved_dep.Dependencies)
                    Console.WriteLine($"        subdep: {subdep.Id}@{subdep.VersionRange}");
            }
            BasePackage dep = new(
                name: resolved_dep.Id,
                version: resolved_dep.Version.ToString(),
                framework: ProjectFramework!.GetShortFolderName());

            resolution.Dependencies.Add(dep);
        }

        return resolution;
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
        NuGetFramework? project_framework) : base(projectPath, nugetApi, project_framework)
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
                Console.WriteLine($"    attributes {attributes.ToJson()}");

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

            if (Config.TRACE_DEEP)
                Console.WriteLine($"        version_value: {version_value}");

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
                    targetFramework: ProjectFramework);
            } else {
                packref = new PackageReference(
                    identity: identity,
                    targetFramework: ProjectFramework,
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