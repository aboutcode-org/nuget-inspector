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

    public DependencyResolution Resolve()
    {
        var result = new DependencyResolution();
        var tree = new NugetApiResolver(nugetApi: NugetApi);

        // This is the .NET core default version
        result.ProjectVersion = "1.0.0";
        Encoding.RegisterProvider(provider: CodePagesEncodingProvider.Instance);

        var doc = new XmlDocument();
        doc.Load(filename: ProjectPath);

        var packagesNodes = doc.GetElementsByTagName(name: "PackageReference");
        if (packagesNodes.Count > 0)
            foreach (XmlNode package in packagesNodes)
            {
                var attributes = package.Attributes;
                string? version_value = null;


                if (attributes != null)
                {
                    if (Config.TRACE)
                    {
                        Console.WriteLine($"ProjFileXmlParserPackageReferenceHandler: attributes {attributes}");
                    }

                    var include = attributes[name: "Include"];
                    if (include != null)
                    {
                        var version = attributes[name: "Version"];
                        if (version != null)
                        {
                            version_value = version.Value;
                        }
                        else
                        {
                            //try nested element instead of attribute  
                            foreach (XmlElement versionNode in package.ChildNodes)
                            {
                                if (versionNode.Name == "Version")
                                {
                                    if (Config.TRACE)
                                        Console.WriteLine(
                                            $"    no version attribute, using Version tag: {versionNode.InnerText}");
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
                if (!pkg.dependencies.Contains(item: package)) continue;
                has_references = true;
                break;
            }

            if (!has_references)
            {
                result.Dependencies.Add(item: package);
            }
        }

        return result;
    }
}