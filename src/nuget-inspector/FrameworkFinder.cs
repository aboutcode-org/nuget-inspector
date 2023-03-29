using System.Xml;
using NuGet.Frameworks;

namespace NugetInspector;

/// <summary>
/// Helper module to fidn the proper project frameworj
/// </summary>
public static class FrameworkFinder
{
    /// <summary>
    /// Return a tuple of (warning message or null, NuGetFramework) using either
    /// the requested framework or a found framework or a fallback to "any".
    /// </summary>
    public static (string?, NuGetFramework) GetFramework(string? RequestedFramework, string ProjectFilePath)
    {
        string? framework_warning = null;
        NuGetFramework project_framework;
        // Force using the provided framework if present
        if (!string.IsNullOrWhiteSpace(RequestedFramework))
        {
            project_framework = NuGetFramework.ParseFolder(folderName: RequestedFramework.ToLower());
            if (project_framework == NuGetFramework.UnsupportedFramework)
            {
                framework_warning = $"Unsupported framework requested: {RequestedFramework}, falling back to 'any' framework.";
                project_framework = NuGetFramework.AnyFramework;
            }
        }
        else
        {
            // TODO: Use the project model instead to obtain the framework
            // Or use the first framework found in the project
            string framework_moniker;
            (framework_moniker, project_framework) = FindProjectTargetFramework(ProjectFilePath);
            if (project_framework == NuGetFramework.UnsupportedFramework)
            {
                project_framework = NuGetFramework.AnyFramework;
                framework_warning = 
                    $"Unsupported framework found: {framework_moniker} in {ProjectFilePath}, " +
                    $"falling back to '{project_framework.GetShortFolderName()}' framework.";
            }
        }

        if (Config.TRACE)
            Console.WriteLine($"Effective project framework: {project_framework.GetShortFolderName()} {framework_warning}");

        return (framework_warning, project_framework);
    }

    /// <summary>
    /// Return the first NuGetFramework found in the *.*proj XML file or Any.
    /// Handles new and legacy style target framework references.
    /// </summary>
    /// <param name="project_file_path"></param>
    /// <returns></returns>
    public static (string, NuGetFramework) FindProjectTargetFramework(string project_file_path)
    {
        var doc = new XmlDocument();
        doc.Load(filename: project_file_path);

        var target_framework = doc.GetElementsByTagName(name: "TargetFramework");
        foreach (XmlNode tf in target_framework)
        {
            var framework_moniker = tf.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(framework_moniker))
            {
                return (framework_moniker, NuGetFramework.ParseFolder(framework_moniker));
            }
        }

        var target_framework_version = doc.GetElementsByTagName(name: "TargetFrameworkVersion");
        foreach (XmlNode tfv in target_framework_version)
        {
            var framework_version = tfv.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(framework_version))
            {
                var version = Version.Parse(framework_version.Trim('v', 'V'));
                return (framework_version, new NuGetFramework(FrameworkConstants.FrameworkIdentifiers.Net, version));
            }
        }

        var target_frameworks = doc.GetElementsByTagName(name: "TargetFrameworks");
        foreach (XmlNode tf in target_frameworks)
        {
            var framework_monikers = tf.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(framework_monikers))
            {
                var monikers = framework_monikers.Split(";", StringSplitOptions.RemoveEmptyEntries);
                foreach (var moniker in monikers)
                {
                     if (!string.IsNullOrWhiteSpace(moniker))
                        return (moniker, NuGetFramework.ParseFolder(moniker.Trim()));
                }
            }
        }
        // fallback to any if none are specified
        return ("any", NuGetFramework.AnyFramework);
    }
}