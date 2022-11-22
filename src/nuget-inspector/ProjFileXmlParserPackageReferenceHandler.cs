using System.Text;
using System.Xml;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace NugetInspector;

/// <summary>
/// Read the .*proj file directly as XML to extract PackageReference as a last resort
/// This handler reads a *.*proj file as plain XML and calls the NuGet API for resolution. 
/// </summary>
internal class ProjFileXmlParserPackageReferenceHandler : IDependencyResolver
{
    public const string DatasourceId = "dotnet-project-xml";
    private readonly NuGetFramework? ProjectTargetFramework;
    private readonly NugetApi NugetApi;

    private readonly string ProjectPath;

    public ProjFileXmlParserPackageReferenceHandler(string projectPath, NugetApi nugetApi,
        NuGetFramework? projectTargetFramework)
    {
        ProjectPath = projectPath;
        NugetApi = nugetApi;
        ProjectTargetFramework = projectTargetFramework;
    }

    public DependencyResolution Process()
    {
        var result = new DependencyResolution();
        var tree = new NugetApiResolver(nugetApi: NugetApi);

        // This is the .NET core default version
        result.ProjectVersion = "1.0.0";
        Encoding.RegisterProvider(provider: CodePagesEncodingProvider.Instance);

        var doc = new XmlDocument();
        doc.Load(filename: ProjectPath);

        var versionNodes = doc.GetElementsByTagName(name: "Version");
        if (versionNodes != null && versionNodes.Count > 0)
        {
            foreach (XmlNode version in versionNodes)
                if (version.NodeType != XmlNodeType.Comment)
                    result.ProjectVersion = version.InnerText;
        }
        else
        {
            var prefix = "1.0.0";
            var suffix = "";
            var prefixNodes = doc.GetElementsByTagName(name: "VersionPrefix");
            if (prefixNodes != null && prefixNodes.Count > 0)
                foreach (XmlNode prefixNode in prefixNodes)
                    if (prefixNode.NodeType != XmlNodeType.Comment)
                        prefix = prefixNode.InnerText;
            var suffixNodes = doc.GetElementsByTagName(name: "VersionSuffix");
            if (suffixNodes != null && suffixNodes.Count > 0)
                foreach (XmlNode suffixNode in suffixNodes)
                    if (suffixNode.NodeType != XmlNodeType.Comment)
                        suffix = suffixNode.InnerText;
            result.ProjectVersion = $"{prefix}-{suffix}";
        }

        var packagesNodes = doc.GetElementsByTagName(name: "PackageReference");
        if (packagesNodes.Count > 0)
            foreach (XmlNode package in packagesNodes)
            {
                var attributes = package.Attributes;
                if (attributes != null)
                {
                    var include = attributes[name: "Include"];
                    var version = attributes[name: "Version"];
                    if (include != null && version != null)
                    {
                        var dep = new Dependency(name: include.Value, version_range: VersionRange.Parse(value: version.Value),
                            framework: ProjectTargetFramework);
                        tree.Add(packageDependency: dep);
                    }
                }
            }

        result.Packages = tree.GetPackageList();
        result.Dependencies = new List<BasePackage>();
        foreach (var package in result.Packages)
        {
            var has_references = false;
            foreach (var pkg in result.Packages)
            {
                if (!pkg.Dependencies.Contains(item: package.PackageId)) continue;
                has_references = true;
                break;
            }

            if (!has_references && package.PackageId != null)
            {
                result.Dependencies.Add(item: package.PackageId);
            }
        }

        return result;
    }
}