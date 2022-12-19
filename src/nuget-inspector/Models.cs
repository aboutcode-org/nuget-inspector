using Newtonsoft.Json;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
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
        protected bool Equals(BasePackage other)
        {
            return name == other.name && version == other.version && framework == other.framework;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((BasePackage)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(name, version, framework);
        }

    }

    /// <summary>
    /// Package data object using purl as identifying attributes as
    /// specified here https://github.com/package-url/purl-spec
    /// This model is essentially derived from ScanCode Toolkit Package/PackageData.
    /// This is used to represent the top-level project.
    /// </summary>
    public class Package
    {
        public string type { get; set; } = "nuget";
        [JsonProperty(propertyName: "namespace")]
        public string name_space { get; set; } = "";
        public string name { get; set; } = "";
        public string? version { get; set; } = "";
        public string qualifiers { get; set; } = "";
        public string subpath { get; set; } = "";
        public string purl { get; set; } = "";

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
        public string repository_homepage_url { get; set; } = "";
        public string repository_download_url { get; set; } = "";
        public string api_data_url { get; set; } = "";
        public string datasource_id { get; set; } = "";
        public string project_file { get; set; } = null!;

        // public List<DependentPackage> dependencies { get; set; } = new();
        public List<PackageSet> packages { get; set; } = new();
        public List<BasePackage> dependencies { get; set; } = new();

        
        /// <summary>
        /// Create a new PackageData instance from an IPackageSearchMetadata
        /// </summary>
        /// <param metadata="metadata"></param>
        /// <returns>PackageData</returns>
        public static Package CreateInstance(IPackageSearchMetadata metadata)
        {
            List<string> declared_licenses = new();
            Uri license_url = metadata.LicenseUrl;
            if (license_url != null && !string.IsNullOrWhiteSpace(license_url.ToString()))
            {
                declared_licenses.Add( $"license_url: {license_url}");
            }
            LicenseMetadata license_meta = metadata.LicenseMetadata;
            // license_meta;
            string? license_expression = null;
            if (!string.IsNullOrWhiteSpace(license_expression))
            {
                declared_licenses.Add( $"license_expression: {license_expression}");
            }
            string declared_license = string.Join("\n", declared_licenses);

            string purl = "";
            string version = metadata.Identity.Version.ToString();
            string name = metadata.Identity.Id;
            if (string.IsNullOrWhiteSpace(version))
                purl = $"pkg:nuget/{name}";
            else
                purl = $"pkg:nuget/{name}@{version}";
            
            // TODO consider instead: https://api.nuget.org/packages/{name}.{version}.nupkg
            string download_url = $"https://www.nuget.org/api/v2/package/{name}/{version}";
            string api_data_url = $"https://api.nuget.org/v3/registration5-semver1/{name.ToLower()}/{version.ToLower()}.json";
            
            List<Party> parties = new();
            string authors = metadata.Authors;
            if (!string.IsNullOrWhiteSpace(authors))
            {
                Party party = new()
                {
                    type = "organization",
                    role = "author",
                    name = authors,
                };
                parties.Add(party);
            }  
        
            return new Package
            {
                type = "nuget",
                purl = purl,
                primary_language = "C#",
                description=metadata.Description,
                parties = parties,
                //keywords = null,
                homepage_url = metadata.ProjectUrl.ToString(),
                download_url = download_url,
                declared_license = declared_license,
                // source_packages = null,
                // dependencies = null,
                repository_homepage_url = metadata.PackageDetailsUrl.ToString(),
                repository_download_url = download_url,
                api_data_url = api_data_url
            };
        }
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
        public Package? resolved_package { get; set; }
        public Dictionary<string, string> extra_data { get; set; } = new();
    }

}