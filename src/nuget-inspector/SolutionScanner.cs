using System.Diagnostics;

namespace NugetInspector;

internal class SolutionScannerOptions : ScanOptions
{
    public SolutionScannerOptions()
    {
    }

    public SolutionScannerOptions(ScanOptions old)
    {
        ProjectFilePath = old.ProjectFilePath;
        Verbose = old.Verbose;
        NugetApiFeedUrl = old.NugetApiFeedUrl;
        OutputFilePath = old.OutputFilePath;
    }

    public string? SolutionName { get; set; }
}

public class SolutionProjectReference
{
    public string? GUID;
    public string? Name;
    public string? Path;
    public string? TypeGUID;

    /// <summary>
    /// Return a SolutionProject or null extracted from a .sln file "Project" line
    /// These lines have this form:
    ///   Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "nuget-inspector", "nuget-inspector\nuget-inspector.csproj", "{11C8B8AB-5476-42CD-961A-4DDCD9C9C0FD}"
    /// Note that some project can be on multiple lines but this does not matter here
    /// </summary>
    /// <param name="projectLine"></param>
    /// <returns></returns>
    public static SolutionProjectReference? Parse(string projectLine)
    {
        var equalSplit = projectLine.Split(separator: '=').Select(selector: s => s.Trim()).ToList();
        if (equalSplit.Count() < 2) return null;

        var file = new SolutionProjectReference();
        var leftSide = equalSplit[index: 0];
        var rightSide = equalSplit[index: 1];
        if (leftSide.StartsWith(value: "Project(\"") && leftSide.EndsWith(value: "\")"))
            file.TypeGUID = MiddleOfString(source: leftSide, fromLeft: "Project(\"".Length, fromRight: "\")".Length);
        var opts = rightSide.Split(separator: ',').Select(selector: s => s.Trim()).ToList();
        //strip quotes
        if (opts.Any()) file.Name = MiddleOfString(source: opts[index: 0], fromLeft: 1, fromRight: 1);
        if (opts.Count() >= 2) file.Path = MiddleOfString(source: opts[index: 1], fromLeft: 1, fromRight: 1);
        if (opts.Count() >= 3) file.GUID = MiddleOfString(source: opts[index: 2], fromLeft: 1, fromRight: 1);

        return file;
    }

    private static string MiddleOfString(string source, int fromLeft, int fromRight)
    {
        var left = source.Substring(startIndex: fromLeft);
        return left.Substring(startIndex: 0, length: left.Length - fromRight);
    }
}

/// <summary>
/// Scan the projects of an input Solution file (a .sln file)
/// </summary>
internal class SolutionFileScanner : IScanner
{
    public const string DatasourceId = "dotnet-solution-file";
    public NugetApi NugetService;

    public SolutionScannerOptions Options;

    public SolutionFileScanner(SolutionScannerOptions options, NugetApi nugetService)
    {
        Options = options;
        NugetService = nugetService;
        if (Options == null) throw new Exception(message: "Must provide a valid options object.");

        if (string.IsNullOrWhiteSpace(value: Options.OutputFilePath))
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            Options.OutputFilePath = Path.Combine(path1: currentDirectory, path2: "nuget-inpector-results.json").Replace(oldValue: "\\", newValue: "/");
        }

