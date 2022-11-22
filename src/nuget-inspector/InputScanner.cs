namespace NugetInspector;

/// <summary>
/// Recognize and scan an input Project or Solution file based on options.
/// </summary>
internal class InputScanner
{
    public List<Scan>? Inspect(ScanOptions options, NugetApi nugetService)
    {
        return CreateInspectors(options, nugetService)?.Select(scanner => scanner.RunScan()).ToList();
    }

    private IEnumerable<IScanner> CreateInspectors(ScanOptions options, NugetApi nugetService)
    {
        var inspectors = new List<IScanner>();
        if (Directory.Exists(options.ProjectFilePath))
        {
            if (Config.TRACE) Console.WriteLine("Searching for solution files to process...");
            var solutionPaths = Directory.GetFiles(options.ProjectFilePath, "*.sln", SearchOption.AllDirectories)
                .ToHashSet();

            if (solutionPaths is { Count: >= 1 })
            {
                foreach (var solution in solutionPaths)
                {
                    if (Config.TRACE) Console.WriteLine("Found Solution {0}", solution);
                    var solutionOp = new SolutionScannerOptions(options)
                    {
                        ProjectFilePath = solution
                    };
                    inspectors.Add(new SolutionFileScanner(solutionOp, nugetService));
                }
            }
            else
            {
                if (Config.TRACE) Console.WriteLine("No Solution file found.  Searching for a project file...");
                var projectPaths = Directory.GetFiles(options.ProjectFilePath, "*.*proj", SearchOption.AllDirectories);
                if (projectPaths.Length > 0)
                    foreach (var projectPath in projectPaths)
                    {
                        if (Config.TRACE) Console.WriteLine("Found project {0}", projectPath);
                        var projectOp = new ProjectScannerOptions(options)
                        {
                            ProjectFilePath = projectPath
                        };
                        inspectors.Add(new ProjectFileScanner(projectOp, nugetService));
                    }
            }
        }
        else if (File.Exists(options.ProjectFilePath))
        {
            if (options.ProjectFilePath.Contains(".sln"))
            {
                var solutionOp = new SolutionScannerOptions(options)
                {
                    ProjectFilePath = options.ProjectFilePath
                };
                inspectors.Add(new SolutionFileScanner(solutionOp, nugetService));
            }
            else
            {
                var projectOp = new ProjectScannerOptions(options)
                {
                    ProjectFilePath = options.ProjectFilePath
                };
                inspectors.Add(new ProjectFileScanner(projectOp, nugetService));
            }
        }

        return inspectors;
    }
}