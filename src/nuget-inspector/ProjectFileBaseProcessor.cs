using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// A base class for .*proj-files based processing, e.g., the current "modern" style.
/// </summary>
public class ProjectFileBaseProcessor : IDependencyProcessor
{
    public NuGetFramework? ProjectTargetFramework;
    public NugetApi nugetApi;
    public string ProjectPath;

    public ProjectFileBaseProcessor(
        string projectPath,
        NugetApi nugetApi,
        NuGetFramework? project_target_framework)
    {
        ProjectPath = projectPath;
        this.nugetApi = nugetApi;
        ProjectTargetFramework = project_target_framework;
    }

    /// <summary>
    /// Return a list of Dependency extracted from the project file
    /// using a project model.
    /// </summary>
    public List<Dependency> GetDependencies()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Resolve the dependencies.
    /// </summary>
    public DependencyResolution Resolve()
    {
        if (Config.TRACE)
            Console.WriteLine("\nProjectFileResolver.Resolve: starting resolution");
        try
        {
            var deps_helper = new NugetApiHelper(nugetApi: nugetApi);
            foreach (var dep in GetDependencies())
            {
                deps_helper.Resolve(packageDependency: dep);
            }

            var resolution = new DependencyResolution
            {
                Success = true,
                Packages = deps_helper.GetPackageList(),
                Dependencies = new List<BasePackage>()
            };

            foreach (var package in resolution.Packages)
            {
                var references = resolution.Packages.Any(
                    predicate: pkg => pkg.dependencies.Contains(item: package));
                if (!references && package != null)
                    resolution.Dependencies.Add(item: package);
            }
            return resolution;
        }
        catch (Exception ex)
        {
            return new DependencyResolution
            {
                Success = false,
                ErrorMessage = ex.ToString()
            };
        }
    }

    /// <summary>
    /// Resolve the dependencies.
    /// </summary>
    public DependencyResolution ResolveNew()
    {
        if (Config.TRACE)
            Console.WriteLine("\nProjectFileResolver.Resolve: starting resolution");
        try
        {
            var dependencies = GetDependencies();
            var direct_deps = new List<PackageIdentity>();
            foreach (var dep in dependencies)
            {
                PackageSearchMetadataRegistration? psmr = nugetApi.FindPackageVersion(id: dep.name,  versionRange: dep.version_range);
                if (psmr != null)
                {
                    direct_deps.Add(psmr.Identity);
                }
            }

            var spdis = nugetApi.ResolveDeps(
                direct_dependencies: direct_deps,
                framework: ProjectTargetFramework!
            );

            DependencyResolution resolution = new(Success: true);
            foreach (NuGet.Protocol.Core.Types.SourcePackageDependencyInfo spdi in spdis)
            {
                BasePackage dep = new(
                    name: spdi.Id,
                    version: spdi.Version.ToString(),
                    framework: ProjectTargetFramework!.GetShortFolderName());
                resolution.Dependencies.Add(dep);
            }

            return resolution;
        }
        catch (InvalidProjectFileException ex)
        {
            return new DependencyResolution
            {
                Success = false,
                ErrorMessage = ex.ToString()
            };
        }
    }
}