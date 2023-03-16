using NuGet.Protocol;

namespace NugetInspector;

public class NugetApiHelper
{
    private readonly PackageTree package_tree = new();
    private readonly NugetApi nugetApi;

    public NugetApiHelper(NugetApi nugetApi)
    {
        this.nugetApi = nugetApi;
    }

    public List<BasePackage> GetPackageList()
    {
        return package_tree.GetPackageList();
    }

    public void AddAll(List<Dependency> packages)
    {
        foreach (var package in packages)
        {
            if (Config.TRACE)
                Console.WriteLine($"NugetApiResolver.AddAll: {package}");
            Resolve(packageDependency: package);
        }
    }

    /// <summary>
    /// Resolve a Dependency and add to the PackageTree.
    /// </summary>
    public void Resolve(Dependency packageDependency)
    {
        if (Config.TRACE)
            Console.WriteLine($"\nNugetApiResolver.Add: FOR name: {packageDependency.name} range: {packageDependency.version_range}");

        PackageSearchMetadataRegistration? package = nugetApi.FindPackageVersion(
            id: packageDependency.name,
            versionRange: packageDependency.version_range);
        if (package == null)
        {
            string? version = packageDependency.version_range?.MinVersion.ToNormalizedString();
            if (Config.TRACE)
            {
                Console.WriteLine(
                    $"    Failed to find package: '{packageDependency.name}' "
                    + $"range: '{packageDependency.version_range}', picking instead min version: '{version}'");
            }

            if (packageDependency.name != null)
                package_tree.AddOrUpdatePackage(id: new BasePackage(name: packageDependency.name, version: version));
            return;
        }

        var package_id = new BasePackage(
            name: packageDependency.name!,
            version: package.Identity.Version.ToNormalizedString());

        var dependencies = new List<BasePackage>();

        IEnumerable<NuGet.Packaging.Core.PackageDependency> packages = nugetApi.DependenciesForPackage(
            identity: package.Identity,
            framework: packageDependency.framework);

        foreach (var dependency in packages)
        {
            var resolved_version = package_tree.GetResolvedVersion(name: dependency.Id, range: dependency.VersionRange);
            if (resolved_version != null)
            {
                var id = new BasePackage(name: dependency.Id, version: resolved_version);
                dependencies.Add(item: id);
                if (Config.TRACE)
                    Console.WriteLine($"        dependencies.Add name: {dependency.Id}, version: {resolved_version}");
            }
            else
            {
                PackageSearchMetadataRegistration? api_package_metadata = nugetApi.FindPackageVersion(
                    id: dependency.Id,
                    versionRange: dependency.VersionRange);
                if (api_package_metadata == null)
                {
                    if (Config.TRACE)
                        Console.WriteLine($"        Unable to find package for '{dependency.Id}' version '{dependency.VersionRange}'");
                    continue;
                }

                var base_package = new BasePackage(
                    name: api_package_metadata.Identity.Id,
                    version: api_package_metadata.Identity.Version.ToNormalizedString());

                dependencies.Add(item: base_package);

                if (!package_tree.DoesPackageExist(package: base_package))
                {
                    Dependency pd = new(
                        name: dependency.Id,
                        version_range: dependency.VersionRange,
                        framework: packageDependency.framework);
                    Resolve(packageDependency: pd);
                    if (Config.TRACE)
                        Console.WriteLine($"        Add: {dependency.Id} range: {dependency.VersionRange}");
                }
            }
        }

        package_tree.AddOrUpdatePackage(base_package: package_id, dependencies: dependencies!);
    }
}