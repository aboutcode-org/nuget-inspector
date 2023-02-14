namespace NugetInspector;

/// <summary>
/// Extract version from an AssemblyInfo.cs file
/// https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/general/assembly-version-assembly-file-version
/// For example:
///   [assembly: AssemblyVersion("1.0.0.0")]
/// </summary>
public static class AssemblyInfoParser

{
    public class AssemblyVersion
    {
        public string? Version { get; set; }
        public string Path { get; set; }

        public AssemblyVersion(string? version, string path)
        {
            this.Version = version;
            this.Path = path;
        }

        /// <summary>
        /// Return an AssemblyVersion from an AssemblyInfo.cs
        /// These are of the form [assembly: AssemblyVersion("1.0.0.0")]
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static AssemblyVersion? ParseVersion(string path)
        {
            var lines = new List<string>(collection: File.ReadAllLines(path: path));
            lines = lines.FindAll(match: text => !text.Contains(value: "//"));
            // Search first for AssemblyFileVersion
            var version_lines = lines.FindAll(match: text => text.Contains(value: "AssemblyFileVersion"));
            // The fallback to AssemblyVersion
            if (version_lines.Count == 0)
                version_lines = lines.FindAll(match: text => text.Contains(value: "AssemblyVersion"));

            foreach (var text in version_lines)
            {
                var version_line = text.Trim();
                // the form is [assembly: AssemblyVersion("1.0.0.0")]
                var start = version_line.IndexOf(value: "(", comparisonType: StringComparison.Ordinal) + 2;
                var end = version_line.LastIndexOf(value: ")", comparisonType: StringComparison.Ordinal) - 1 - start;
                var version = version_line.Substring(startIndex: start, length: end);
                if (Config.TRACE) Console.WriteLine($"Assembly version '{version}' in '{path}'.");
                return new AssemblyVersion(version: version, path: path);
            }

            return null;
        }
    }

    /// <summary>
    /// Return a version string or nul from reading an AssemblyInfo file somewhere in the project tree. 
    /// </summary>
    /// <param name="project_directory"></param>
    /// <returns></returns>
    public static string? GetProjectAssemblyVersion(string? project_directory)
    {
        try
        {
            List<AssemblyVersion?> results = Directory
                .GetFiles(path: project_directory!, searchPattern: "*AssemblyInfo.*",
                    searchOption: SearchOption.AllDirectories).ToList()
                .Select(selector: path =>
                {
                    if (!path.EndsWith(value: ".obj") && File.Exists(path: path))
                    {
                        return AssemblyVersion.ParseVersion(path: path);
                    }

                    return null;
                })
                .Where(predicate: it => it != null)
                .ToList();

            if (results.Count > 0)
            {
                var selected = results[0];
                if (selected is null) return null;
                if (Config.TRACE)
                    Console.WriteLine($"Selected version '{selected.Version}' from '{selected.Path}'.");
                return selected.Version;
            }
        }
        catch (Exception e)
        {
            if (Config.TRACE)
                Console.WriteLine(value: $"Failed to collect AssemblyInfo version for project: {project_directory}{e.Message}");
        }

        return null;
    }
}