using NuGet.Frameworks;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// Resolve using packages.config strategy from:
/// See https://docs.microsoft.com/en-us/nuget/consume-packages/dependency-resolution#dependency-resolution-with-packagesconfig
/// See https://learn.microsoft.com/en-us/nuget/reference/packages-config
/// It means that only one package version can exist in the dependencies tree.
/// </summary>
public class PackagesConfigHelper
{
    private readonly NugetApi NugetApi;
    private readonly Dictionary<string, ResolutionData> ResolutionDatas = new();

    public PackagesConfigHelper(NugetApi nugetApi)
    {
        NugetApi = nugetApi;
    }

    private List<VersionRange?> FindAllVersionRangesFor(string id)
    {
        id = id.ToLower();
        var result = new List<VersionRange?>();
        foreach (var pkg in ResolutionDatas.Values)
        {
            foreach (var depPair in pkg.Dependencies)
            {
                if (depPair.Key == id)
                result.Add(item: depPair.Value);
            }
        }

        return result;
    }

    public List<BasePackage> ProcessAll(List<Dependency> dependencies)
    {
        foreach (var dependency in dependencies)
        {
            Add(
                id: dependency.name!,
                name: dependency.name,
                range: dependency.version_range,
                framework: dependency.framework);
        }

        var builder = new PackageTree();
        foreach (var data in ResolutionDatas.Values)
        {
            var deps = new List<BasePackage>();
            foreach (var dep in data.Dependencies.Keys)
            {
                if (!ResolutionDatas.ContainsKey(key: dep))
                {
                    throw new Exception($"Unable to resolve dependencies: {dep}");
                }
                else
                {
                    deps.Add(item: new BasePackage(
                        name: ResolutionDatas[key: dep].Name!,
                        version: ResolutionDatas[key: dep].CurrentVersion?.ToNormalizedString()));
                }
            }

            builder.AddOrUpdatePackage(
                base_package: new BasePackage(name: data.Name!,
                    version: data.CurrentVersion?.ToNormalizedString()),
                    dependencies: deps!);
        }

        return builder.GetPackageList();
    }

    public void Add(string id, string? name, VersionRange? range, NuGetFramework? framework)
    {
        id = id.ToLower();
        Resolve(
            id: id,
            name: name,
            project_target_framework: framework,
            overrideRange: range);
    }

    private void Resolve(
        string id,
        string? name,
        NuGetFramework? project_target_framework = null,
        VersionRange? overrideRange = null)
    {
        id = id.ToLower();
        ResolutionData data = new();
        if (ResolutionDatas.ContainsKey(key: id))
        {
            data = ResolutionDatas[key: id];
            if (overrideRange != null)
            {
                if (data.ExternalVersionRange == null)
                    data.ExternalVersionRange = overrideRange;
                else
                    throw new Exception(message: "Cannot set more than one external version range.");
            }
        }
        else
        {
            data.ExternalVersionRange = overrideRange;
            data.Name = name;
            ResolutionDatas[key: id] = data;
        }

        var allVersions = FindAllVersionRangesFor(id: id);
        if (data.ExternalVersionRange != null) allVersions.Add(item: data.ExternalVersionRange);
        var combo = VersionRange.CommonSubSet(ranges: allVersions);
        var best = NugetApi.FindPackageVersion(name: id, version_range: combo);

        if (best == null)
        {
            if (Config.TRACE)
                Console.WriteLine( value: $"Unable to find package for '{id}' with versions range '{combo}'.");

            if (data.CurrentVersion == null)
                data.CurrentVersion = combo.MinVersion;

            return;
        }

        if (data.CurrentVersion == best.Identity.Version) return;

        data.CurrentVersion = best.Identity.Version;
        data.Dependencies.Clear();

        var packages = NugetApi.GetPackageDependenciesForPackage(identity: best.Identity, framework: project_target_framework);
        foreach (var dependency in packages)
        {
            if (!data.Dependencies.ContainsKey(key: dependency.Id.ToLower()))
            {
                data.Dependencies.Add(key: dependency.Id.ToLower(), value: dependency.VersionRange);
                Resolve(
                    id: dependency.Id.ToLower(),
                    name: dependency.Id,
                    project_target_framework: project_target_framework);
            }
        }
    }

    private class ResolutionData
    {
        public NuGetVersion? CurrentVersion;
        public readonly Dictionary<string, VersionRange?> Dependencies = new();
        public VersionRange? ExternalVersionRange;
        public string? Name;
    }
}