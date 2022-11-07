using Newtonsoft.Json;

namespace NugetInspector;

public class ScanOutput
{
    public string tool_name = "nuget-inspector";
    public string tool_version = "0.5.0";
    [JsonProperty("packages")] public List<Package?> Packages = new();

}

internal class OutputFormatJson
{
    private readonly ScanOutput scanOutput;
    private readonly Scan Result;

    public OutputFormatJson(Scan result)
    {
        Result = result;
        scanOutput = new ScanOutput();
        scanOutput.Packages = result.Packages;
    }

    public string? OutputFilePath()
    {
        return Result.OutputFilePath;
    }

    public void Write()
    {
        Write(Result.OutputFilePath);
    }

    public void Write(string? outputFilePath)
    {
        if (Config.TRACE) Console.WriteLine("Creating output file path: " + outputFilePath);
        using (var fs = new FileStream(outputFilePath, FileMode.Create))
        {
            using (var sw = new StreamWriter(fs))
            {
                var serializer = new JsonSerializer();
                serializer.NullValueHandling = NullValueHandling.Ignore;
                var writer = new JsonTextWriter(sw);
                serializer.Formatting = Formatting.Indented;
                serializer.Serialize(writer, scanOutput);
            }
        }
    }
}