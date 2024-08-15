using Newtonsoft.Json;

namespace NugetInspector;

/// <summary>
/// Dump results to JSON
/// </summary>
public class ScanHeader
{
    #pragma warning disable IDE1006
    public string tool_name { get; set; } = "nuget-inspector";
    public string tool_homepageurl { get; set; } = "https://github.com/aboutcode-org/nuget-inspector";
    public string tool_version { get; set; } = Config.NUGET_INSPECTOR_VERSION;
    public List<string> options { get; set; }

    public string project_framework { get; set; } = "";

    public string notice { get; set; } = "Dependency tree generated with nuget-inspector.\n" +
                                         "nuget-inspector is a free software tool from nexB Inc. and others.\n" +
                                         "Visit https://github.com/aboutcode-org/nuget-inspector/ for support and download.";

    public List<string> warnings { get; set; } = new();
    public List<string> errors { get; set; } = new();
    #pragma warning restore IDE1006
    public ScanHeader(Options options)
    {
        this.options = options.AsCliList();
    }
}

public class ScanOutput
{
    [JsonProperty(propertyName: "headers")]
    public List<ScanHeader> Headers { get; set; } = new();

    [JsonProperty(propertyName: "files")]
    public List<ScannedFile> Files { get; set; } = new();

    [JsonProperty(propertyName: "packages")]
    public List<BasePackage> Packages { get; set; } = new();

    [JsonProperty(propertyName: "dependencies")]
    public List<BasePackage> Dependencies { get; set; } = new();
}

internal class OutputFormatJson
{
    public readonly ScanOutput scan_output;
    public readonly ScanResult scan_result;

    public OutputFormatJson(ScanResult scan_result)
    {
        scan_result.Sort();
        this.scan_result = scan_result;

        scan_output = new ScanOutput();
        scan_output.Packages.Add(scan_result.project_package);

        ScanHeader scan_header = new(scan_result.Options!)
        {
            project_framework = scan_result.Options!.ProjectFramework!,
            warnings = scan_result.warnings,
            errors = scan_result.errors
        };
        scan_output.Headers.Add(scan_header);
        scan_output.Dependencies = scan_result.project_package.GetFlatDependencies();
    }

    public void Write()
    {
        var output_file_path = scan_result.Options!.OutputFilePath;
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
