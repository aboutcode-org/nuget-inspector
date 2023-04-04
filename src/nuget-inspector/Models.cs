using Newtonsoft.Json;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NugetInspector
{
    #pragma warning disable IDE1006
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

            package_with_deps = BasePackage.FromPackage(package: package, dependencies: new List<BasePackage>());
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
        public void AddOrUpdatePackage(BasePackage base_package, List<BasePackage> dependencies)
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
        public List<string> warnings { get; set; } = new();
        public List<string> errors { get; set; } = new();

        // Track if we updated this package metadata
        [JsonIgnore]
        public bool has_updated_metadata;

       public BasePackage(){}

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

        public static BasePackage FromPackage(BasePackage package, List<BasePackage> dependencies)
        {
            return new(name: package.name, version: package.version)
            {
                extra_data = package.extra_data,
                dependencies = dependencies
            };
        }

        ///<summary>
        /// Return a deep clone of this package. Optionally clone dependencies.
        ///</summary>
        public BasePackage Clone(bool with_deps=false)
        {
            List<BasePackage> deps = with_deps ? dependencies : new List<BasePackage>();

            return new BasePackage(
                name: name,
                version:version,
                datafile_path: datafile_path
            )
            {
                type = type,
                namespace_ = namespace_,

                qualifiers = qualifiers,
                subpath = subpath,
                purl = purl,
                primary_language = primary_language,
                description = description,
                release_date = release_date,
                parties = new List<Party>(parties.Select(p => p.Clone())),
                keywords = new List<string>(keywords),
                homepage_url = homepage_url,
                download_url = download_url,
                size = size,
                sha1 = sha1,
                md5 = md5,
                sha256 = sha256,
                sha512 = sha512,
                bug_tracking_url = bug_tracking_url,
                code_view_url = code_view_url,
                vcs_url = vcs_url,
                copyright = copyright,
                license_expression = license_expression,
                declared_license = declared_license,
                notice_text = notice_text,
                source_packages = new List<string>(source_packages),
                repository_homepage_url = repository_homepage_url,
                repository_download_url = repository_download_url,
                api_data_url = api_data_url,
                datasource_id = datasource_id,
                datafile_path = datafile_path,
                warnings = warnings,
                errors = errors,
                dependencies = deps,
                extra_data = new Dictionary<string, string>(extra_data),
                has_updated_metadata = has_updated_metadata
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
                return new PackageIdentity(id: name, version: new NuGetVersion(version));
            else
                return new PackageIdentity(id: name, version: null);
        }

        /// <summary>
        /// Update this Package instance using the NuGet API to fetch extra metadata
        /// and also update all its dependencies recursively.
        /// </summary>
        public void Update(NugetApi nugetApi, bool with_details = false)
        {
            if (has_updated_metadata)
                return;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
            {
                errors.Add("ERROR: Cannot fetch remote metadata: Name or version cannot be empty");
                return;
            }

            try
            {
                UpdateWithRemoteMetadata(nugetApi, with_details: with_details);
            }
            catch (Exception ex)
            {
                var message = $"Failed to get remote metadata for name: '{name}' version: '{version}'. ";
                if (Config.TRACE) Console.WriteLine($"        {message}");
                warnings.Add(message + ex.ToString());
            }
            has_updated_metadata = true;

            foreach (var dep in dependencies)
                dep.Update(nugetApi, with_details: with_details);
        }

        /// <summary>
        /// Update this Package instance using the NuGet API to fetch metadata
        /// </summary>
        public void UpdateWithRemoteMetadata(NugetApi nugetApi, bool with_details = false)
        {
            {
                PackageIdentity pid = GetPackageIdentity();
                PackageSearchMetadataRegistration? psmr = nugetApi.FindPackageVersion(pid: pid);

                // TODO: need to add an error to errors
                if (psmr == null)
                    return;

                // Also fetch download URL and package hash
                PackageDownload? download = nugetApi.GetPackageDownload(identity: pid, with_details: with_details);
                SourcePackageDependencyInfo? spdi = nugetApi.GetResolvedSourcePackageDependencyInfo(pid, framework: null);
                UpdateAttributes(metadata: psmr, download: download, spdi: spdi);
            }
        }

        /// <summary>
        /// Update this Package instance
        /// </summary>
        public void UpdateAttributes(
            PackageSearchMetadataRegistration? metadata,
            PackageDownload? download,
            SourcePackageDependencyInfo? spdi)
        {
            string? synthetic_api_data_url = null;

            if (metadata != null)
            {
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
                    meta_declared_licenses.Add($"LicenseUrl: {license_url}");

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
                if (!string.IsNullOrWhiteSpace(authors) && !parties.Any(p => p.name == authors && p.role == "author"))
                {
                    Party item = new() { type = "organization", role = "author", name = authors };
                    parties.Add(item);
                }

                string owners = metadata.Owners;
                if (!string.IsNullOrWhiteSpace(owners) && !parties.Any(p => p.name == owners && p.role == "owner"))
                {
                    Party item = new() { type = "organization", role = "owner", name = owners };
                    parties.Add(item);
                }

                // Update misc and URL fields
                primary_language = "C#";
                description = metadata.Description;

                string tags = metadata.Tags;
                if (!string.IsNullOrWhiteSpace(tags))
                {
                    tags = tags.Trim();
                    keywords = tags.Split(separator: ", ", options: StringSplitOptions.RemoveEmptyEntries).ToList();
                }

                if (metadata.ProjectUrl != null)
                    homepage_url = metadata.ProjectUrl.ToString();

                string name_lower = meta_name.ToLower();
                string version_lower = meta_version.ToLower();

                if (metadata.PackageDetailsUrl != null)
                    repository_homepage_url = metadata.PackageDetailsUrl.ToString();

                synthetic_api_data_url = $"https://api.nuget.org/v3/registration5-gz-semver2/{name_lower}/{version_lower}.json";
            }

            if (download != null)
            {
                // Download data
                if (string.IsNullOrWhiteSpace(sha512))
                    sha512 = download.hash;

                if (size == 0 && download.size != null && download.size > 0)
                    size = (int)download.size;

                if (!string.IsNullOrWhiteSpace(download.download_url))
                {
                    download_url = download.download_url;
                    repository_download_url = download_url;
                }

                if (Config.TRACE_NET) Console.WriteLine($"        download_url:{download_url}");

                // other URLs

                if (
                    string.IsNullOrWhiteSpace(api_data_url)
                    && download_url.StartsWith("https://api.nuget.org/")
                    && !string.IsNullOrWhiteSpace(synthetic_api_data_url)
                )
                {
                    api_data_url = synthetic_api_data_url;
                }
                else
                {
                    try
                    {
                        if (spdi != null && metadata != null)
                            api_data_url = GetApiDataUrl(pid: metadata.Identity, spdi: spdi);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(ex.ToString());
                    }
                }
                if (Config.TRACE_NET) Console.WriteLine($"         api_data_url:{api_data_url}");
            }

            // TODO consider also: https://api.nuget.org/v3-flatcontainer/{name_lower}/{version_lower}/{name_lower}.nuspec
        }

        public static string GetApiDataUrl(PackageIdentity pid, SourcePackageDependencyInfo? spdi)
        {
            if (spdi != null)
            {
                RegistrationResourceV3 rrv3 = spdi.Source.GetResource<RegistrationResourceV3>(CancellationToken.None);
                if (rrv3 != null)
                    return rrv3.GetUri(pid).ToString();
            }
            return "";
        }

        /// <summary>
        /// Sort recursively the dependencies of this package.
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

        /// <summary>
        /// Return a flat list of dependencies collected from a list of top-level packages.
        /// </summary>
        public List<BasePackage> GetFlatDependencies()
        {
            var flat_deps = FlattenDeps(dependencies);
            flat_deps.Sort();
            return flat_deps;
        }

        /// <summary>
        /// Flatten recursively a tree of dependencies. Remove subdeps as the flattening goes.
        /// </summary>
        public static List<BasePackage> FlattenDeps(List<BasePackage> dependencies)
        {
            List<BasePackage> flattened = new();
            List<BasePackage> depdeps;
            foreach (var dep in dependencies)
            {
                depdeps = dep.dependencies;
                flattened.Add(dep.Clone());
                flattened.AddRange(FlattenDeps(depdeps));
            }
            return flattened;
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

        public Party Clone()
        {
            return new Party(){
                type=type,
                role=role,
                name=name,
                email=email,
                url=url
            };
        }
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
        public bool IsEnhanced(){
            return !string.IsNullOrWhiteSpace(download_url) && !string.IsNullOrWhiteSpace(hash);
        }

        public static PackageDownload FromSpdi(SourcePackageDependencyInfo spdi)
        {
            PackageDownload download = new(){ download_url = spdi.DownloadUri.ToString() };
            /// Note that this hash is unlikely there per https://github.com/NuGet/NuGetGallery/issues/9433
            if (!string.IsNullOrEmpty(spdi.PackageHash))
            {
                download.hash = spdi.PackageHash;
                download.hash_algorithm = "SHA512";
            }
            return download;
        }

        public override string ToString()
        {
            return $"{download_url} hash: {hash} hash_algorithm: {hash_algorithm} size: {size}";
        }
    }

    public class ScannedFile
    {
        public string path { get; set; } = "";
        // file or directory
        public string type { get; set; } = "file";
        public string name { get; set; } = "";
        public string base_name { get; set; } = "";
        public string extension { get; set; } = "";
        public int? size { get; set; } = 0;
    }
}