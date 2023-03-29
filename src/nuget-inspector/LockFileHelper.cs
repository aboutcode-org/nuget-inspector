using NuGet.ProjectModel;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// Parse legacy style (project-lock.json) and new style lock files (project.assets.json)
/// See https://learn.microsoft.com/en-us/nuget/archive/project-json#projectlockjson
/// See https://kimsereyblog.blogspot.com/2018/08/sdk-style-project-and-projectassetsjson.html
/// See https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build?tabs=netcore2x#implicit-restore
/// </summary>
public class LockFileHelper
{
    private readonly LockFile ProjectLockFile;

    public LockFileHelper(LockFile lockfile)
    {
        ProjectLockFile = lockfile;
    }

    private static NuGetVersion GetBestVersion(string name, VersionRange range, IList<LockFileTargetLibrary> libraries)
    {
        var versions = libraries.Where(predicate: lib => lib.Name == name).Select(selector: lib => lib.Version).ToList();
        var best_version = range.FindBestMatch(versions: versions);
        if (best_version != null)
            return best_version;

        if (versions.Count == 1)
            return versions[0];

        if (Config.TRACE_DEEP)
        {
            Console.WriteLine($"GetBestVersion: WARNING: Unable to find a '{name}' version that satisfies range {range.PrettyPrint()}");
            Console.WriteLine($"    Using min version in range: {range.MinVersion.ToFullString()}");
        }

        return range.MinVersion;
    }

    private static NuGetVersion GetBestLibraryVersion(string? name, VersionRange range, IList<LockFileLibrary> libraries)
    {
        var versions = libraries.Where(predicate: lib => lib.Name == name).Select(selector: lib => lib.Version).ToList();
        var best_version = range.FindBestMatch(versions: versions);
        if (best_version != null)
            return best_version;

        if (versions.Count == 1)
            return versions[0];

        if (Config.TRACE)
            Console.WriteLine($"GetBestLibraryVersion: WARNING: Unable to find a '{name}' version that satisfies range {range.PrettyPrint()}");

        if (range.HasUpperBound && !range.HasLowerBound)
        {
            if (Config.TRACE)
                Console.WriteLine($"    Using max version in range: {range.MaxVersion.ToFullString()}");

            return range.MaxVersion;
        }

        if (Config.TRACE)
            Console.WriteLine($"    Using min version in range: {range.MinVersion.ToFullString()}");
        return range.MinVersion;
    }

    public DependencyResolution Process()
    {
        var tree_builder = new PackageTree();
        var resolution = new DependencyResolution();

        foreach (var target in ProjectLockFile.Targets)
        {
            foreach (var library in target.Libraries)
            {
                var name = library.Name;
                var version = library.Version.ToNormalizedString();
                var package = new BasePackage(name: name, version: version);
                var dependencies = new List<BasePackage>();
                foreach (var dependency in library.Dependencies)
                {
                    var dep_name = dependency.Id;
                    var dep_version_range = dependency.VersionRange;
                    //vr.Float.FloatBehavior = NuGet.Versioning.NuGetVersionFloatBehavior.
                    var libraries = target.Libraries;
                    var best_version = GetBestVersion(name: dep_name, range: dep_version_range, libraries: libraries);
                    if (best_version == null)
                    {
                        if (Config.TRACE) Console.WriteLine(dependency.Id);
                        _ = GetBestVersion(name: dep_name, range: dep_version_range, libraries: libraries);
                    }
                    else
                    {
                        var depId = new BasePackage(name: dep_name, version: best_version.ToNormalizedString());
                        dependencies.Add(item: depId);
                    }
                }

                tree_builder.AddOrUpdatePackage(base_package: package, dependencies: dependencies);
            }
        }

        if (Config.TRACE)
        {
            Console.WriteLine($"LockFile: {ProjectLockFile}");
            Console.WriteLine($"LockFile.Path: {ProjectLockFile.Path}");
        }

        if (ProjectLockFile?.PackageSpec?.Dependencies != null
            && ProjectLockFile.PackageSpec.Dependencies.Count > 0)
        {
            foreach (var dep in ProjectLockFile.PackageSpec.Dependencies)
            {
                var version = tree_builder.GetResolvedVersion(name: dep.Name, range: dep.LibraryRange.VersionRange);
                resolution.Dependencies.Add(item: new BasePackage(name: dep.Name, version: version));
            }
        }
        else
        {
            if (Config.TRACE)
            {
                Console.WriteLine($"LockFile.PackageSpec: {ProjectLockFile?.PackageSpec}");
                Console.WriteLine(
                    value: $"LockFile.PackageSpec.TargetFrameworks: {ProjectLockFile?.PackageSpec?.TargetFrameworks}");
            }

            var target_frameworks = ProjectLockFile?.PackageSpec?.TargetFrameworks ?? new List<TargetFrameworkInformation>();
            foreach (var framework in target_frameworks)
            {
                foreach (var dep in framework.Dependencies)
                {
                    var version = tree_builder.GetResolvedVersion(name: dep.Name, range: dep.LibraryRange.VersionRange);
                    resolution.Dependencies.Add(item: new BasePackage(name: dep.Name, version: version));
                }
            }
        }

        if (ProjectLockFile == null)
        {
            return resolution;
        }

        foreach (var dependency_group in ProjectLockFile.ProjectFileDependencyGroups)
        {
            foreach (var dependency in dependency_group.Dependencies)
            {
                var project_dependency = ParseProjectFileDependencyGroup(project_file_dependency: dependency);
                var library_version = GetBestLibraryVersion(name: project_dependency.GetName(),
                    range: project_dependency.GetVersionRange(), libraries: ProjectLockFile.Libraries);
                string? version = null;
                if (library_version != null)
                {
                    version = library_version.ToNormalizedString();
                }

                resolution.Dependencies.Add(
                    item: new BasePackage(name: project_dependency.GetName()!, version: version));
            }
        }

        if (resolution.Dependencies.Count == 0 && Config.TRACE)
        {
            Console.WriteLine($"Found no dependencies for lock file: {ProjectLockFile.Path}");
        }
        return resolution;
    }

