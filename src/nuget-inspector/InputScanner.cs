namespace NugetInspector;

/// <summary>
/// Scan an input Project file based on options.
/// </summary>
internal class InputScanner
{
    public List<Scan>? Inspect(ScanOptions options, NugetApi nugetService)
    {
        return CreateInspectors(options: options, nugetService: nugetService)
            ?.Select(selector: scanner => scanner.RunScan()).ToList();
    }

    private IEnumerable<IScanner> CreateInspectors(ScanOptions options, NugetApi nugetService)
    {
        var inspectors = new List<IScanner>();
        if (File.Exists(path: options.ProjectFilePath))
        {
            var projectOp = new ProjectScannerOptions(old: options)
            {
                ProjectFilePath = options.ProjectFilePath
            };
            inspectors.Add(item: new ProjectFileScanner(options: projectOp, nuget_api_service: nugetService));
        }

        return inspectors;
    }
}