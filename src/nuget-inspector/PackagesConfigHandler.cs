using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// Handles legacy packages.config format originally designed for NuGet projects
/// See https://learn.microsoft.com/en-us/nuget/reference/packages-config
/// https://docs.microsoft.com/en-us/nuget/consume-packages/dependency-resolution#dependency-resolution-with-packagesconfig
/// and https://learn.microsoft.com/en-us/nuget/consume-packages/migrate-packages-config-to-package-reference
/// </summary>
internal class PackagesConfigHandler : IDependencyResolver
{
    public const string DatasourceId = "nuget-packages.config";
    private readonly NugetApi nugetApi;

    private readonly string PackagesConfigPath;

    public PackagesConfigHandler(string packagesConfigPath, NugetApi nugetApi)
    {
        PackagesConfigPath = packagesConfigPath;
        this.nugetApi = nugetApi;
    }

    public DependencyResolution Process()
    {
        var dependencies = GetDependencies();

        var result = new DependencyResolution();
        result.Packages = CreatePackageSets(dependencies: dependencies);

        result.Dependencies = new List<BasePackage?>();
        foreach (var package in result.Packages)
        {
            var anyPackageReferences = result.Packages.Where(predicate: pkg => pkg.Dependencies.Contains(item: package.PackageId)).Any();
            if (!anyPackageReferences && package.PackageId != null)
                result.Dependencies.Add(item: package.PackageId);
        }

        return result;
    }

    private List<Dependency> GetDependencies()
    {
        Stream stream = new FileStream(path: PackagesConfigPath, mode: FileMode.Open, access: FileAccess.Read);
        var reader = new PackagesConfigReader(stream: stream);
        List<PackageReference> packages = reader.GetPackages().ToList();

        var dependencies = new List<Dependency>();

        foreach (var packageRef in packages)
        {
            var name = packageRef.PackageIdentity.Id;
            var version = packageRef.PackageIdentity.Version;
            var range = new VersionRange(
                minVersion: version,
                includeMinVersion: true,
                maxVersion: version,
                includeMaxVersion: true
            );
            var framework = NuGetFramework.Parse(folderName: packageRef.TargetFramework.Framework);

            var dep = new Dependency(name: name, versionRange: range, framework: framework);
            dependencies.Add(item: dep);
        }

        return dependencies;
    }

    private List<PackageSet> CreatePackageSets(List<Dependency> dependencies)
    {
        try
        {
            var resolver = new LegacyPackagesConfigNoDupeResolver(service: nugetApi);
            var packages = resolver.ProcessAll(packages: dependencies);
            return packages;
        }
        catch (Exception flatException)
        {
            if (Config.TRACE)
                Console.WriteLine(value: "There was an issue processing packages.config as flat: " + flatException.Message);
            try
            {
                var treeResolver = new NugetApiResolver(nugetApi: nugetApi);
                treeResolver.AddAll(packages: dependencies);
                return treeResolver.GetPackageList();
            }
            catch (Exception treeException)
            {
                if (Config.TRACE)
                    Console.WriteLine(value: "There was an issue processing packages.config as a tree: " +
                                             treeException.Message);
                var packages = new List<PackageSet>(collection: dependencies.Select(selector: dependency => dependency.ToEmptyPackageSet()));
                return packages;
            }
        }
    }
}