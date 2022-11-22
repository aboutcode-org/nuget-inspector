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
            return packageSets.ContainsKey(id);
        }

        public PackageSet GetOrCreatePackageSet(BasePackage package)
        {
            if (packageSets.TryGetValue(package, out var packageSet))
            {
                return packageSet;
            }

            packageSet = new PackageSet
            {
                PackageId = package,
                Dependencies = new HashSet<BasePackage?>()
            };
            packageSets[package] = packageSet;

            NuGetVersion.TryParse(value: package.Version, version: out var version);
            if (package.Version != null)
            {
                versions[package] = new VersionPair(package.Version, version);
            }

            return packageSet;
        }

        /// <summary>
        /// Add BasePackage to the packageSets
        /// </summary>
        /// <param name="id"></param>
        public void AddOrUpdatePackage(BasePackage id)
        {
            GetOrCreatePackageSet(id);
        }

        /// <summary>
        /// Add BasePackage to the packageSets, and dependency as a dependency.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="dependency"></param>
        public void AddOrUpdatePackage(BasePackage id, BasePackage dependency)
        {
            var packageSet = GetOrCreatePackageSet(id);
            packageSet.Dependencies.Add(dependency);
        }

        /// <summary>
        /// Add BasePackage to the packageSets, and dependencies as dependencies.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="dependencies"></param>
        public void AddOrUpdatePackage(BasePackage id, HashSet<BasePackage> dependencies)
        {
            var packageSet = GetOrCreatePackageSet(id);
            packageSet.Dependencies.UnionWith(dependencies);
        }

        public List<PackageSet> GetPackageList()
        {
            return packageSets.Values.ToList();
        }

        public string? GetResolvedVersion(string name, VersionRange range)
        {
            var allVersions = versions.Keys.Where(key => key.Name == name).Select(key => versions[key]);
            var best = range.FindBestMatch(allVersions.Select(ver => ver.Version));
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
        [JsonProperty("package")] public BasePackage? PackageId;
        [JsonProperty("dependencies")] public HashSet<BasePackage?> Dependencies = new();
    }

    public class BasePackage
    {
        [JsonProperty("type")] public string Type = "nuget";
        [JsonProperty("name")] public string? Name { get; set; }
        [JsonProperty("version")] public string? Version { get; set; }
        [JsonProperty("framework")] public string? Framework { get; set; }
        [JsonProperty("purl")] public string Purl { get; set; }
        [JsonProperty("download_url")] public string DownloadUrl { get; set; }

        public BasePackage(string? name, string? version, string? framework)
        {
            Name = name;
            Version = version;
            Framework = framework;

            // FIXME: support having no version
            Purl = "pkg:nuget/" + name + "@" + version;
            DownloadUrl = "https://www.nuget.org/api/v2/package/" + name + "/" + version;
        }

        public BasePackage(string? name, string? version)
        {
            Name = name;
            Version = version;
            // FIXME: support having no version
            Purl = "pkg:nuget/" + name + "@" + version;
            DownloadUrl = "https://www.nuget.org/api/v2/package/" + name + "/" + version;
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
            else if (!Name.Equals(other.Name))
            {
                return false;
            }

            if (Version == null)
            {
                if (other.Version != null) return false;
            }
            else if (!Version.Equals(other.Version))
            {
                return false;
            }

            if (Framework == null)
            {
                if (other.Framework != null) return false;
            }
            else if (!Framework.Equals(other.Framework))
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
        [JsonProperty("project_name")] public string? Name { get; set; }
        [JsonProperty("project_version")] public string? Version { get; set; }

        [JsonProperty("project_datafile_type")]
        public string Type { get; set; } = "solution-file";

        [JsonProperty("datasource_id")] public string DatasourceId { get; set; }
        [JsonProperty("project_file")] public string SourcePath { get; set; }
        [JsonProperty("outputs")] public List<string> OutputPaths { get; set; } = new();
        [JsonProperty("packages")] public List<PackageSet> Packages { get; set; } = new();
        [JsonProperty("dependencies")] public List<BasePackage> Dependencies { get; set; } = new();
        [JsonProperty("children")] public List<Package?> Children { get; set; } = new();
    }
}