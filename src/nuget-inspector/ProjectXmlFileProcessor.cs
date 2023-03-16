using System.Text;
using System.Xml;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// Read the .*proj file directly as XML to extract PackageReference as a last resort
/// This handler reads a *.*proj file as plain XML and calls the NuGet API for resolution.
/// </summary>
internal class ProjectXmlFileProcessor : IDependencyProcessor
{
    public const string DatasourceId = "dotnet-project-xml";
    private readonly NuGetFramework? ProjectTargetFramework;
    private readonly NugetApi NugetApi;
    private readonly string ProjectPath;

    public ProjectXmlFileProcessor(string projectPath, NugetApi nugetApi,
        NuGetFramework? project_target_framework)
    {
        ProjectPath = projectPath;
        NugetApi = nugetApi;
        ProjectTargetFramework = project_target_framework;
    }

    /// <summary>
    /// Return a list of Dependency extracted from the raw XML of a project file.
    /// Note that this is used only as a fallback and does not handle the same
    /// breadth of attributes as with an MSBuild-based parsing. In particular
    /// this does not handle frameworks and conditions.
    /// </summary>
    public List<Dependency> GetDependencies()
    {
        if (Config.TRACE)
            Console.WriteLine($"ProjFileXmlParserPackageReferenceHandler.GetDependencies: ProjectPath {ProjectPath}");

        var dependencies = new List<Dependency>();

        Encoding.RegisterProvider(provider: CodePagesEncodingProvider.Instance);
        var doc = new XmlDocument();
        doc.Load(filename: ProjectPath);

        var packagesNodes = doc.GetElementsByTagName(name: "PackageReference");
        foreach (XmlNode package in packagesNodes)
        {
            var attributes = package.Attributes;
            string? version_value = null;

            if (attributes == null)
                continue;

            if (Config.TRACE)
                Console.WriteLine($"    attributes {attributes}");

            var include = attributes[name: "Include"];
            if (include == null)
                continue;

            var version = attributes[name: "Version"];
            if (version != null)
            {
                version_value = version.Value;
            }
            else
            {
                // XML is beautfiful: let's try nested element instead of direct attribute  
                foreach (XmlElement versionNode in package.ChildNodes)
                {
                    if (versionNode.Name == "Version")
                    {
                        if (Config.TRACE)
                            Console.WriteLine($"    no version attribute, using Version tag: {versionNode.InnerText}");
                        version_value = versionNode.InnerText;
                    }
                }
            }

            if (Config.TRACE)
                Console.WriteLine($"    version_value: {version_value}");

            VersionRange? version_range = null;
            if (version_value != null)
                version_range = VersionRange.Parse(value: version_value);
            var dep = new Dependency(
                name: include.Value,
                version_range: version_range,
                framework: ProjectTargetFramework,
                is_direct: true);
            dependencies.Add(dep);
        }
        return dependencies;
    }

    /// <summary>
    /// Resolve using a best effort when a project file cannot be processed
    /// otherwise. This is using raw XML of a project file directly.
    /// </summary>
    public DependencyResolution Resolve()
    {
        var tree = new NugetApiHelper(nugetApi: NugetApi);

        var resolution = new DependencyResolution
        {
            // This is the .NET core default version
            ProjectVersion = "1.0.0",
            Dependencies = new List<BasePackage>(),
            Packages = tree.GetPackageList()
        };

        foreach (var dep in GetDependencies())
        {
            tree.Resolve(packageDependency: dep);
        }

        foreach (var package in resolution.Packages)
        {
            var has_references = false;
            foreach (var pkg in resolution.Packages)
            {
                if (!pkg.dependencies.Contains(item: package)) continue;
                has_references = true;
                break;
            }

            if (!has_references)
            {
                resolution.Dependencies.Add(item: package);
            }
        }
        return resolution;
    }
}