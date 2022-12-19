using NuGet.Protocol.Core.Types;

namespace NugetInspector;

public class NugetApiResolver
{
    private readonly PackageBuilder builder = new();
    private readonly NugetApi nugetApi;

    public NugetApiResolver(NugetApi nugetApi)
    {
        this.nugetApi = nugetApi;
    }

    public List<BasePackage> GetPackageList()
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
            nugetApi.FindPackageVersion(id: packageDependency.name, versionRange: packageDependency.version_range);
        if (package == null)
        {
            var version = packageDependency.version_range?.MinVersion.ToNormalizedString();
            if (Config.TRACE)
                Console.WriteLine(
                    $"Nuget failed to find package: '{packageDependency.name}' with version range: '{packageDependency.version_range}', assuming instead version: '{version}'");

            if (packageDependency.name != null)
                builder.AddOrUpdatePackage(id: new BasePackage(name: packageDependency.name, version: version));
            return;
        }

        var package_id = new BasePackage(
            name: packageDependency.name!,
            version: package.Identity.Version.ToNormalizedString());

        var dependencies = new List<BasePackage>();

        var packages =
            nugetApi.DependenciesForPackage(
                identity: package.Identity, 
                framework: packageDependency.framework);

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
                IPackageSearchMetadata? api_package_metadata = nugetApi.FindPackageVersion(
                    id: dependency.Id, 
                    versionRange: dependency.VersionRange);
                if (api_package_metadata == null)
                {
                    if (Config.TRACE)
                        Console.WriteLine(
                            value: $"Unable to find package for '{dependency.Id}' version '{dependency.VersionRange}'");
                    continue;
                }

                var id = new BasePackage(name: api_package_metadata.Identity.Id,
                    version: api_package_metadata.Identity.Version.ToNormalizedString());
                if (Config.TRACE)
                {
                    Console.WriteLine($"Package details");
                    Console.WriteLine($"    Description: {api_package_metadata.Description}");
                }
                dependencies.Add(item: id);

                if (!builder.DoesPackageExist(package: id))
                    Add(packageDependency: new Dependency(name: dependency.Id, version_range: dependency.VersionRange,
                        framework: packageDependency.framework));
            }
        }


        builder.AddOrUpdatePackage(id: package_id, dependencies: dependencies!);
    }
}