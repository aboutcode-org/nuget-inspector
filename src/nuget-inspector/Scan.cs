namespace NugetInspector;

public class Scan
{
    public enum ResultStatus
    {
        Success,
        Error
    }

    public Exception? Exception;
    public string? OutputFilePath;
    public List<Package> Packages = new();
    public string? ResultName;
    public ResultStatus Status;
}

public class ScanOptions
{
    public string ProjectFilePath { get; set; } = "";
    public bool Verbose { get; set; }
    public string NugetApiFeedUrl { get; set; } = "";
    public string NugetConfigPath { get; set; } = "";
    public string OutputFilePath { get; set; } = "";
}