using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// See https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files
/// This handler reads a *.*proj file using MSBuild readers and calls the NuGet API for resolution. 
/// </summary>
internal class ProjFileStandardPackageReferenceHandler : IDependencyResolver
{
    public const string DatasourceId = "dotnet-project-reference";
    private readonly NuGetFramework? ProjectTargetFramework;
    private readonly NugetApi nugetApi;

    private readonly string ProjectPath;

    public ProjFileStandardPackageReferenceHandler(
        string projectPath,
        NugetApi nugetApi,
        NuGetFramework? projectTargetFramework)
    {
        ProjectPath = projectPath;
        this.nugetApi = nugetApi;
        ProjectTargetFramework = projectTargetFramework;
    }

    public DependencyResolution Process()
    {
        try
        {
            var tree = new NugetApiResolver(nugetApi: nugetApi);
            var proj = new Microsoft.Build.Evaluation.Project(projectFile: ProjectPath);
            var deps = new List<Dependency>();
            foreach (var reference in proj.GetItemsIgnoringCondition(itemType: "PackageReference"))
            {
                var versionMetaData = reference.Metadata
                    .Where(predicate: meta => meta.Name == "Version")
                    .FirstOrDefault();
                VersionRange? version_range;
                if (versionMetaData is not null)
                {
                    VersionRange.TryParse(value: versionMetaData.EvaluatedValue, versionRange: out version_range);
                }
                else
                {
                    if (Config.TRACE)
                        Console.WriteLine($"Project reference without version: {reference.EvaluatedInclude}");
                    version_range = null;
                }
                var dep = new Dependency(
                    name: reference.EvaluatedInclude, 
                    version_range: version_range,
                    framework: ProjectTargetFramework);
                deps.Add(item: dep);
            }

            foreach (var reference in proj.GetItemsIgnoringCondition(itemType: "Reference"))
                if (reference.Xml != null && !string.IsNullOrWhiteSpace(value: reference.Xml.Include) &&
                    reference.Xml.Include.Contains(value: "Version="))
                {
                    var packageInfo = reference.Xml.Include;

                    var comma = packageInfo.IndexOf(value: ",", comparisonType: StringComparison.Ordinal);
                    var artifact = packageInfo.Substring(startIndex: 0, length: comma);

                    var versionKey = "Version=";
                    var versionKeyIndex =
                        packageInfo.IndexOf(value: versionKey, comparisonType: StringComparison.Ordinal);
                    var versionStartIndex = versionKeyIndex + versionKey.Length;
                    var packageInfoAfterVersionKey = packageInfo.Substring(startIndex: versionStartIndex);

                    string version;
                    if (packageInfoAfterVersionKey.Contains(value: ","))
                    {
                        var firstSep =
                            packageInfoAfterVersionKey.IndexOf(value: ",", comparisonType: StringComparison.Ordinal);
                        version = packageInfoAfterVersionKey.Substring(startIndex: 0, length: firstSep);
                    }
                    else
                    {
                        version = packageInfoAfterVersionKey;
                    }

                    var dep = new Dependency(name: artifact, version_range: VersionRange.Parse(value: version),
                        framework: ProjectTargetFramework);
                    deps.Add(item: dep);
                }

            ProjectCollection.GlobalProjectCollection.UnloadProject(project: proj);

            foreach (var dep in deps) tree.Add(packageDependency: dep);

            var result = new DependencyResolution
            {
                Success = true,
                Packages = tree.GetPackageList(),
                Dependencies = new List<BasePackage>()
            };

            foreach (var package in result.Packages)
            {
                var anyPackageReferences =
                    result.Packages.Any(predicate: pkg => pkg.dependencies.Contains(item: package.package));
                if (!anyPackageReferences)
                    if (package.package != null)
                        result.Dependencies.Add(item: package.package);
            }

            return result;
        }
        catch (InvalidProjectFileException)
        {
            return new DependencyResolution
            {
                Success = false
            };
        }
    }
}