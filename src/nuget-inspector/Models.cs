using Newtonsoft.Json;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NugetInspector
{
    public class Dependency
    {
        public string? name;
        public NuGetFramework? framework;
        public VersionRange? version_range;
       public bool is_direct;

        public Dependency(string? name, VersionRange? version_range, NuGetFramework? framework = null, bool is_direct = false)
        {
            this.framework = framework;
            this.name = name;
            this.version_range = version_range;
            this.is_direct = is_direct;
        }
        /// <summary>
        /// Return a new empty BasePackageWithDeps using this package.
        /// </summary>
        /// <returns></returns>
        public BasePackage CreateEmptyBasePackage()
        {
            return new BasePackage(
                name: name!,
                version: version_range?.MinVersion.ToNormalizedString(),
                framework: framework?.ToString()
            );
        }
    }

    public class PackageTree
    {
        private readonly Dictionary<BasePackage, BasePackage> base_package_deps_by_base_package = new();
        private readonly Dictionary<BasePackage, VersionPair> versions_pair_by_base_package = new();

        public bool DoesPackageExist(BasePackage package)
        {
            return base_package_deps_by_base_package.ContainsKey(key: package);
        }

        public BasePackage GetOrCreateBasePackage(BasePackage package)
        {
            if (base_package_deps_by_base_package.TryGetValue(key: package, value: out var package_with_deps))
            {
                return package_with_deps;
            }

            package_with_deps = BasePackage.FromBasePackage(package: package, dependencies: new List<BasePackage>());
            base_package_deps_by_base_package[key: package] = package_with_deps;

            _ = NuGetVersion.TryParse(value: package.version, version: out NuGetVersion version);

            if (package.version != null)
                versions_pair_by_base_package[key: package] = new VersionPair(rawVersion: package.version, version: version);

            return package_with_deps;
        }

        /// <summary>
        /// Add BasePackage to the packageSets
        /// </summary>
        /// <param name="id"></param>
        public void AddOrUpdatePackage(BasePackage id)
        {
            GetOrCreateBasePackage(package: id);
        }

        /// <summary>
        /// Add BasePackage to the packageSets, and dependency as a dependency.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="dependency"></param>
        public void AddOrUpdatePackage(BasePackage id, BasePackage dependency)
        {
            var packageSet = GetOrCreateBasePackage(package: id);
            packageSet.dependencies.Add(item: dependency);
        }

        /// <summary>
        /// Add BasePackage base_package to the packageSets, and dependencies to dependencies.
        /// </summary>
        /// <param name="base_package"></param>
        /// <param name="dependencies"></param>
        public void AddOrUpdatePackage(BasePackage base_package, List<BasePackage?> dependencies)
        {
            var packageWithDeps = GetOrCreateBasePackage(package: base_package);
            foreach (var dep in dependencies)
            {
                if (dep != null)
                {
                    packageWithDeps.dependencies.Add(dep);
                }
            }
            packageWithDeps.dependencies = packageWithDeps.dependencies.Distinct().ToList();
        }

        public List<BasePackage> GetPackageList()
        {
            return base_package_deps_by_base_package.Values.ToList();
        }

        public string? GetResolvedVersion(string name, VersionRange range)
        {
            var allVersions = versions_pair_by_base_package.Keys.Where(predicate: key => key.name == name)
                .Select(selector: key => versions_pair_by_base_package[key: key]);
            var best = range.FindBestMatch(versions: allVersions.Select(selector: ver => ver.version));
            foreach (var pair in versions_pair_by_base_package)
            {
                if (pair.Key.name == name && pair.Value.version == best)
                    return pair.Key.version;
            }

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

    /// <summary>
    /// Package data object using purl as identifying attributes as
    /// specified here https://github.com/package-url/purl-spec
    /// This model is essentially derived from ScanCode Toolkit Package/PackageData.
    /// This is used to represent the top-level project.
    /// </summary>
    public class BasePackage : IEquatable<BasePackage>, IComparable<BasePackage>
    {
        public string type { get; set; } = "nuget";
        [JsonProperty(propertyName: "namespace")]
        public string namespace_ { get; set; } = "";
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
        public string datafile_path { get; set; } = "";
        public List<BasePackage> dependencies { get; set; } = new();

        // Track if we updated this package metadata

        [JsonIgnore]
        public bool has_updated_metadata;

        public BasePackage(string name, string? version, string? framework = "", string? datafile_path = "")
        {
            this.name = name;
            this.version = version;
            if (!string.IsNullOrWhiteSpace(framework))
                this.version = version;
            if (!string.IsNullOrWhiteSpace(datafile_path))
                this.datafile_path = datafile_path;
            if (!string.IsNullOrWhiteSpace(framework))
                extra_data["framework"] = framework;
        }

        public static BasePackage FromBasePackage(BasePackage package, List<BasePackage> dependencies)
        {
            return new(name: package.name, version: package.version)
            {
                extra_data = package.extra_data,
                dependencies = dependencies
            };
        }

        protected bool Equals(BasePackage other)
        {
            return
                type == other.type
                && namespace_ == other.namespace_
                && name == other.name
                && version == other.version
                && qualifiers == other.qualifiers
                && subpath == other.subpath;
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((BasePackage)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(type, namespace_, name, version, qualifiers, subpath);
        }

        public PackageIdentity GetPackageIdentity()
        {
            if (!string.IsNullOrWhiteSpace(version))
            {
                return new PackageIdentity(id: name, version: new NuGetVersion(version));
            }
            else
            {
                return new PackageIdentity(id: name, version: null);
            }
        }

        /// <summary>
        /// Update this Package instance using the NuGet API to fetch extra metadata
        /// and also update all its dependencies recursively.
        /// </summary>
        public void Update(NugetApi nugetApi)
        {
            UpdateWithRemoteMetadata(nugetApi);
            foreach (var dep in dependencies)
                dep.Update(nugetApi);
        }

        /// <summary>
        /// Update this Package instance using the NuGet API to fetch extra metadata
        /// </summary>
        public void UpdateWithRemoteMetadata(NugetApi nugetApi)
        {
            if (has_updated_metadata)
                return;

            PackageSearchMetadataRegistration? meta = nugetApi.FindPackageVersion(
                name: name,
                version: version,
                use_cache: true,
                include_prerelease: true);

            if (meta == null)
            {
                // Try again this time bypassing cache and also looking for pre-releases
                meta = nugetApi.FindPackageVersion(
                    name: name,
                    version: version,
                    use_cache: false,
                    include_prerelease: true);
                if (meta == null)
                    return;
            }

            Update(meta);

            // also fetch API-provided download URL and package hash
            PackageIdentity identity = new(id: name , version: new NuGetVersion(version));
            var download = nugetApi.GetPackageDownload(identity: identity);
            if (download != null)
            {
                download_url = download.download_url;
                if (download.hash_algorithm == "SHA512")
                    sha512 = download.hash;
                if (download.size != null)
                    size = (int)download.size;
            }
            has_updated_metadata = true;
        }

        /// <summary>
        /// Update this Package instance from an PackageSearchMetadataRegistration
        /// </summary>
        /// <param name="metadata"></param>
        public void Update(PackageSearchMetadataRegistration? metadata)
        {
            if (metadata == null)
                return;

            // set the purl
            string meta_name = metadata.Identity.Id;
            string meta_version = metadata.Identity.Version.ToString();
            if (string.IsNullOrWhiteSpace(meta_version))
                purl = $"pkg:nuget/{meta_name}";
            else
                purl = $"pkg:nuget/{meta_name}@{meta_version}";

            // Update the declared license
            List<string> meta_declared_licenses = new();
            Uri license_url = metadata.LicenseUrl;
            if (license_url != null && !string.IsNullOrWhiteSpace(license_url.ToString()))
            {
                meta_declared_licenses.Add($"LicenseUrl: {license_url}");
            }

            LicenseMetadata license_meta = metadata.LicenseMetadata;
            if (license_meta != null)
            {
                meta_declared_licenses.Add($"LicenseType: {license_meta.Type}");
                if (!string.IsNullOrWhiteSpace(license_meta.License))
                    meta_declared_licenses.Add($"License: {license_meta.License}");
                var expression = license_meta.LicenseExpression;
                if (expression != null)
                    meta_declared_licenses.Add($"LicenseExpression: {license_meta.LicenseExpression}");
            }

            declared_license = string.Join("\n", meta_declared_licenses);

            // Update the parties
            string authors = metadata.Authors;
            if (!string.IsNullOrWhiteSpace(authors))
            {
                if (!parties.Any(p => p.name == authors && p.role == "author"))
                {
                    Party item = new() { type = "organization", role = "author", name = authors };
                    parties.Add(item);
                }
            }

            string owners = metadata.Owners;
            if (!string.IsNullOrWhiteSpace(owners))
            {
                if (!parties.Any(p => p.name == owners && p.role == "owner"))
                {
                    Party item = new() { type = "organization", role = "owner", name = owners };
                    parties.Add(item);
                }
            }

            // Update misc and URL fields
            primary_language = "C#";
            description = metadata.Description;

            string tags = metadata.Tags.Trim();
            if (!string.IsNullOrWhiteSpace(tags))
            {
                keywords = tags.Split(separator: ", ", options: StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            if (metadata.ProjectUrl != null)
                homepage_url = metadata.ProjectUrl.ToString();

            string name_lower = meta_name.ToLower();
            string version_lower = meta_version.ToLower();

            // TODO consider also: https://www.nuget.org/api/v2/package/{meta_name}/{meta_version}
            // TODO consider also: https://api.nuget.org/v3-flatcontainer/{name_lower}/{version_lower}/{name_lower}.nuspec
            // TODO consider instead: https://api.nuget.org/packages/{name_lower}.{version_lower}.nupkg
            // this is the URL from the home page
            download_url = $"https://api.nuget.org/v3-flatcontainer/{name_lower}/{version_lower}/{name_lower}.{version_lower}.nupkg";
            repository_download_url = download_url;

            repository_homepage_url = metadata.PackageDetailsUrl.ToString();
            api_data_url = $"https://api.nuget.org/v3/registration5-gz-semver2/{name_lower}/{version_lower}.json";

            // source_packages = null;
        }

        /// <summary>
        /// Sort recursively the dependencies.
        /// </summary>
        public void Sort() {
            dependencies.Sort();
            foreach (var dep in dependencies)
                dep.Sort();
        }

        bool IEquatable<BasePackage>.Equals(BasePackage? other)
        {
            if (other != null)
                return Equals(other);
            return false;
        }

        public (string, string, string, string, string, string) AsTuple()
        {
            return ValueTuple.Create(
                this.type.ToLowerInvariant(),
                this.namespace_.ToLowerInvariant(),
                this.name.ToLowerInvariant(),
                (this.version ?? "").ToLowerInvariant(),
                this.qualifiers.ToLowerInvariant(),
                this.subpath.ToLowerInvariant());
        }

        public int CompareTo(BasePackage? other)
        {
            if (other == null)
                return 1;
            return AsTuple().CompareTo(other.AsTuple());
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

    // TODO: unused
    public class DependentPackage
    {
        public string purl { get; set; } = "";
        public string extracted_requirement { get; set; } = "";
        public string scope { get; set; } = "";
        public bool is_runtime { get; set; }
        public bool is_optional { get; set; }
        public bool is_resolved { get; set; }
        public BasePackage? resolved_package { get; set; }
        public Dictionary<string, string> extra_data { get; set; } = new();
    }

    /// <summary>
    /// A PackageDownload has a URL and checkcum
    /// </summary>
    public class PackageDownload
    {
        public string download_url { get; set; } = "";
        public string hash { get; set; } = "";
        public string hash_algorithm { get; set; } = "";
        public int? size { get; set; } = 0;
        public SourcePackageDependencyInfo? package_info = null;

        public bool IsEnhanced(){
            return !string.IsNullOrWhiteSpace(download_url)
            && !string.IsNullOrWhiteSpace(hash);
        }
    }
}