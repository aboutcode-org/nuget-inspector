namespace NugetInspector;

/// <summary>
/// Recognize and scan an input Project or Solution file based on options.
/// </summary>
internal class InputScanner
{
    public List<Scan>? Inspect(ScanOptions options, NugetApi nugetService)
    {
        return CreateInspectors(options: options, nugetService: nugetService)?.Select(selector: scanner => scanner.RunScan()).ToList();
    }

    private IEnumerable<IScanner> CreateInspectors(ScanOptions options, NugetApi nugetService)
    {
        var inspectors = new List<IScanner>();
        if (Directory.Exists(path: options.ProjectFilePath))
        {
            if (Config.TRACE) Console.WriteLine(value: "Searching for solution files to process...");
            var solution_paths = Directory.GetFiles(path: options.ProjectFilePath, searchPattern: "*.sln", searchOption: SearchOption.AllDirectories)
                .ToHashSet();

            if (solution_paths is { Count: >= 1 })
            {
                foreach (var solution in solution_paths)
                {
                    if (Config.TRACE) Console.WriteLine(format: "Found Solution {0}", arg0: solution);
                    var solution_options = new SolutionScannerOptions(old: options)
                    {
                        ProjectFilePath = solution
                    };
                    inspectors.Add(item: new SolutionFileScanner(options: solution_options, nugetService: nugetService));
                }
            }
            else
            {
                if (Config.TRACE) Console.WriteLine(value: "No Solution file found.  Searching for a project file...");
                var project_paths = Directory.GetFiles(path: options.ProjectFilePath, searchPattern: "*.*proj", searchOption: SearchOption.AllDirectories);
                if (project_paths.Length > 0)
                    foreach (var projectPath in project_paths)
                    {
                        if (Config.TRACE) Console.WriteLine(format: "Found project {0}", arg0: projectPath);
                        var projectOp = new ProjectScannerOptions(old: options)
                        {
                            ProjectFilePath = projectPath
                        };
                        inspectors.Add(item: new ProjectFileScanner(options: projectOp, nugetApiService: nugetService));
                    }
            }
        }
        else if (File.Exists(path: options.ProjectFilePath))
        {
            if (options.ProjectFilePath.Contains(value: ".sln"))
            {
                var solutionOp = new SolutionScannerOptions(old: options)
                {
                    ProjectFilePath = options.ProjectFilePath
                };
                inspectors.Add(item: new SolutionFileScanner(options: solutionOp, nugetService: nugetService));
            }
            else
            {
                var projectOp = new ProjectScannerOptions(old: options)
                {
                    ProjectFilePath = options.ProjectFilePath
                };
                inspectors.Add(item: new ProjectFileScanner(options: projectOp, nugetApiService: nugetService));
            }
        }

        return inspectors;
    }
}