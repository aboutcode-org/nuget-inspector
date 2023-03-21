using NuGet.Protocol;

namespace NugetInspector;

/// <summary>
/// A helper class to resolve NuGet dependencies as a tree, once at a time.
/// </summary>
public class NugetResolverHelper
{
    private readonly PackageTree package_tree = new();
    private readonly NugetApi nugetApi;

    public NugetResolverHelper(NugetApi nugetApi)
    {
        this.nugetApi = nugetApi;
    }

    public List<BasePackage> GetPackageList()
    {
        return package_tree.GetPackageList();
    }

    public void ResolveManyOneByOne(List<Dependency> dependencies)
    {
        foreach (var dep in dependencies)
        {
            if (Config.TRACE)
                Console.WriteLine($"NugetApiHelper.ResolveManyOneByOne: {dep}");
            ResolveOne(dependency: dep);
        }
    }

    /// <summary>
    /// Resolve a Dependency and add it to the PackageTree.
    /// </summary>
    public void ResolveOne(Dependency dependency)
    {
        if (Config.TRACE)
            Console.WriteLine($"\nNugetApiHelper.ResolveOne: FOR name: {dependency.name} range: {dependency.version_range}");

        PackageSearchMetadataRegistration? package = nugetApi.FindPackageVersion(
            id: dependency.name,
            versionRange: dependency.version_range);

        if (package == null)
        {
            string? version = dependency.version_range?.MinVersion.ToNormalizedString();
            if (Config.TRACE)
            {
                Console.WriteLine(
                    $"    Failed to find package: '{dependency.name}' "
                    + $"range: '{dependency.version_range}', picking instead min version: '{version}'");
            }

            if (dependency.name != null)
                package_tree.AddOrUpdatePackage(id: new BasePackage(name: dependency.name, version: version));
            return;
        }

        var package_id = new BasePackage(
            name: dependency.name!,
            version: package.Identity.Version.ToNormalizedString());

        IEnumerable<NuGet.Packaging.Core.PackageDependency> packages = nugetApi.GetPackageDependenciesForPackage(
            identity: package.Identity,
            framework: dependency.framework);

        var dependencies = new List<BasePackage>();
        foreach (var pkg in packages)
        {
            var resolved_version = package_tree.GetResolvedVersion(name: pkg.Id, range: pkg.VersionRange);
            if (resolved_version != null)
            {
                var id = new BasePackage(name: pkg.Id, version: resolved_version);
                dependencies.Add(item: id);
                if (Config.TRACE)
                    Console.WriteLine($"        dependencies.Add name: {pkg.Id}, version: {resolved_version}");
            }
            else
            {
                PackageSearchMetadataRegistration? api_package_metadata = nugetApi.FindPackageVersion(
                    id: pkg.Id,
                    versionRange: pkg.VersionRange);
                if (api_package_metadata == null)
                {
                    if (Config.TRACE)
                        Console.WriteLine($"        Unable to find package for '{pkg.Id}' version '{pkg.VersionRange}'");
                    continue;
                }

                var base_package = new BasePackage(
                    name: api_package_metadata.Identity.Id,
                    version: api_package_metadata.Identity.Version.ToNormalizedString());

                dependencies.Add(item: base_package);

                if (!package_tree.DoesPackageExist(package: base_package))
                {
                    Dependency pd = new(
                        name: pkg.Id,
                        version_range: pkg.VersionRange,
                        framework: dependency.framework);

                    ResolveOne(dependency: pd);
                    if (Config.TRACE)
                        Console.WriteLine($"        ResolveOne: {pkg.Id} range: {pkg.VersionRange}");
                }
            }
        }

        package_tree.AddOrUpdatePackage(base_package: package_id, dependencies: dependencies!);
    }
}