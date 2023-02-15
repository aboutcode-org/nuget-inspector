using NuGet.ProjectModel;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// Handle legacy and new style lock files (project.assets.json)
/// See https://learn.microsoft.com/en-us/nuget/archive/project-json#projectlockjson
/// See https://kimsereyblog.blogspot.com/2018/08/sdk-style-project-and-projectassetsjson.html
/// See https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build?tabs=netcore2x#implicit-restore
/// </summary>
public class LockFileHandler
{
    private readonly LockFile LockFile;

    public LockFileHandler(LockFile lockFile)
    {
        LockFile = lockFile;
    }

    private static NuGetVersion BestVersion(string name, VersionRange range, IList<LockFileTargetLibrary> libraries)
    {
        var versions = libraries.Where(predicate: lib => lib.Name == name).Select(selector: lib => lib.Version);
        var nuGetVersions = versions.ToList();
        var bestMatch = range.FindBestMatch(versions: nuGetVersions);
        if (bestMatch == null)
        {
            if (nuGetVersions.Count == 1)
                return nuGetVersions[0];

            if (Config.TRACE)
            {
                Console.WriteLine($"WARNING: Unable to find a version to satisfy range {range.PrettyPrint()} for the dependency {name}");
                Console.WriteLine($"Instead will return the minimum range demanded: {range.MinVersion.ToFullString()}");
            }

            return range.MinVersion;
        }

        return bestMatch;
    }

    private static NuGetVersion BestLibraryVersion(string? name, VersionRange range, IList<LockFileLibrary> libraries)
    {
        var versions = libraries.Where(predicate: lib => lib.Name == name).Select(selector: lib => lib.Version);
        var nuGetVersions = versions.ToList();
        var bestMatch = range.FindBestMatch(versions: nuGetVersions);
        if (bestMatch == null)
        {
            if (nuGetVersions.Count == 1)
                return nuGetVersions[0];

            if (Config.TRACE)
                Console.WriteLine($"WARNING: Unable to find a version to satisfy range {range.PrettyPrint()} for the dependency {name}");

            if (range.HasUpperBound && !range.HasLowerBound)
            {
                if (Config.TRACE)
                    Console.WriteLine($"Instead will return the maximum range demanded: {range.MaxVersion.ToFullString()}");

                return range.MaxVersion;
            }

            if (Config.TRACE)
                Console.WriteLine($"Instead will return the minimum range demanded: {range.MinVersion.ToFullString()}");
            return range.MinVersion;
        }

        return bestMatch;
    }

    public DependencyResolution Process()
    {
        var builder = new PackageBuilder();
        var result = new DependencyResolution();

        foreach (var target in LockFile.Targets)
        {
            foreach (var library in target.Libraries)
            {
                var name = library.Name;
                var version = library.Version.ToNormalizedString();
                var packageId = new BasePackage(name: name, version: version);

                var dependencies = new List<BasePackage?>();
                foreach (var dependency in library.Dependencies)
                {
                    var dep_id = dependency.Id;
                    var vr = dependency.VersionRange;
                    //vr.Float.FloatBehavior = NuGet.Versioning.NuGetVersionFloatBehavior.
                    var lb = target.Libraries;
                    var bs = BestVersion(name: dep_id, range: vr, libraries: lb);
                    if (bs == null)
                    {
                        if (Config.TRACE)
                            Console.WriteLine(dependency.Id);
                        _ = BestVersion(name: dep_id, range: vr, libraries: lb);
                    }
                    else
                    {
                        var depId = new BasePackage(name: dep_id, version: bs.ToNormalizedString());
                        dependencies.Add(item: depId);
                    }
                }

                builder.AddOrUpdatePackage(id: packageId, dependencies: dependencies);
            }
        }

        if (Config.TRACE)
        {
            Console.WriteLine($"LockFile: {LockFile}");
            Console.WriteLine($"LockFile.Path: {LockFile.Path}");
        }

        if (LockFile?.PackageSpec?.Dependencies != null
            && LockFile.PackageSpec.Dependencies.Count != 0)
        {
            foreach (var dep in LockFile.PackageSpec.Dependencies)
            {
                var version = builder.GetResolvedVersion(name: dep.Name, range: dep.LibraryRange.VersionRange);
                result.Dependencies.Add(item: new BasePackage(name: dep.Name, version: version));
            }
        }
        else
        {
            if (Config.TRACE)
            {
                Console.WriteLine($"LockFile.PackageSpec: {LockFile?.PackageSpec}");
                Console.WriteLine(
                    value: $"LockFile.PackageSpec.TargetFrameworks: {LockFile?.PackageSpec?.TargetFrameworks}");
            }

            var target_frameworks = LockFile?.PackageSpec?.TargetFrameworks;

            if (target_frameworks != null)
            {
                foreach (var framework in target_frameworks)
                {
                    foreach (var dep in framework.Dependencies)
                    {
                        var version = builder.GetResolvedVersion(name: dep.Name, range: dep.LibraryRange.VersionRange);
                        result.Dependencies.Add(item: new BasePackage(name: dep.Name, version: version));
                    }
                }
            }
        }

        if (LockFile != null)
        {
            foreach (var project_file_dependency_group in LockFile.ProjectFileDependencyGroups)
            {
                foreach (var project_file_dependency in project_file_dependency_group.Dependencies)
                {
                    var project_dependency =
                        ParseProjectFileDependencyGroup(project_file_dependency: project_file_dependency);
                    var library_version = BestLibraryVersion(name: project_dependency.GetName(),
                        range: project_dependency.GetVersionRange(), libraries: LockFile.Libraries);
                    string? version = null;
                    if (library_version != null)
                    {
                        version = library_version.ToNormalizedString();
                    }

                    result.Dependencies.Add(
                        item: new BasePackage(name: project_dependency.GetName()!, version: version));
                }
            }

            if (result.Dependencies.Count == 0 && Config.TRACE)
            {
                Console.WriteLine($"Found no dependencies fo r lock file: {LockFile.Path}");
            }
        }

        result.Packages = builder.GetPackageList();
        return result;
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