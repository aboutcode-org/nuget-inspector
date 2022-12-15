using Newtonsoft.Json;

namespace NugetInspector;

/// <summary>
/// Dump results to JSON
/// </summary>


public class Header
{
    public string tool_name = "nuget-inspector";
    public string tool_homepageurl = "https://github.com/nexB/nuget-inspector";
    public string tool_version = "0.7.0";
}


public class ScanOutput
{
    // {
    //     "headers": {
    //         "tool_name": "python-inspector",
    //         "tool_homepageurl": "https://github.com/nexB/python-inspector",
    //         "tool_version": "0.9.3",
    //         "options": [
    //         "--requirement /home/tg1999/Desktop/python-inspector-1/tests/data/azure-devops.req.txt",
    //         "--index-url https://pypi.org/simple",
    //         "--python-version 38",
    //         "--operating-system linux",
    //         "--json <file>"
    //             ],
    //         "notice": "Dependency tree generated with python-inspector.\npython-inspector is a free software tool from nexB Inc. and others.\nVisit https://github.com/nexB/python-inspector/ for support and download.",
    //         "warnings": [],
    //         "errors": []
    //     },
    //     "files": [
    //     {
    //         "type": "file",
    //         "path": "/home/tg1999/Desktop/python-inspector-1/tests/data/azure-devops.req.txt",
    //         "package_data": [
    //         {
    
    [JsonProperty(propertyName: "headers")]
    public List<Header> Headers = new();
    
    [JsonProperty(propertyName: "packages")]
    public List<Package?> Packages = new();
    
    
}


internal class OutputFormatJson
{
    private readonly ScanOutput scanOutput;
    private readonly ScanResult Result;

    public OutputFormatJson(ScanResult result)
    {
        Result = result;
        scanOutput = new ScanOutput
        {
            Packages = result.Packages!
        };
        Header header = new();
        scanOutput.Headers.Add(header);
    }

    public string? OutputFilePath()
    {
        return Result.OutputFilePath;
    }

    public void Write()
    {
        Write(outputFilePath: Result.OutputFilePath);
    }

    public void Write(string? outputFilePath)
    {
        if (Config.TRACE) Console.WriteLine($"Creating output file path: {outputFilePath}");
        using (var fs = new FileStream(path: outputFilePath!, mode: FileMode.Create))
        {
            using (var sw = new StreamWriter(stream: fs))
            {
                var serializer = new JsonSerializer();
                serializer.NullValueHandling = NullValueHandling.Ignore;
                var writer = new JsonTextWriter(textWriter: sw);
                serializer.Formatting = Formatting.Indented;
                serializer.Serialize(jsonWriter: writer, value: scanOutput);
            }
        }
    }
}