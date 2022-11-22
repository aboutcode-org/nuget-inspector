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

    private NuGetVersion BestVersion(string name, VersionRange range, IList<LockFileTargetLibrary> libraries)
    {
        var versions = libraries.Where(predicate: lib => lib.Name == name).Select(selector: lib => lib.Version);
        var nuGetVersions = versions.ToList();
        var bestMatch = range.FindBestMatch(versions: nuGetVersions);
        if (bestMatch == null)
        {
            if (nuGetVersions.Count() == 1) return nuGetVersions.First();

            if (Config.TRACE)
                Console.WriteLine(
                    value: $"WARNING: Unable to find a version to satisfy range {range.PrettyPrint()} for the dependency {name}");
            if (Config.TRACE)
                Console.WriteLine(
                    value: $"Instead will return the minimum range demanded: {range.MinVersion.ToFullString()}");
            return range.MinVersion;
        }

        return bestMatch;
    }

    private NuGetVersion BestLibraryVersion(string? name, VersionRange range, IList<LockFileLibrary> libraries)
    {
        var versions = libraries.Where(predicate: lib => lib.Name == name).Select(selector: lib => lib.Version);
        var nuGetVersions = versions.ToList();
        var bestMatch = range.FindBestMatch(versions: nuGetVersions);
        if (bestMatch == null)
        {
            if (nuGetVersions.Count() == 1) return nuGetVersions.First();

            if (Config.TRACE)
                Console.WriteLine(
                    value: $"WARNING: Unable to find a version to satisfy range {range.PrettyPrint()} for the dependency {name}");
            if (range.HasUpperBound && !range.HasLowerBound)
            {
                if (Config.TRACE)
                    Console.WriteLine(
                        value: $"Instead will return the maximum range demanded: {range.MaxVersion.ToFullString()}");
                return range.MaxVersion;
            }

            if (Config.TRACE)
                Console.WriteLine(
                    value: $"Instead will return the minimum range demanded: {range.MinVersion.ToFullString()}");
            return range.MinVersion;
        }

        return bestMatch;
    }

    public DependencyResolution Process()
    {
        var builder = new PackageSetBuilder();
        var result = new DependencyResolution();

        foreach (var target in LockFile.Targets)
        {
            foreach (var library in target.Libraries)
            {
                var name = library.Name;
                var version = library.Version.ToNormalizedString();
                var packageId = new BasePackage(name: name, version: version);

                var dependencies = new HashSet<BasePackage?>();
                foreach (var dependency in library.Dependencies)
                {
                    var dep_id = dependency.Id;
                    var vr = dependency.VersionRange;
                    //vr.Float.FloatBehavior = NuGet.Versioning.NuGetVersionFloatBehavior.
                    var lb = target.Libraries;
                    var bs = BestVersion(name: dep_id, range: vr, libraries: lb);
                    if (bs == null)
                    {
                        if (Config.TRACE) Console.WriteLine(value: dependency.Id);
                        bs = BestVersion(name: dep_id, range: vr, libraries: lb);
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
            Console.WriteLine(value: $"LockFile: {LockFile}");
            Console.WriteLine(value: $"LockFile.Path: {LockFile.Path}");
        }

        if (LockFile != null
            && LockFile.PackageSpec != null
            && LockFile.PackageSpec.Dependencies != null
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
                Console.WriteLine(value: $"LockFile.PackageSpec: {LockFile?.PackageSpec}");
                Console.WriteLine(value: $"LockFile.PackageSpec.TargetFrameworks: {LockFile?.PackageSpec?.TargetFrameworks}");
            }

            var packageSpecTargetFrameworks = LockFile.PackageSpec.TargetFrameworks;

            foreach (var framework in packageSpecTargetFrameworks)
            {
                foreach (var dep in framework.Dependencies)
                {
                    var version = builder.GetResolvedVersion(name: dep.Name, range: dep.LibraryRange.VersionRange);
                    result.Dependencies.Add(item: new BasePackage(name: dep.Name, version: version));
                }
            }
        }

        foreach (var projectFileDependencyGroup in LockFile.ProjectFileDependencyGroups)
        {
            foreach (var projectFileDependency in projectFileDependencyGroup.Dependencies)
            {
                var projectDependencyParsed = ParseProjectFileDependencyGroup(projectFileDependency: projectFileDependency);
                var libraryVersion = BestLibraryVersion(name: projectDependencyParsed.GetName(),
                    range: projectDependencyParsed.GetVersionRange(), libraries: LockFile.Libraries);
                string? version = null;
                if (libraryVersion != null) version = libraryVersion.ToNormalizedString();
                result.Dependencies.Add(item: new BasePackage(name: projectDependencyParsed.GetName(), version: version));
            }
        }


        if (result.Dependencies.Count == 0 && Config.TRACE)
        {
            Console.WriteLine(value: $"Found no dependencies fo r lock file: {LockFile.Path}");
        }

        result.Packages = builder.GetPackageList();
        return result;
    }


    public ProjectFileDependency ParseProjectFileDependencyGroup(string projectFileDependency)
    {
        //See: https://github.com/NuGet/NuGet.Client/blob/538727480d93b7d8474329f90ccb9ff3b3543714/nuget-inspector/NuGet.Core/NuGet.LibraryModel/LibraryRange.cs#L68
        // FIXME: there are some rare cases where we have multiple constraints as in this JSON snippet for FSharp.Core:
        //  "projectFileDependencyGroups": {
        //    ".NETFramework,Version=v4.7.2": [
        //      "FSharp.Core >= 4.3.4 < 5.0.0",
        //    ]
        //  },
        // This a case that is NOT handled yet

        if (ParseProjectFileDependencyGroupTokens(input: projectFileDependency, tokens: " >= ", projectName: out var projectName,
                projectVersion: out var versionRaw))
            return new ProjectFileDependency(name: projectName,
                versionRange: MinVersionOrFloat(versionValueRaw: versionRaw, includeMin: true /* Include min version. */));

        if (ParseProjectFileDependencyGroupTokens(input: projectFileDependency, tokens: " > ", projectName: out var projectName2,
                projectVersion: out var versionRaw2))
            return new ProjectFileDependency(name: projectName2,
                versionRange: MinVersionOrFloat(versionValueRaw: versionRaw2, includeMin: false /* Do not include min version. */));

        if (ParseProjectFileDependencyGroupTokens(input: projectFileDependency, tokens: " <= ", projectName: out var projectName3,
                projectVersion: out var versionRaw3))
        {
            var maxVersion = NuGetVersion.Parse(value: versionRaw3);
            return new ProjectFileDependency(name: projectName3,
                versionRange: new VersionRange(minVersion: null, includeMinVersion: false, maxVersion: maxVersion, includeMaxVersion: true /* Include Max */));
        }

        if (ParseProjectFileDependencyGroupTokens(input: projectFileDependency, tokens: " < ", projectName: out var projectName4,
                projectVersion: out var versionRaw4))
        {
            var maxVersion = NuGetVersion.Parse(value: versionRaw4);
            return new ProjectFileDependency(name: projectName4,
                versionRange: new VersionRange(minVersion: null, includeMinVersion: false, maxVersion: maxVersion /* Do NOT Include Max */));
        }

        throw new Exception(message:
            $"Unable to parse project file dependency group, please contact support: {projectFileDependency}");
    }

    private bool ParseProjectFileDependencyGroupTokens(string input, string tokens, out string projectName,
        out string? projectVersion)
    {
        if (input.Contains(value: tokens))
        {
            var pieces = input.Split(separator: tokens);
            projectName = pieces[0].Trim();
            projectVersion = pieces[1].Trim();
            return true;
        }

        projectName = null;
        projectVersion = null;
        return false;
    }

    private VersionRange MinVersionOrFloat(string versionValueRaw, bool includeMin)
    {
        //could be Floating or MinVersion
        if (NuGetVersion.TryParse(value: versionValueRaw, version: out var minVersion))
            return new VersionRange(minVersion: minVersion, includeMinVersion: includeMin);
        return VersionRange.Parse(value: versionValueRaw, allowFloating: true);
    }

    public class ProjectFileDependency
    {
        private readonly string? name;
        private readonly VersionRange versionRange;

        public ProjectFileDependency(string? name, VersionRange versionRange)
        {
            this.name = name;
            this.versionRange = versionRange;
        }

        public string? GetName()
        {
            return name;
        }

        public VersionRange GetVersionRange()
        {
            return versionRange;
        }
    }
}