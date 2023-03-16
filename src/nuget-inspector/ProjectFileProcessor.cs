using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// See https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files
/// This handler reads a *.*proj file using MSBuild readers and calls the NuGet API for resolution.
/// </summary>
internal class ProjectFileProcessor : IDependencyProcessor
{
    public const string DatasourceId = "dotnet-project-reference";
    private readonly NuGetFramework? ProjectTargetFramework;
    private readonly NugetApi nugetApi;
    private readonly string ProjectPath;

    public ProjectFileProcessor(
        string projectPath,
        NugetApi nugetApi,
        NuGetFramework? projectTargetFramework)
    {
        ProjectPath = projectPath;
        this.nugetApi = nugetApi;
        ProjectTargetFramework = projectTargetFramework;
    }

    /// <summary>
    /// Return a list of Dependency extracted from the project file using a project model.
    /// </summary>
    public List<Dependency> GetDependencies()
    {
        if (Config.TRACE)
            Console.WriteLine($"ProjFileStandardPackageReferenceHandler.GetDependencies: ProjectPath {ProjectPath}");

        var dependencies = new List<Dependency>();

        Dictionary<string, string> properties = new();
        if (ProjectTargetFramework != null)
            properties["TargetFramework"] = ProjectTargetFramework.GetShortFolderName();

        var proj = new Microsoft.Build.Evaluation.Project(
            projectFile: ProjectPath,
            globalProperties: properties,
            toolsVersion: null);

        foreach (var reference in proj.GetItems(itemType: "PackageReference"))
        {
            var versionMetaData = reference.Metadata.FirstOrDefault(predicate: meta => meta.Name == "Version");
            VersionRange? version_range;
            if (versionMetaData is not null)
            {
                _ = VersionRange.TryParse(value: versionMetaData.EvaluatedValue, versionRange: out version_range);
            }
            else
            {
                if (Config.TRACE)
                    Console.WriteLine($"    Project reference without version: {reference.EvaluatedInclude}");
                version_range = null;
            }

            var dep = new Dependency(
                name: reference.EvaluatedInclude,
                version_range: version_range,
                framework: ProjectTargetFramework,
                is_direct: true);
            dependencies.Add(item: dep);

            if (Config.TRACE)
                Console.WriteLine($"    Add Direct dependcy from PackageReference: name: {dep.name} version_range: {dep.version_range}");
        }

        // Also fetch "legacy" versioned references
        foreach (var reference in proj.GetItems(itemType: "Reference"))
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

                var dep = new Dependency(
                    name: artifact,
                    version_range: VersionRange.Parse(value: version),
                    framework: ProjectTargetFramework,
                    is_direct: true);

                dependencies.Add(item: dep);

                if (Config.TRACE)
                    Console.WriteLine($"    Add Direct dependcy from Reference: name: {dep.name} version_range: {dep.version_range}");
            }
        }

        ProjectCollection.GlobalProjectCollection.UnloadProject(project: proj);
        return dependencies;
    }

    /// <summary>
    /// Resolve the dependencies.
    /// </summary>
    public DependencyResolution ResolveNew()
    {
        if (Config.TRACE)
            Console.WriteLine("\nProjectFileResolver.Resolve: starting resolution");
        try
        {
            var dependencies = GetDependencies();
            var direct_deps = new List<PackageIdentity>();
            foreach (var dep in dependencies)
            {
                PackageSearchMetadataRegistration? psmr = nugetApi.FindPackageVersion(id: dep.name,  versionRange: dep.version_range);
                if (psmr != null)
                {
                    direct_deps.Add(psmr.Identity);
                }
            }

            var spdis = nugetApi.ResolveDeps(
                direct_dependencies: direct_deps,
                framework: ProjectTargetFramework!
            );

            DependencyResolution resolution = new(Success: true);
            foreach (NuGet.Protocol.Core.Types.SourcePackageDependencyInfo spdi in spdis)
            {
                BasePackage dep = new(
                    name: spdi.Id,
                    version: spdi.Version.ToString(),
                    framework: ProjectTargetFramework!.GetShortFolderName());
                resolution.Dependencies.Add(dep);
            }

            return resolution;
        }
        catch (InvalidProjectFileException ex)
        {
            return new DependencyResolution
            {
                Success = false,
                ErrorMessage = ex.ToString()
            };
        }
    }

    public DependencyResolution Resolve()
    {
        if (Config.TRACE)
            Console.WriteLine("\nProjectFileResolver.Resolve: starting resolution");
        try
        {
            var dependencies = GetDependencies();

            var deps_helper = new NugetApiHelper(nugetApi: nugetApi);
            foreach (var dep in dependencies)
            {
                deps_helper.Resolve(packageDependency: dep);
            }

            var result = new DependencyResolution
            {
                Success = true,
                Packages = deps_helper.GetPackageList(),
                Dependencies = new List<BasePackage>()
            };

            foreach (var package in result.Packages)
            {
                var anyPackageReferences =
                    result.Packages.Any(predicate: pkg => pkg.dependencies.Contains(item: package));
                if (!anyPackageReferences && package != null)
                    result.Dependencies.Add(item: package);
            }

            return result;
        }
        catch (InvalidProjectFileException ex)
        {
            return new DependencyResolution
            {
                Success = false,
                ErrorMessage = ex.ToString()
            };
        }
    }
}