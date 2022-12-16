﻿using Newtonsoft.Json;

namespace NugetInspector;

/// <summary>
/// Dump results to JSON
/// </summary>
public class ScanHeader
{
    public string tool_name = "nuget-inspector";
    public string tool_homepageurl = "https://github.com/nexB/nuget-inspector";
    public string tool_version = "0.7.0";
    public List<string> options;

    public string notice = "Dependency tree generated with nuget-inspector.\n" +
                           "nuget-inspector is a free software tool from nexB Inc. and others.\n" +
                           "Visit https://github.com/nexB/nuget-inspector/ for support and download.";

    public List<string> warnings = new();
    public List<string> errors = new();

    public ScanHeader(Options options)
    {
        this.options = options.AsCliList();
    }
}

public class ScanOutput
{
    [JsonProperty(propertyName: "headers")]
    public List<ScanHeader> Headers = new();

    // "type": "file",
    // "path": "/home/tg1999/Desktop/python-inspector-1/tests/data/azure-devops.req.txt",
    // "package_data": [
    [JsonProperty(propertyName: "files")] public List<Package?> Files = new();

    [JsonProperty(propertyName: "packages")]
    public List<Package?> Packages = new();
}

internal class OutputFormatJson
{
    private readonly ScanOutput scan_output;
    private readonly ScanResult Result;

    public OutputFormatJson(ScanResult result)
    {
        Result = result;
        scan_output = new ScanOutput
        {
            Packages = result.Packages!
        };
        ScanHeader scan_header = new(result.Options!);
        scan_output.Headers.Add(scan_header);
    }

    public void Write()
    {
        var output_file_path = Result.Options!.OutputFilePath;
        if (Config.TRACE) Console.WriteLine($"Creating output file path: {output_file_path}");
        using (var fs = new FileStream(path: output_file_path!, mode: FileMode.Create))
        {
            using (var sw = new StreamWriter(stream: fs))
            {
                var serializer = new JsonSerializer
                {
                    Formatting = Formatting.Indented
                };
                var writer = new JsonTextWriter(textWriter: sw);
                serializer.Serialize(jsonWriter: writer, value: scan_output);
            }
        }
    }
}