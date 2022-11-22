using Newtonsoft.Json;
using NuGet.Versioning;

namespace NugetInspector
{
    public class PackageSetBuilder
    {
        private readonly Dictionary<BasePackage, PackageSet> packageSets = new();
        private readonly Dictionary<BasePackage, VersionPair> versions = new();


        public bool DoesPackageExist(BasePackage id)
        {
            return packageSets.ContainsKey(key: id);
        }

        public PackageSet GetOrCreatePackageSet(BasePackage package)
        {
            if (packageSets.TryGetValue(key: package, value: out var packageSet))
            {
                return packageSet;
            }

            packageSet = new PackageSet
            {
                PackageId = package,
                Dependencies = new HashSet<BasePackage?>()
            };
            packageSets[key: package] = packageSet;

            NuGetVersion.TryParse(value: package.Version, version: out var version);
            if (package.Version != null)
            {
                versions[key: package] = new VersionPair(rawVersion: package.Version, version: version);
            }

            return packageSet;
        }

        /// <summary>
        /// Add BasePackage to the packageSets
        /// </summary>
        /// <param name="id"></param>
        public void AddOrUpdatePackage(BasePackage id)
        {
            GetOrCreatePackageSet(package: id);
        }

        /// <summary>
        /// Add BasePackage to the packageSets, and dependency as a dependency.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="dependency"></param>
        public void AddOrUpdatePackage(BasePackage id, BasePackage dependency)
        {
            var packageSet = GetOrCreatePackageSet(package: id);
            packageSet.Dependencies.Add(item: dependency);
        }

        /// <summary>
        /// Add BasePackage to the packageSets, and dependencies as dependencies.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="dependencies"></param>
        public void AddOrUpdatePackage(BasePackage id, HashSet<BasePackage> dependencies)
        {
            var packageSet = GetOrCreatePackageSet(package: id);
            packageSet.Dependencies.UnionWith(other: dependencies);
        }

        public List<PackageSet> GetPackageList()
        {
            return packageSets.Values.ToList();
        }

        public string? GetResolvedVersion(string name, VersionRange range)
        {
            var allVersions = versions.Keys.Where(predicate: key => key.Name == name).Select(selector: key => versions[key: key]);
            var best = range.FindBestMatch(versions: allVersions.Select(selector: ver => ver.Version));
            foreach (var pair in versions)
                if (pair.Key.Name == name && pair.Value.Version == best)
                    return pair.Key.Version;

            return null;
        }

        private class VersionPair
        {
            public string RawVersion;
            public NuGetVersion Version;

            public VersionPair(string rawVersion, NuGetVersion version)
            {
                RawVersion = rawVersion;
                Version = version;
            }
        }
    }

    public class PackageSet
    {
        [JsonProperty(propertyName: "package")] public BasePackage? PackageId;
        [JsonProperty(propertyName: "dependencies")] public HashSet<BasePackage?> Dependencies = new();
    }

    public class BasePackage
    {
        [JsonProperty(propertyName: "type")] public string Type = "nuget";
        [JsonProperty(propertyName: "name")] public string? Name { get; set; }
        [JsonProperty(propertyName: "version")] public string? Version { get; set; }
        [JsonProperty(propertyName: "framework")] public string? Framework { get; set; }
        [JsonProperty(propertyName: "purl")] public string Purl { get; set; }
        [JsonProperty(propertyName: "download_url")] public string DownloadUrl { get; set; }

        public BasePackage(string? name, string? version, string? framework)
        {
            Name = name;
            Version = version;
            Framework = framework;

            // FIXME: support having no version
            Purl = $"pkg:nuget/{name}@{version}";
            DownloadUrl = $"https://www.nuget.org/api/v2/package/{name}/{version}";
        }

        public BasePackage(string? name, string? version)
        {
            Name = name;
            Version = version;
            // FIXME: support having no version
            Purl = $"pkg:nuget/{name}@{version}";
            DownloadUrl = $"https://www.nuget.org/api/v2/package/{name}/{version}";
        }

        public override int GetHashCode()
        {
            int hash = 37;
            hash = (hash * 7);
            hash += Name == null ? 0 : Name.GetHashCode();
            hash = (hash * 7);
            hash += Version == null ? 0 : Version.GetHashCode();
            hash = (hash * 7);
            hash += Framework == null ? 0 : Framework.GetHashCode();
            return hash;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            var other = (BasePackage)obj;
            if (Name == null)
            {
                if (other.Name != null) return false;
            }
            else if (!Name.Equals(value: other.Name))
            {
                return false;
            }

            if (Version == null)
            {
                if (other.Version != null) return false;
            }
            else if (!Version.Equals(value: other.Version))
            {
                return false;
            }

            if (Framework == null)
            {
                if (other.Framework != null) return false;
            }
            else if (!Framework.Equals(value: other.Framework))
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// This is a core data structure
    /// </summary>
    public class Package
    {
        [JsonProperty(propertyName: "project_name")] public string? Name { get; set; }
        [JsonProperty(propertyName: "project_version")] public string? Version { get; set; }

        [JsonProperty(propertyName: "project_datafile_type")]
        public string Type { get; set; } = "solution-file";

        [JsonProperty(propertyName: "datasource_id")] public string DatasourceId { get; set; }
        [JsonProperty(propertyName: "project_file")] public string SourcePath { get; set; }
        [JsonProperty(propertyName: "outputs")] public List<string> OutputPaths { get; set; } = new();
        [JsonProperty(propertyName: "packages")] public List<PackageSet> Packages { get; set; } = new();
        [JsonProperty(propertyName: "dependencies")] public List<BasePackage> Dependencies { get; set; } = new();
        [JsonProperty(propertyName: "children")] public List<Package?> Children { get; set; } = new();
    }
}