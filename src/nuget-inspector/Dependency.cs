using NuGet.Frameworks;
using NuGet.Versioning;

namespace NugetInspector;

public class Dependency
{
    public string? Name;
    public NuGetFramework? Framework;
    public VersionRange? VersionRange;

    public Dependency(string? name, VersionRange? version_range, NuGetFramework? framework = null)
    {
        Framework = framework;
        Name = name;
        VersionRange = version_range;
    }

    public Dependency(NuGet.Packaging.Core.PackageDependency dependency, NuGetFramework? framework)
    {
        Framework = framework;
        Name = dependency.Id;
        VersionRange = dependency.VersionRange;
    }

    /// <summary>
    /// Return an empty PackageSet using this package.
    /// </summary>
    /// <returns></returns>
    public PackageSet ToEmptyPackageSet()
    {
        var package_set = new PackageSet
        {
            PackageId = new BasePackage(
                name: Name,
                version: VersionRange?.MinVersion.ToNormalizedString(),
                framework: Framework?.ToString()
            )
        };
        return package_set;
    }
}