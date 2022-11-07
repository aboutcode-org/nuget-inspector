namespace NugetInspector;


/// <summary>
/// Extract version from an AssemblyInfo.cs file
/// https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/general/assembly-version-assembly-file-version
/// For example:
///   [assembly: AssemblyVersion("1.0.0.0")]
/// </summary>
public class AssemblyInfoParser

{
    public class AssemblyVersion
    {
        public AssemblyVersion(string? version, string path)
        {
            this.Version = version;
            this.Path = path;
        }

        public string? Version { get; }
        public string Path { get; }

        /// <summary>
        /// Return an AssemblyVersion from an AssemblyInfo.cs
        /// These are of the form [assembly: AssemblyVersion("1.0.0.0")]
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static AssemblyVersion? ParseVersion(string path)
        {
            var lines = new List<string>(File.ReadAllLines(path));
            lines = lines.FindAll(text => !text.Contains("//"));
            // Search first for AssemblyFileVersion
            var versionLines = lines.FindAll(text => text.Contains("AssemblyFileVersion"));
            // The fallback to AssemblyVersion
            if (versionLines.Count == 0)
                versionLines = lines.FindAll(text => text.Contains("AssemblyVersion"));

            foreach (var text in versionLines)
            {
                var versionLine = text.Trim();
                // the form is [assembly: AssemblyVersion("1.0.0.0")]
                var start = versionLine.IndexOf("(", StringComparison.Ordinal) + 2;
                var end = versionLine.LastIndexOf(")", StringComparison.Ordinal) - 1 - start;
                var version = versionLine.Substring(start, end);
                if (Config.TRACE) Console.WriteLine($"Assembly version '{version}' in '{path}'.");
                return new AssemblyVersion(version, path);
            }

            return null;
        }
    }

    /// <summary>
    /// Return a version string or nul.
    /// </summary>
    /// <param name="projectDirectory"></param>
    /// <returns></returns>
    public static string? GetProjectAssemblyVersion(string? projectDirectory)
    {
        try
        {
            List<AssemblyVersion?> results = Directory
                .GetFiles(projectDirectory, "*AssemblyInfo.*", SearchOption.AllDirectories).ToList()
                .Select(path =>
                {
                    if (!path.EndsWith(".obj") && File.Exists(path))
                    {
                        return AssemblyVersion.ParseVersion(path);
                    }

                    return null;
                })
                .Where(it => it != null)
                .ToList();

            if (results.Count > 0)
            {
                var selected = results.First();
                if (selected is null) return null;
                if (Config.TRACE) Console.WriteLine($"Selected version '{selected.Version}' from '{selected.Path}'.");
                return selected.Version;
            }
        }
        catch (Exception e)
        {
            if (Config.TRACE) Console.WriteLine("Failed to collect AssemblyInfo version for project: " + projectDirectory + e.Message);
        }

        return null;
    }
}