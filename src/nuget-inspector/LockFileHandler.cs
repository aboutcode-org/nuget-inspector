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
        var versions = libraries.Where(lib => lib.Name == name).Select(lib => lib.Version);
        var bestMatch = range.FindBestMatch(versions);
        if (bestMatch == null)
        {
            if (versions.Count() == 1) return versions.First();

            if (Config.TRACE) Console.WriteLine(
                $"WARNING: Unable to find a version to satisfy range {range.PrettyPrint()} for the dependency " + name);
            if (Config.TRACE) Console.WriteLine("Instead will return the minimum range demanded: " + range.MinVersion.ToFullString());
            return range.MinVersion;
        }

        return bestMatch;
    }

    private NuGetVersion BestLibraryVersion(string? name, VersionRange range, IList<LockFileLibrary> libraries)
    {
        var versions = libraries.Where(lib => lib.Name == name).Select(lib => lib.Version);
        var bestMatch = range.FindBestMatch(versions);
        if (bestMatch == null)
        {
            if (versions.Count() == 1) return versions.First();

            if (Config.TRACE) Console.WriteLine(
                $"WARNING: Unable to find a version to satisfy range {range.PrettyPrint()} for the dependency " + name);
            if (range.HasUpperBound && !range.HasLowerBound)
            {
                if (Config.TRACE) Console.WriteLine("Instead will return the maximum range demanded: " + range.MaxVersion.ToFullString());
                return range.MaxVersion;
            }

            if (Config.TRACE) Console.WriteLine("Instead will return the minimum range demanded: " + range.MinVersion.ToFullString());
            return range.MinVersion;
        }

        return bestMatch;
    }

    public DependencyResolution Process()
    {
        var builder = new PackageSetBuilder();
        var result = new DependencyResolution();

        foreach (var target in LockFile.Targets)
        foreach (var library in target.Libraries)
        {
            var name = library.Name;
            var version = library.Version.ToNormalizedString();
            var packageId = new PackageId(name, version);

            var dependencies = new HashSet<PackageId?>();
            foreach (var dep in library.Dependencies)
            {
                var id = dep.Id;
                var vr = dep.VersionRange;
                //vr.Float.FloatBehavior = NuGet.Versioning.NuGetVersionFloatBehavior.
                var lb = target.Libraries;
                var bs = BestVersion(id, vr, lb);
                if (bs == null)
                {
                    if (Config.TRACE) Console.WriteLine(dep.Id);
                    bs = BestVersion(id, vr, lb);
                }
                else
                {
                    var depId = new PackageId(id, bs.ToNormalizedString());
                    dependencies.Add(depId);
                }
            }
            builder.AddOrUpdatePackage(packageId, dependencies);
        }

        if (LockFile.PackageSpec.Dependencies.Count != 0)
            foreach (var dep in LockFile.PackageSpec.Dependencies)
            {
                var version = builder.GetResolvedVersion(dep.Name, dep.LibraryRange.VersionRange);
                result.Dependencies.Add(new PackageId(dep.Name, version));
            }
        else
            foreach (var framework in LockFile.PackageSpec.TargetFrameworks)
            foreach (var dep in framework.Dependencies)
            {
                var version = builder.GetResolvedVersion(dep.Name, dep.LibraryRange.VersionRange);
                result.Dependencies.Add(new PackageId(dep.Name, version));
            }

        foreach (var projectFileDependencyGroup in LockFile.ProjectFileDependencyGroups)
        foreach (var projectFileDependency in projectFileDependencyGroup.Dependencies)
        {
            var projectDependencyParsed = ParseProjectFileDependencyGroup(projectFileDependency);
            var libraryVersion = BestLibraryVersion(projectDependencyParsed.GetName(),
                projectDependencyParsed.GetVersionRange(), LockFile.Libraries);
            string? version = null;
            if (libraryVersion != null) version = libraryVersion.ToNormalizedString();
            result.Dependencies.Add(new PackageId(projectDependencyParsed.GetName(), version));
        }


        if (result.Dependencies.Count == 0 && Config.TRACE)
        {
            Console.WriteLine("Found no dependencies for lock file: " + LockFile.Path);
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

        if (ParseProjectFileDependencyGroupTokens(projectFileDependency, " >= ", out var projectName,
                out var versionRaw))
            return new ProjectFileDependency(projectName,
                MinVersionOrFloat(versionRaw, true /* Include min version. */));

        if (ParseProjectFileDependencyGroupTokens(projectFileDependency, " > ", out var projectName2,
                out var versionRaw2))
            return new ProjectFileDependency(projectName2,
                MinVersionOrFloat(versionRaw2, false /* Do not include min version. */));

        if (ParseProjectFileDependencyGroupTokens(projectFileDependency, " <= ", out var projectName3,
                out var versionRaw3))
        {
            var maxVersion = NuGetVersion.Parse(versionRaw3);
            return new ProjectFileDependency(projectName3,
                new VersionRange(null, false, maxVersion, true /* Include Max */));
        }

        if (ParseProjectFileDependencyGroupTokens(projectFileDependency, " < ", out var projectName4,
                out var versionRaw4))
        {
            var maxVersion = NuGetVersion.Parse(versionRaw4);
            return new ProjectFileDependency(projectName4,
                new VersionRange(null, false, maxVersion /* Do NOT Include Max */));
        }

        throw new Exception("Unable to parse project file dependency group, please contact support: " +
                            projectFileDependency);
    }

    private bool ParseProjectFileDependencyGroupTokens(string input, string tokens, out string? projectName,
        out string projectVersion)
    {
        if (input.Contains(tokens))
        {
            var pieces = input.Split(tokens);
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
        if (NuGetVersion.TryParse(versionValueRaw, out var minVersion))
            return new VersionRange(minVersion, includeMin);
        return VersionRange.Parse(versionValueRaw, true);
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