    /// <summary>
    /// Parse a ProjectFile DependencyGroup
    /// </summary>
    /// <param name="project_file_dependency"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    ///
    /// See: https://github.com/NuGet/NuGet.Client/blob/538727480d93b7d8474329f90ccb9ff3b3543714/nuget-inspector/NuGet.Core/NuGet.LibraryModel/LibraryRange.cs#L68
    /// FIXME: there are some rare cases where we have multiple constraints as in this JSON snippet for FSharp.Core:
    /// "projectFileDependencyGroups": {
    ///     ".NETFramework,Version=v4.7.2": ["FSharp.Core >= 4.3.4 < 5.0.0",]
    /// },
    /// This a case that is NOT handled yet
    public static ProjectFileDependency ParseProjectFileDependencyGroup(string project_file_dependency)
    {
        if (ParseProjectFileDependencyGroupTokens(
                input: project_file_dependency,
                tokens: " >= ",
                project_name: out var project_name,
                project_version: out var version_raw))
        {
            return new ProjectFileDependency(
                name: project_name,
                version_range: MinVersionOrFloat(
                    version_value_raw: version_raw,
                    include_min: true));
        }

        if (ParseProjectFileDependencyGroupTokens(
                input: project_file_dependency,
                tokens: " > ",
                project_name: out var project_name2,
                project_version: out var version_raw2))
        {
            return new ProjectFileDependency(
                name: project_name2,
                version_range: MinVersionOrFloat(
                    version_value_raw: version_raw2,
                    include_min: false));
        }

        if (ParseProjectFileDependencyGroupTokens(
                input: project_file_dependency,
                tokens: " <= ",
                project_name: out var project_name3,
                project_version: out var version_raw3))
        {
            var maxVersion = NuGetVersion.Parse(value: version_raw3);
            return new ProjectFileDependency(
                name: project_name3,
                version_range: new VersionRange(
                    minVersion: null,
                    includeMinVersion: false,
                    maxVersion: maxVersion,
                    includeMaxVersion: true));
        }

        if (ParseProjectFileDependencyGroupTokens(
                input: project_file_dependency,
                tokens: " < ",
                project_name: out var project_name4,
                project_version: out var version_raw4))
        {
            var maxVersion = NuGetVersion.Parse(value: version_raw4);
            return new ProjectFileDependency(
                name: project_name4,
                version_range: new VersionRange(
                    minVersion: null,
                    includeMinVersion: false,
                    maxVersion: maxVersion));
        }

        throw new Exception(message:
            $"Unable to parse project file dependency group: {project_file_dependency}");
    }

    private static bool ParseProjectFileDependencyGroupTokens(string input, string tokens, out string? project_name,
        out string? project_version)
    {
        if (input.Contains(value: tokens))
        {
            var pieces = input.Split(separator: tokens);
            project_name = pieces[0].Trim();
            project_version = pieces[1].Trim();
            return true;
        }

        project_name = null;
        project_version = null;
        return false;
    }

    private static VersionRange MinVersionOrFloat(string? version_value_raw, bool include_min)
    {
        //could be Floating or MinVersion
        if (NuGetVersion.TryParse(value: version_value_raw, version: out var min_version))
            return new VersionRange(minVersion: min_version, includeMinVersion: include_min);
        return VersionRange.Parse(value: version_value_raw, allowFloating: true);
    }

    public class ProjectFileDependency
    {
        private readonly string? Name;
        private readonly VersionRange VersionRange;

        public ProjectFileDependency(string? name, VersionRange version_range)
        {
            Name = name;
            VersionRange = version_range;
        }

        public string? GetName()
        {
            return Name;
        }

        public VersionRange GetVersionRange()
        {
            return VersionRange;
        }
    }
}