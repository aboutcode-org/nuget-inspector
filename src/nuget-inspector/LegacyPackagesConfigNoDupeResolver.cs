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
    private readonly NugetApi nuget;
    private readonly Dictionary<string, ResolutionData> resolutionData = new();

    public LegacyPackagesConfigNoDupeResolver(NugetApi service)
    {
        nuget = service;
    }

    private List<VersionRange?> FindAllVersionRangesFor(string id)
    {
        id = id.ToLower();
        var result = new List<VersionRange?>();
        foreach (var pkg in resolutionData.Values)
        foreach (var depPair in pkg.Dependencies)
            if (depPair.Key == id)
                result.Add(depPair.Value);
        return result;
    }

    public List<PackageSet> ProcessAll(List<Dependency> packages)
    {
        foreach (var package in packages) Add(package.Name, package.Name, package.VersionRange, package.Framework);

        var builder = new PackageSetBuilder();
        foreach (var data in resolutionData.Values)
        {
            var deps = new HashSet<BasePackage?>();
            foreach (var dep in data.Dependencies.Keys)
                if (!resolutionData.ContainsKey(dep))
                    throw new Exception($"Encountered a dependency but was unable to resolve a package for it: {dep}");
                else
                    deps.Add(new BasePackage(resolutionData[dep].Name,
                        resolutionData[dep].CurrentVersion.ToNormalizedString()));
            builder.AddOrUpdatePackage(new BasePackage(data.Name, data.CurrentVersion.ToNormalizedString()), deps);
        }

        return builder.GetPackageList();
    }

    public void Add(string id, string? name, VersionRange? range, NuGetFramework? framework)
    {
        id = id.ToLower();
        Resolve(id: id, name: name, framework: framework, overrideRange: range);
    }

    private void Resolve(string id, string? name, NuGetFramework? framework = null, VersionRange? overrideRange = null)
    {
        id = id.ToLower();
        ResolutionData data;
        if (resolutionData.ContainsKey(id))
        {
            data = resolutionData[id];
            if (overrideRange != null)
            {
                if (data.ExternalVersionRange == null)
                    data.ExternalVersionRange = overrideRange;
                else
                    throw new Exception("Can't set more than one external version range.");
            }
        }
        else
        {
            data = new ResolutionData();
            data.ExternalVersionRange = overrideRange;
            data.Name = name;
            resolutionData[id] = data;
        }

        var allVersions = FindAllVersionRangesFor(id);
        if (data.ExternalVersionRange != null) allVersions.Add(data.ExternalVersionRange);
        var combo = VersionRange.CommonSubSet(allVersions);
        var best = nuget.FindPackageVersion(id, combo);

        if (best == null)
        {
            if (Config.TRACE)
                Console.WriteLine(
                    $"Unable to find package for '{id}' with range '{combo.ToString()}'. Likely a conflict exists in packages.config or the nuget metadata service configured incorrectly.");
            if (data.CurrentVersion == null) data.CurrentVersion = combo.MinVersion;
            return;
        }

        if (data.CurrentVersion == best.Identity.Version) return;

        data.CurrentVersion = best.Identity.Version;
        data.Dependencies.Clear();

        var packages = nuget.DependenciesForPackage(best.Identity, framework);
        foreach (var dependency in packages)
            if (!data.Dependencies.ContainsKey(dependency.Id.ToLower()))
            {
                data.Dependencies.Add(dependency.Id.ToLower(), dependency.VersionRange);
                Resolve(dependency.Id.ToLower(), dependency.Id, framework);
            }
    }

    private class ResolutionData
    {
        public NuGetVersion CurrentVersion;
        public readonly Dictionary<string, VersionRange?> Dependencies = new();
        public VersionRange? ExternalVersionRange;
        public string? Name;
    }
}