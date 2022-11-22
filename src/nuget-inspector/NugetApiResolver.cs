using NuGet.Protocol.Core.Types;

namespace NugetInspector;

public class NugetApiResolver
{
    private readonly PackageSetBuilder builder = new();
    private readonly NugetApi nugetApi;

    public NugetApiResolver(NugetApi nugetApi)
    {
        this.nugetApi = nugetApi;
    }

    public List<PackageSet> GetPackageList()
    {
        return builder.GetPackageList();
    }

    public void AddAll(List<Dependency> packages)
    {
        foreach (var package in packages) Add(package);
    }

    public void Add(Dependency packageDependency)
    {
        IPackageSearchMetadata? package =
            nugetApi.FindPackageVersion(packageDependency.Name, packageDependency.VersionRange);
        if (package == null)
        {
            var version = packageDependency.VersionRange?.MinVersion.ToNormalizedString();
            if (Config.TRACE)
                Console.WriteLine(
                    $"Nuget failed to find package: '{packageDependency.Name}' "
                    + $"with version range: '{packageDependency.VersionRange}', "
                    + $"assuming instead version: '{version}'");
            builder.AddOrUpdatePackage(id: new BasePackage(packageDependency.Name, version));
            return;
        }

        var packageId = new BasePackage(packageDependency.Name, package.Identity.Version.ToNormalizedString());
        var dependencies = new HashSet<BasePackage?>();

        var packages = nugetApi.DependenciesForPackage(package.Identity, packageDependency.Framework);

        foreach (var dependency in packages)
        {
            var bestExisting = builder.GetResolvedVersion(dependency.Id, dependency.VersionRange);
            if (bestExisting != null)
            {
                var id = new BasePackage(dependency.Id, bestExisting);
                dependencies.Add(id);
            }
            else
            {
                var depPackage = nugetApi.FindPackageVersion(dependency.Id, dependency.VersionRange);
                if (depPackage == null)
                {
                    if (Config.TRACE)
                        Console.WriteLine(
                            $"Unable to find package for '{dependency.Id}' version '{dependency.VersionRange}'");
                    continue;
                }

                var id = new BasePackage(depPackage.Identity.Id, depPackage.Identity.Version.ToNormalizedString());
                dependencies.Add(id);

                if (!builder.DoesPackageExist(id))
                    Add(new Dependency(dependency.Id, dependency.VersionRange, packageDependency.Framework));
            }
        }


        builder.AddOrUpdatePackage(packageId, dependencies);
    }
}