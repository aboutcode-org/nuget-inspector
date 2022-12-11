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
        foreach (var package in packages) Add(packageDependency: package);
    }

    public void Add(Dependency packageDependency)
    {
        IPackageSearchMetadata? package =
            nugetApi.FindPackageVersion(id: packageDependency.Name, versionRange: packageDependency.VersionRange);
        if (package == null)
        {
            var version = packageDependency.VersionRange?.MinVersion.ToNormalizedString();
            if (Config.TRACE)
                Console.WriteLine(
                    value:
                    $"Nuget failed to find package: '{packageDependency.Name}' with version range: '{packageDependency.VersionRange}', assuming instead version: '{version}'");
            builder.AddOrUpdatePackage(id: new BasePackage(name: packageDependency.Name, version: version));
            return;
        }

        var package_id = new BasePackage(name: packageDependency.Name,
            version: package.Identity.Version.ToNormalizedString());
        var dependencies = new HashSet<BasePackage?>();

        var packages =
            nugetApi.DependenciesForPackage(identity: package.Identity, framework: packageDependency.Framework);

        foreach (var dependency in packages)
        {
            var resolved_version = builder.GetResolvedVersion(name: dependency.Id, range: dependency.VersionRange);
            if (resolved_version != null)
            {
                var id = new BasePackage(name: dependency.Id, version: resolved_version);
                dependencies.Add(item: id);
            }
            else
            {
                var depPackage = nugetApi.FindPackageVersion(id: dependency.Id, versionRange: dependency.VersionRange);
                if (depPackage == null)
                {
                    if (Config.TRACE)
                        Console.WriteLine(
                            value: $"Unable to find package for '{dependency.Id}' version '{dependency.VersionRange}'");
                    continue;
                }

                var id = new BasePackage(name: depPackage.Identity.Id,
                    version: depPackage.Identity.Version.ToNormalizedString());
                dependencies.Add(item: id);

                if (!builder.DoesPackageExist(id: id))
                    Add(packageDependency: new Dependency(name: dependency.Id, version_range: dependency.VersionRange,
                        framework: packageDependency.Framework));
            }
        }


        builder.AddOrUpdatePackage(id: package_id, dependencies: dependencies!);
    }
}