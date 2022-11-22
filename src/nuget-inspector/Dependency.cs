using NuGet.Frameworks;
using NuGet.Versioning;

namespace NugetInspector;

public class Dependency
{
    public NuGetFramework? Framework;
    public string? Name;
    public VersionRange? VersionRange;

    public Dependency(string? name, VersionRange? versionRange, NuGetFramework? framework = null)
    {
        Framework = framework;
        Name = name;
        VersionRange = versionRange;
    }

    public Dependency(NuGet.Packaging.Core.PackageDependency dependency, NuGetFramework? framework)
    {
        Framework = framework;
        Name = dependency.Id;
        VersionRange = dependency.VersionRange;
    }

    /// <summary>
    /// Return an PackageSet using this package.
    /// </summary>
    /// <returns></returns>
    public PackageSet ToEmptyPackageSet()
    {
        var packageSet = new PackageSet
        {
            PackageId = new BasePackage(Name, VersionRange?.MinVersion.ToNormalizedString(), Framework?.ToString())
        };
        return packageSet;
    }
}