        if (string.IsNullOrWhiteSpace(value: Options.SolutionName))
            Options.SolutionName = Path.GetFileNameWithoutExtension(path: Options.ProjectFilePath);
    }

    public Scan RunScan()
    {
        try
        {
            var package = GetPackage();
            var packages = new List<Package> { };
            if (package != null)
            {
                packages.Add(item: package);
            }

            return new Scan
            {
                Status = Scan.ResultStatus.Success,
                ResultName = Options.SolutionName,
                OutputFilePath = Options.OutputFilePath,
                Packages = packages
            };
        }
        catch (Exception ex)
        {
            if (Config.TRACE) Console.WriteLine(format: "{0}", arg0: ex);
            return new Scan
            {
                Status = Scan.ResultStatus.Error,
                Exception = ex
            };
        }
    }

    public Package? GetPackage()
    {
        if (Config.TRACE) Console.WriteLine(value: $"Processing Solution: {Options.ProjectFilePath}");
        var stopwatch = Stopwatch.StartNew();
        var solution = new Package
        {
            Name = Options.SolutionName,
            SourcePath = Options.ProjectFilePath,
            Type = "solution-file",
            DatasourceId = DatasourceId
        };
        try
        {
            var projectFiles = FindProjectFilesFromSolutionFile(solutionPath: Options.ProjectFilePath);
            if (Config.TRACE) Console.WriteLine(value: "Parsed Solution File");
            if (projectFiles.Count > 0)
            {
                var solutionDirectory = Path.GetDirectoryName(path: Options.ProjectFilePath);
                if (Config.TRACE) Console.WriteLine(format: "Solution directory: {0}", arg0: solutionDirectory);

                var duplicateNames = projectFiles
                    .GroupBy(keySelector: project => project.Name)
                    .Where(predicate: group => group.Count() > 1)
                    .Select(selector: group => group.Key);

                foreach (var project in projectFiles)
                    try
                    {
                        var projectRelativePath = project.Path;
                        var projectPath = Path
                            .Combine(path1: solutionDirectory ?? string.Empty, path2: projectRelativePath ?? string.Empty)
                            .Replace(oldValue: "\\", newValue: "/");
                        var projectName = project.Name;
                        var projectId = projectName;
                        if (duplicateNames.Contains(value: projectId))
                        {
                            if (Config.TRACE)
                                Console.WriteLine(value: $"Duplicate project name '{projectId}' found. Using GUID instead.");
                            projectId = project.GUID;
                        }

                        bool projectFileExists;
                        try
                        {
                            projectFileExists = File.Exists(path: projectPath);
                        }
                        catch (Exception)
                        {
                            // TODO: this should reported in the scan output
                            if (Config.TRACE) Console.WriteLine(value: $"Skipping missing project file: {projectPath}");
                            continue;
                        }

                        if (!projectFileExists)
                        {
                            if (Config.TRACE) Console.WriteLine(value: $"Skipping non-existent project path: {projectPath}");
                            continue;
                        }

                        var projectScanner = new ProjectFileScanner(options: new ProjectScannerOptions(old: Options)
                        {
                            ProjectName = projectName,
                            ProjectUniqueId = projectId,
                            ProjectFilePath = projectPath
                        }, nugetApiService: NugetService);

                        var scan = projectScanner.RunScan();
                        if (scan.Packages != null)
                            solution.Children.AddRange(collection: scan.Packages);
                    }
                    catch (Exception ex)
                    {
                        if (Config.TRACE) Console.WriteLine(value: ex.ToString());
                        throw;
                    }
            }
            else
            {
                if (Config.TRACE) Console.WriteLine(format: "No project data found for solution {0}", arg0: Options.ProjectFilePath);
            }
        }
        catch (Exception ex)
        {
            if (Config.TRACE) Console.WriteLine(value: ex.ToString());
            throw;
        }

        if (solution.Children.Any())
            if (Config.TRACE)
                Console.WriteLine(value: $"Found {solution.Children.Count} children.");
        if (Config.TRACE) Console.WriteLine(value: $"Finished processing solution: {Options.ProjectFilePath}");
        if (Config.TRACE) Console.WriteLine(value: $"Took {stopwatch.ElapsedMilliseconds} ms to process.");
        return solution;
    }

    private List<SolutionProjectReference> FindProjectFilesFromSolutionFile(string solutionPath)
    {
        var projects = new List<SolutionProjectReference>();
        // Visual Studio right now is not resolving the Microsoft.Build.Construction.SolutionFile type
        // parsing the solution file manually for now.
        if (File.Exists(path: solutionPath))
        {
            var contents = new List<string>(collection: File.ReadAllLines(path: solutionPath));
            var projectLines = contents.FindAll(match: text => text.StartsWith(value: "Project("));
            foreach (var projectText in projectLines)
            {
                var file = SolutionProjectReference.Parse(projectLine: projectText);
                if (file != null) projects.Add(item: file);
            }

            if (Config.TRACE)
                Console.WriteLine(format: "Nuget Inspector found {0} project elements, processed {1} project elements for data",
                    arg0: projectLines.Count(), arg1: projects.Count());
        }
        else
        {
            throw new Exception(message: $"Solution File {solutionPath} not found");
        }

        return projects;
    }
}