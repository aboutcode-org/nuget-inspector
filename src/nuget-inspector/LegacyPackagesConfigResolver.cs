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
internal class LegacyPackagesConfigResolver : IDependencyResolver
{
    public const string DatasourceId = "nuget-packages.config";
    private readonly NugetApi nugetApi;

    private readonly string PackagesConfigPath;

    public LegacyPackagesConfigResolver(string packagesConfigPath, NugetApi nugetApi)
    {
        PackagesConfigPath = packagesConfigPath;
        this.nugetApi = nugetApi;
    }

    public DependencyResolution Process()
    {
        var dependencies = GetDependencies();

        var result = new DependencyResolution();
        result.Packages = CreatePackageSets(dependencies);

        result.Dependencies = new List<PackageId?>();
        foreach (var package in result.Packages)
        {
            var anyPackageReferences = result.Packages.Where(pkg => pkg.Dependencies.Contains(package.PackageId)).Any();
            if (!anyPackageReferences) result.Dependencies.Add(package.PackageId);
        }

        return result;
    }

    private List<Dependency> GetDependencies()
    {
        Stream stream = new FileStream(PackagesConfigPath, FileMode.Open, FileAccess.Read);
        var reader = new PackagesConfigReader(stream);
        List<PackageReference> packages = reader.GetPackages().ToList();

        var dependencies = new List<Dependency>();

        foreach (var packageRef in packages)
        {
            var componentName = packageRef.PackageIdentity.Id;
            var version = packageRef.PackageIdentity.Version;
            var versionRange = new VersionRange(version, true, version, true);
            var framework = NuGetFramework.Parse(packageRef.TargetFramework.Framework);

            var dep = new Dependency(componentName, versionRange, framework);
            dependencies.Add(dep);
        }

        return dependencies;
    }

    private List<PackageSet> CreatePackageSets(List<Dependency> dependencies)
    {
        try
        {
            var resolver = new LegacyPackagesConfigNoDupeResolver(nugetApi);
            var packages = resolver.ProcessAll(dependencies);
            return packages;
        }
        catch (Exception flatException)
        {
            if (Config.TRACE) Console.WriteLine("There was an issue processing packages.config as flat: " + flatException.Message);
            try
            {
                var treeResolver = new NugetApiResolver(nugetApi);
                treeResolver.AddAll(dependencies);
                return treeResolver.GetPackageList();
            }
            catch (Exception treeException)
            {
                if (Config.TRACE) Console.WriteLine("There was an issue processing packages.config as a tree: " + treeException.Message);
                var packages = new List<PackageSet>(dependencies.Select(dependency => dependency.ToEmptyPackageSet()));
                return packages;
            }
        }
    }
}