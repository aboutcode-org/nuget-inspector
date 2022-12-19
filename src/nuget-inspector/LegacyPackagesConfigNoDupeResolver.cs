using NuGet.Frameworks;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// Resolve using packages.config strategy from: 
/// See https://docs.microsoft.com/en-us/nuget/consume-packages/dependency-resolution#dependency-resolution-with-packagesconfig
/// See https://learn.microsoft.com/en-us/nuget/reference/packages-config
/// It means that that only one package version can exist in the deps tree.
/// </summary>
public class LegacyPackagesConfigNoDupeResolver
{
    public const string DatasourceId = "nuget-packages.config-no-dupe";
    private readonly NugetApi NugetApi;
    private readonly Dictionary<string, ResolutionData> ResolutionDatas = new();

    public LegacyPackagesConfigNoDupeResolver(NugetApi service)
    {
        NugetApi = service;
    }

    private List<VersionRange?> FindAllVersionRangesFor(string id)
    {
        id = id.ToLower();
        var result = new List<VersionRange?>();
        foreach (var pkg in ResolutionDatas.Values)
        foreach (var depPair in pkg.Dependencies)
            if (depPair.Key == id)
                result.Add(item: depPair.Value);
        return result;
    }

    public List<BasePackage> ProcessAll(List<Dependency> packages)
    {
        foreach (var package in packages)
        {
            Add(id: package.name!, name: package.name, range: package.version_range, framework: package.framework);
        }

        var builder = new PackageBuilder();
        foreach (var data in ResolutionDatas.Values)
        {
            var deps = new List<BasePackage>();
            foreach (var dep in data.Dependencies.Keys)
                if (!ResolutionDatas.ContainsKey(key: dep))
                    throw new Exception(
                        message: $"Encountered a dependency but was unable to resolve a package for it: {dep}");
                else
                    deps.Add(item: new BasePackage(
                        name: ResolutionDatas[key: dep].Name!,
                        version: ResolutionDatas[key: dep].CurrentVersion?.ToNormalizedString()));
            builder.AddOrUpdatePackage(
                id: new BasePackage(name: data.Name!, version: data.CurrentVersion?.ToNormalizedString()),
                dependencies: deps!);
        }

        return builder.GetPackageList();
    }

    public void Add(string id, string? name, VersionRange? range, NuGetFramework? framework)
    {
        id = id.ToLower();
        Resolve(id: id, name: name, framework: framework, overrideRange: range);
    }

    private void Resolve(
        string id,
        string? name,
        NuGetFramework? framework = null,
        VersionRange? overrideRange = null)
    {
        id = id.ToLower();
        ResolutionData data;
        if (ResolutionDatas.ContainsKey(key: id))
        {
            data = ResolutionDatas[key: id];
            if (overrideRange != null)
            {
                if (data.ExternalVersionRange == null)
                    data.ExternalVersionRange = overrideRange;
                else
                    throw new Exception(message: "Can't set more than one external version range.");
            }
        }
        else
        {
            data = new ResolutionData();
            data.ExternalVersionRange = overrideRange;
            data.Name = name;
            ResolutionDatas[key: id] = data;
        }

        var allVersions = FindAllVersionRangesFor(id: id);
        if (data.ExternalVersionRange != null) allVersions.Add(item: data.ExternalVersionRange);
        var combo = VersionRange.CommonSubSet(ranges: allVersions);
        var best = NugetApi.FindPackageVersion(id: id, versionRange: combo);

        if (best == null)
        {
            if (Config.TRACE)
                Console.WriteLine(
                    value:
                    $"Unable to find package for '{id}' with range '{combo.ToString()}'. Likely a conflict exists in packages.config or the nuget metadata service configured incorrectly.");
            if (data.CurrentVersion == null) data.CurrentVersion = combo.MinVersion;
            return;
        }

        if (data.CurrentVersion == best.Identity.Version) return;

        data.CurrentVersion = best.Identity.Version;
        data.Dependencies.Clear();

        var packages = NugetApi.DependenciesForPackage(identity: best.Identity, framework: framework);
        foreach (var dependency in packages)
            if (!data.Dependencies.ContainsKey(key: dependency.Id.ToLower()))
            {
                data.Dependencies.Add(key: dependency.Id.ToLower(), value: dependency.VersionRange);
                Resolve(id: dependency.Id.ToLower(), name: dependency.Id, framework: framework);
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