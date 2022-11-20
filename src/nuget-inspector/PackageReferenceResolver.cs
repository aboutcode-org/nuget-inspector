using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// See https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files
/// </summary>
internal class PackageReferenceResolver : IDependencyResolver
{
    public const string DatasourceId = "dotnet-project-reference";
    private readonly NuGetFramework? ProjectTargetFramework;
    private readonly NugetApi nugetApi;

    private readonly string ProjectPath;

    public PackageReferenceResolver(
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
            var tree = new NugetApiResolver(nugetApi);
            var proj = new Microsoft.Build.Evaluation.Project(ProjectPath);
            var deps = new List<Dependency>();
            foreach (var reference in proj.GetItemsIgnoringCondition("PackageReference"))
            {
                var versionMetaData = reference.Metadata.Where(meta => meta.Name == "Version").FirstOrDefault();
                VersionRange? version;
                if (versionMetaData is not null && VersionRange.TryParse(versionMetaData.EvaluatedValue, out version))
                {
                    var dep = new Dependency(reference.EvaluatedInclude, version, ProjectTargetFramework);
                    deps.Add(dep);
                }
                else
                {
                    if (Config.TRACE) Console.WriteLine("Framework dependency had no version, will not be included: " +
                                                        reference.EvaluatedInclude);
                }
            }

            foreach (var reference in proj.GetItemsIgnoringCondition("Reference"))
                if (reference.Xml != null && !string.IsNullOrWhiteSpace(reference.Xml.Include) &&
                    reference.Xml.Include.Contains("Version="))
                {
                    var packageInfo = reference.Xml.Include;

                    var comma = packageInfo.IndexOf(",", StringComparison.Ordinal);
                    var artifact = packageInfo.Substring(0, comma);

                    var versionKey = "Version=";
                    var versionKeyIndex = packageInfo.IndexOf(versionKey, StringComparison.Ordinal);
                    var versionStartIndex = versionKeyIndex + versionKey.Length;
                    var packageInfoAfterVersionKey = packageInfo.Substring(versionStartIndex);

                    string version;
                    if (packageInfoAfterVersionKey.Contains(","))
                    {
                        var firstSep = packageInfoAfterVersionKey.IndexOf(",", StringComparison.Ordinal);
                        version = packageInfoAfterVersionKey.Substring(0, firstSep);
                    }
                    else
                    {
                        version = packageInfoAfterVersionKey;
                    }

                    var dep = new Dependency(artifact, VersionRange.Parse(version), ProjectTargetFramework);
                    deps.Add(dep);
                }

            ProjectCollection.GlobalProjectCollection.UnloadProject(proj);

            foreach (var dep in deps) tree.Add(dep);

            var result = new DependencyResolution
            {
                Success = true,
                Packages = tree.GetPackageList(),
                Dependencies = new List<PackageId>()
            };

            foreach (var package in result.Packages)
            {
                var anyPackageReferences =
                    result.Packages.Any(pkg => pkg.Dependencies.Contains(package.PackageId));
                if (!anyPackageReferences)
                    if (package.PackageId != null)
                        result.Dependencies.Add(package.PackageId);
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