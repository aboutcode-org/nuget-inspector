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
            Console.WriteLine($"\nNugetApiHelper.ResolveOne: name: {dependency.name} range: {dependency.version_range}");

        if (string.IsNullOrWhiteSpace(dependency.name))
            throw new ArgumentNullException($"Dependency: {dependency} name cannot be null");

        PackageSearchMetadataRegistration? psmr = nugetApi.FindPackageVersion(
            name: dependency.name,
            version_range: dependency.version_range);

        if (psmr == null)
        {
            string? version = dependency.version_range?.MinVersion.ToNormalizedString();
            if (Config.TRACE)
            {
                Console.WriteLine(
                    $"    Failed to find package: '{dependency.name}' "
                    + $"range: '{dependency.version_range}', picking instead min version: '{version}'");
            }

            if (dependency.name != null)
                package_tree.AddOrUpdatePackage(id: new BasePackage(name: dependency.name, type: dependency.type, version: version));
            return;
        }

        var base_package = new BasePackage(
            name: dependency.name!,
            type: dependency.type,
            version: psmr.Identity.Version.ToNormalizedString());

        IEnumerable<NuGet.Packaging.Core.PackageDependency> packages = nugetApi.GetPackageDependenciesForPackage(
            identity: psmr.Identity,
            framework: dependency.framework);

        var dependencies = new List<BasePackage>();
        foreach (var pkg in packages)
        {
            var resolved_version = package_tree.GetResolvedVersion(name: pkg.Id, range: pkg.VersionRange);
            if (resolved_version != null)
            {
                var base_pkg = new BasePackage(name: pkg.Id, type: ComponentType.NuGet, version: resolved_version);
                dependencies.Add(item: base_pkg);
                if (Config.TRACE)
                    Console.WriteLine($"        dependencies.Add name: {pkg.Id}, version: {resolved_version}");
            }
            else
            {
                PackageSearchMetadataRegistration? psrm = nugetApi.FindPackageVersion(
                    name: pkg.Id,
                    version_range: pkg.VersionRange);
                if (psrm == null)
                {
                    if (Config.TRACE)
                        Console.WriteLine($"        Unable to find package for '{pkg.Id}' version '{pkg.VersionRange}'");
                    continue;
                }

                var dependent_package = new BasePackage(
                    name: psrm.Identity.Id,
                    type: ComponentType.NuGet,
                    version: psrm.Identity.Version.ToNormalizedString());

                dependencies.Add(item: dependent_package);

                if (!package_tree.DoesPackageExist(package: dependent_package))
                {
                    Dependency pd = new(
                        name: pkg.Id,
                        type: ComponentType.NuGet,
                        version_range: pkg.VersionRange,
                        framework: dependency.framework);

                    ResolveOne(dependency: pd);
                    if (Config.TRACE)
                        Console.WriteLine($"        ResolveOne: {pkg.Id} range: {pkg.VersionRange}");
                }
            }
        }

        package_tree.AddOrUpdatePackage(base_package: base_package, dependencies: dependencies!);
    }
}