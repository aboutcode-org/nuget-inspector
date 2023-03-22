using Newtonsoft.Json;

namespace NugetInspector;

/// <summary>
/// Dump results to JSON
/// </summary>
public class ScanHeader
{
    public string tool_name { get; set; } = "nuget-inspector";
    public string tool_homepageurl { get; set; } = "https://github.com/nexB/nuget-inspector";
    public string tool_version { get; set; } = Config.NUGET_INSPECTOR_VERSION;
    public List<string> options { get; set; }

    public string project_framework { get; set; } = "";

    public string notice { get; set; } = "Dependency tree generated with nuget-inspector.\n" +
                                         "nuget-inspector is a free software tool from nexB Inc. and others.\n" +
                                         "Visit https://github.com/nexB/nuget-inspector/ for support and download.";

    public List<string> warnings { get; set; } = new();
    public List<string> errors { get; set; } = new();

    public ScanHeader(Options options)
    {
        this.options = options.AsCliList();
    }
}

public class ScanOutput
{
    [JsonProperty(propertyName: "headers")]
    public List<ScanHeader> Headers { get; set; } = new();

    [JsonProperty(propertyName: "files")] public List<BasePackage> Files { get; set; } = new();

    [JsonProperty(propertyName: "packages")]
    public List<BasePackage> Packages { get; set; } = new();

    [JsonProperty(propertyName: "dependencies")]
    public List<BasePackage> Dependencies { get; set; } = new();

}

internal class OutputFormatJson
{
    private readonly ScanOutput scan_output;
    private readonly ScanResult Result;

    public OutputFormatJson(ScanResult result)
    {
        result.Sort();
        Result = result;
        scan_output = new ScanOutput
        {
            Packages = result.Packages
        };
        ScanHeader scan_header = new(result.Options!)
        {
            project_framework = result.Options!.ProjectFramework!
        };
        scan_output.Headers.Add(scan_header);
        scan_output.Dependencies = GetDependencies(result.Packages);
    }

    /// <summary>
    /// Return a flat list of dependencies collected from a list of top-level packages.
    /// </summary>
    public static List<BasePackage> GetDependencies(List<BasePackage> packages)
    {
        var flat_deps = new List<BasePackage>();
        foreach (var package in packages)
        {
            flat_deps.AddRange(FlattenDeps(package.dependencies));
        }
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

    public void Write()
    {
        var output_file_path = Result.Options!.OutputFilePath;
        using var fs = new FileStream(path: output_file_path!, mode: FileMode.Create);
        using var sw = new StreamWriter(stream: fs);
        var serializer = new JsonSerializer
        {
            Formatting = Formatting.Indented
        };
        var writer = new JsonTextWriter(textWriter: sw);
        serializer.Serialize(jsonWriter: writer, value: scan_output);
    }
}