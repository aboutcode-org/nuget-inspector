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
internal class PackagesConfigProcessor : IDependencyProcessor
{
    public const string DatasourceId = "nuget-packages.config";
    private readonly NugetApi nugetApi;

    private readonly string PackagesConfigPath;

    private readonly NuGetFramework project_target_framework;

    public PackagesConfigProcessor(
        string packages_config_path,
        NugetApi nuget_api,
        NuGetFramework project_target_framework)
    {
        PackagesConfigPath = packages_config_path;
        nugetApi = nuget_api;
        this.project_target_framework = project_target_framework;
    }

    /// <summary>
    /// Resolve dependencies for a packages.config file.
    /// A packages.config is a lockfile that contains all the dependencies.
    /// </summary>
    public DependencyResolution Resolve()
    {
        var dependencies = GetDependencies();

        DependencyResolution result = new()
        {
            Packages = CreateBasePackage(dependencies: dependencies),
            Dependencies = new List<BasePackage>()
        };

        foreach (var package in result.Packages)
        {
            var has_package_references = result.Packages.Any(pkg => pkg.dependencies.Contains(item: package));
            if (!has_package_references && package != null)
                result.Dependencies.Add(item: package);
        }

        return result;
    }

    /// <summary>
    /// Return a list of Dependency found in a packages.config file.
    /// Skip packages with a TargetFramework that is not compatible with
    /// the requested Project TargetFramework.
    /// </summary>
    private List<Dependency> GetDependencies()
    {
        Stream stream = new FileStream(
            path: PackagesConfigPath,
            mode: FileMode.Open,
            access: FileAccess.Read);

        PackagesConfigReader reader = new(stream: stream);
        List<PackageReference> packages = reader.GetPackages(allowDuplicatePackageIds: true).ToList();

        var compat = DefaultCompatibilityProvider.Instance;
        var project_framework = this.project_target_framework;

        var dependencies = new List<Dependency>();

        if (Config.TRACE)
            Console.WriteLine("PackagesConfigHandler.GetDependencies");

        foreach (var package in packages)
        {
            var name = package.PackageIdentity.Id;
            var version = package.PackageIdentity.Version;
            NuGetFramework? package_framework = package.TargetFramework;

            if  (package_framework?.IsUnsupported != false)
                package_framework = NuGetFramework.AnyFramework;

            if (Config.TRACE)
                Console.WriteLine($"    for: {name}@{version}  project_framework: {project_framework} package_framework: {package_framework}");

            if  (project_framework?.IsUnsupported == false
                && !compat.IsCompatible(framework: project_framework, other: package_framework))
            {
                if (Config.TRACE)
                    Console.WriteLine("    incompatible frameworks");
                continue;
            }
            var range = new VersionRange(
                minVersion: version,
                includeMinVersion: true,
                maxVersion: version,
                includeMaxVersion: true
            );

            Dependency dep = new(
                name: name,
                version_range: range,
                framework: package_framework,
                is_direct: true);
            dependencies.Add(item: dep);
        }

        return dependencies;
    }

    private List<BasePackage> CreateBasePackage(List<Dependency> dependencies)
    {
        try
        {
            var resolver_helper = new PackagesConfigHelper(nugetApi: nugetApi);
            var packages = resolver_helper.ProcessAll(packages: dependencies);
            return packages;
        }
        catch (Exception listex)
        {
            if (Config.TRACE)
                Console.WriteLine($"PackagesConfigHandler.CreateBasePackage: Failed processing packages.config as list: {listex.Message}");
            try
            {
                var resolver = new NugetApiHelper(nugetApi: nugetApi);
                resolver.AddAll(packages: dependencies);
                return resolver.GetPackageList();
            }
            catch (Exception treeex)
            {
                if (Config.TRACE)
                    Console.WriteLine($"PackagesConfigHandler.CreateBasePackage: TFailed processing packages.config as a tree: {treeex.Message}");
                var packages =
                    new List<BasePackage>(
                        collection: dependencies.Select(selector: dependency => dependency.CreateEmptyBasePackage()));
                return packages;
            }
        }
    }
}