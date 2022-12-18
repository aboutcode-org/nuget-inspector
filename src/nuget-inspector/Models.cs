using Newtonsoft.Json;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace NugetInspector
{
    public class Dependency
    {
        public string? name;
        public NuGetFramework? framework;
        public VersionRange? version_range;

        public Dependency(string? name, VersionRange? version_range, NuGetFramework? framework = null)
        {
            this.framework = framework;
            this.name = name;
            this.version_range = version_range;
        }

        public Dependency(NuGet.Packaging.Core.PackageDependency dependency, NuGetFramework? framework)
        {
            this.framework = framework;
            name = dependency.Id;
            version_range = dependency.VersionRange;
        }

        /// <summary>
        /// Return an empty PackageSet using this package.
        /// </summary>
        /// <returns></returns>
        public PackageSet ToEmptyPackageSet()
        {
            var package_set = new PackageSet
            {
                package = new BasePackage(
                    name: name,
                    version: version_range?.MinVersion.ToNormalizedString(),
                    framework: framework?.ToString()
                )
            };
            return package_set;
        }
    }

    public class PackageSetBuilder
    {
        private readonly Dictionary<BasePackage, PackageSet> package_sets = new();
        private readonly Dictionary<BasePackage, VersionPair> versions = new();


        public bool DoesPackageExist(BasePackage id)
        {
            return package_sets.ContainsKey(key: id);
        }

        public PackageSet GetOrCreatePackageSet(BasePackage package)
        {
            if (package_sets.TryGetValue(key: package, value: out var packageSet))
            {
                return packageSet;
            }

            packageSet = new PackageSet
            {
                package = package,
                dependencies = new HashSet<BasePackage?>()
            };
            package_sets[key: package] = packageSet;

            NuGetVersion.TryParse(value: package.version, version: out var version);
            if (package.version != null)
            {
                versions[key: package] = new VersionPair(rawVersion: package.version, version: version);
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
            packageSet.dependencies.Add(item: dependency);
        }

        /// <summary>
        /// Add BasePackage to the packageSets, and dependencies as dependencies.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="dependencies"></param>
        public void AddOrUpdatePackage(BasePackage id, HashSet<BasePackage?> dependencies)
        {
            var packageSet = GetOrCreatePackageSet(package: id);
            packageSet.dependencies.UnionWith(other: dependencies);
        }

        public List<PackageSet> GetPackageList()
        {
            return package_sets.Values.ToList();
        }

        public string? GetResolvedVersion(string name, VersionRange range)
        {
            var allVersions = versions.Keys.Where(predicate: key => key.name == name)
                .Select(selector: key => versions[key: key]);
            var best = range.FindBestMatch(versions: allVersions.Select(selector: ver => ver.version));
            foreach (var pair in versions)
                if (pair.Key.name == name && pair.Value.version == best)
                    return pair.Key.version;

            return null;
        }

        private class VersionPair
        {
            public string raw_version;
            public NuGetVersion version;

            public VersionPair(string rawVersion, NuGetVersion version)
            {
                raw_version = rawVersion;
                this.version = version;
            }
        }
    }

    public class PackageSet
    {
        public BasePackage? package;
        public HashSet<BasePackage?> dependencies = new();
    }

    public class BasePackage
    {
        public string type = "nuget";
        public string? name { get; set; } = "";
        public string? version { get; set; } = "";
        public string? framework { get; set; } = "";
        public string purl { get; set; } = "";
        public string download_url { get; set; } = "";

        public BasePackage(string? name, string? version, string? framework)
        {
            this.name = name;
            this.version = version;
            this.framework = framework;

            // FIXME: support having no version
            if (string.IsNullOrWhiteSpace(version))
                purl = $"pkg:nuget/{name}";
            else
                purl = $"pkg:nuget/{name}@{version}";
            
            download_url = $"https://www.nuget.org/api/v2/package/{name}/{version}";
        }

        public BasePackage(string? name, string? version)
        {
            this.name = name;
            this.version = version;
            // FIXME: support having no version
            purl = $"pkg:nuget/{name}@{version}";
            download_url = $"https://www.nuget.org/api/v2/package/{name}/{version}";
        }

        public override int GetHashCode()
        {
            int hash = 37;
            hash = (hash * 7);
            hash += name == null ? 0 : name.GetHashCode();
            hash = (hash * 7);
            hash += version == null ? 0 : version.GetHashCode();
            hash = (hash * 7);
            hash += framework == null ? 0 : framework.GetHashCode();
            return hash;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            var other = (BasePackage)obj;
            if (name == null)
            {
                if (other.name != null) return false;
            }
            else if (!name.Equals(value: other.name))
            {
                return false;
            }

            if (version == null)
            {
                if (other.version != null) return false;
            }
            else if (!version.Equals(value: other.version))
            {
                return false;
            }

            if (framework == null)
            {
                if (other.framework != null) return false;
            }
            else if (!framework.Equals(value: other.framework))
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
        public string? name { get; set; } = "";
        public string? version { get; set; } = "";
        public string datasource_id { get; set; } = null!;
        public string project_file { get; set; } = null!;
        public List<PackageSet> packages { get; set; } = new();
        public List<BasePackage> dependencies { get; set; } = new();
    }

    /// <summary>
    /// This is a core data structure, modelled after ScanCode models
    /// </summary>
    public class PackageData
    {
        public string? name { get; set; } = null;
        public string? version { get; set; } = null;
        public string datasource_id { get; set; } = null!;
        public string project_file { get; set; } = null!;
        public List<PackageSet> packages { get; set; } = new();
        public List<BasePackage> dependencies { get; set; } = new();
    }

    /// <summary>
    /// A party is a person, project or organization related to a package.
    /// </summary>
    public class Party
    {
        //One of  'person', 'project' or 'organization'
        public string type { get; set; } = "";
        public string role { get; set; } = "";
        public string? name { get; set; } = "";
        public string? email { get; set; } = "";
        public string? url { get; set; } = "";
    }

    public class DependentPackage
    {
        public string purl { get; set; }= "";
        public string extracted_requirement { get; set; } = "";
        public string scope { get; set; } = "";
        public bool is_runtime { get; set;}
        public bool is_optional { get; set; }
        public bool is_resolved { get; set; }
        public ScannedPackageData? resolved_package { get; set; }
        public Dictionary<string, string> extra_data { get; set; } = new();
    }
    

    /// <summary>
    /// Package data object using purl as identifying attributes as
    /// specified here https://github.com/package-url/purl-spec.
    /// </summary>
    public class ScannedPackageData
    {
        public string type { get; set; } = "";
        [JsonProperty(propertyName: "namespace")]
        public string name_space { get; set; } = "";
        public string name { get; set; } = "";
        public string version { get; set; } = "";
        public string qualifiers { get; set; } = "";
        public string subpath { get; set; } = "";

        public string primary_language { get; set; } = "C#";
        public string description { get; set; } = "";
        public string release_date { get; set; } = "";
        public List<Party> parties { get; set; } = new();
        public List<string> keywords { get; set; } = new();
        public string homepage_url { get; set; } = "";
        public string download_url { get; set; } = "";
        public int size { get; set; }
        public string sha1 { get; set; } = "";
        public string md5 { get; set; } = "";
        public string sha256 { get; set; } = "";
        public string sha512 { get; set; } = "";
        public string bug_tracking_url { get; set; } = "";
        public string code_view_url { get; set; } = "";
        public string vcs_url { get; set; } = "";
        public string copyright { get; set; } = "";
        public string license_expression { get; set; } = "";
        public string declared_license { get; set; } = "";
        public string notice_text { get; set; } = "";
        public List<string> source_packages { get; set; } = new();
        public Dictionary<string, string> extra_data { get; set; } = new();
        public List<DependentPackage> dependencies { get; set; } = new();
        public string repository_homepage_url { get; set; } = "";
        public string repository_download_url { get; set; } = "";
        public string api_data_url { get; set; } = "";
        public string datasource_id { get; set; } = "";
    }
}