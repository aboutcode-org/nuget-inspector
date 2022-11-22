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
        var equalSplit = projectLine.Split('=').Select(s => s.Trim()).ToList();
        if (equalSplit.Count() < 2) return null;

        var file = new SolutionProjectReference();
        var leftSide = equalSplit[0];
        var rightSide = equalSplit[1];
        if (leftSide.StartsWith("Project(\"") && leftSide.EndsWith("\")"))
            file.TypeGUID = MiddleOfString(leftSide, "Project(\"".Length, "\")".Length);
        var opts = rightSide.Split(',').Select(s => s.Trim()).ToList();
        //strip quotes
        if (opts.Any()) file.Name = MiddleOfString(opts[0], 1, 1);
        if (opts.Count() >= 2) file.Path = MiddleOfString(opts[1], 1, 1);
        if (opts.Count() >= 3) file.GUID = MiddleOfString(opts[2], 1, 1);

        return file;
    }

    private static string MiddleOfString(string source, int fromLeft, int fromRight)
    {
        var left = source.Substring(fromLeft);
        return left.Substring(0, left.Length - fromRight);
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
        if (Options == null) throw new Exception("Must provide a valid options object.");

        if (string.IsNullOrWhiteSpace(Options.OutputFilePath))
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            Options.OutputFilePath = Path.Combine(currentDirectory, "nuget-inpector-results.json").Replace("\\", "/");
        }

        if (string.IsNullOrWhiteSpace(Options.SolutionName))
            Options.SolutionName = Path.GetFileNameWithoutExtension(Options.ProjectFilePath);
    }

    public Scan RunScan()
    {
        try
        {
            var package = GetPackage();
            var packages = new List<Package> { };
            if (package != null)
            {
                packages.Add(package);
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
            if (Config.TRACE) Console.WriteLine("{0}", ex);
            return new Scan
            {
                Status = Scan.ResultStatus.Error,
                Exception = ex
            };
        }
    }

    public Package? GetPackage()
    {
        if (Config.TRACE) Console.WriteLine($"Processing Solution: {Options.ProjectFilePath}");
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
            var projectFiles = FindProjectFilesFromSolutionFile(Options.ProjectFilePath);
            if (Config.TRACE) Console.WriteLine("Parsed Solution File");
            if (projectFiles.Count > 0)
            {
                var solutionDirectory = Path.GetDirectoryName(Options.ProjectFilePath);
                if (Config.TRACE) Console.WriteLine("Solution directory: {0}", solutionDirectory);

                var duplicateNames = projectFiles
                    .GroupBy(project => project.Name)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key);

                foreach (var project in projectFiles)
                    try
                    {
                        var projectRelativePath = project.Path;
                        var projectPath = Path
                            .Combine(solutionDirectory ?? string.Empty, projectRelativePath ?? string.Empty)
                            .Replace("\\", "/");
                        var projectName = project.Name;
                        var projectId = projectName;
                        if (duplicateNames.Contains(projectId))
                        {
                            if (Config.TRACE)
                                Console.WriteLine($"Duplicate project name '{projectId}' found. Using GUID instead.");
                            projectId = project.GUID;
                        }

                        bool projectFileExists;
                        try
                        {
                            projectFileExists = File.Exists(projectPath);
                        }
                        catch (Exception)
                        {
                            // TODO: this should reported in the scan output
                            if (Config.TRACE) Console.WriteLine($"Skipping missing project file: {projectPath}");
                            continue;
                        }

                        if (!projectFileExists)
                        {
                            if (Config.TRACE) Console.WriteLine($"Skipping non-existent project path: {projectPath}");
                            continue;
                        }

                        var projectScanner = new ProjectFileScanner(new ProjectScannerOptions(Options)
                        {
                            ProjectName = projectName,
                            ProjectUniqueId = projectId,
                            ProjectFilePath = projectPath
                        }, NugetService);

                        var scan = projectScanner.RunScan();
                        if (scan.Packages != null)
                            solution.Children.AddRange(scan.Packages);
                    }
                    catch (Exception ex)
                    {
                        if (Config.TRACE) Console.WriteLine(ex.ToString());
                        throw;
                    }
            }
            else
            {
                if (Config.TRACE) Console.WriteLine("No project data found for solution {0}", Options.ProjectFilePath);
            }
        }
        catch (Exception ex)
        {
            if (Config.TRACE) Console.WriteLine(ex.ToString());
            throw;
        }

        if (solution.Children.Any())
            if (Config.TRACE)
                Console.WriteLine("Found " + solution.Children.Count + " children.");
        if (Config.TRACE) Console.WriteLine("Finished processing solution: " + Options.ProjectFilePath);
        if (Config.TRACE) Console.WriteLine("Took " + stopwatch.ElapsedMilliseconds + " ms to process.");
        return solution;
    }

    private List<SolutionProjectReference> FindProjectFilesFromSolutionFile(string solutionPath)
    {
        var projects = new List<SolutionProjectReference>();
        // Visual Studio right now is not resolving the Microsoft.Build.Construction.SolutionFile type
        // parsing the solution file manually for now.
        if (File.Exists(solutionPath))
        {
            var contents = new List<string>(File.ReadAllLines(solutionPath));
            var projectLines = contents.FindAll(text => text.StartsWith("Project("));
            foreach (var projectText in projectLines)
            {
                var file = SolutionProjectReference.Parse(projectText);
                if (file != null) projects.Add(file);
            }

            if (Config.TRACE)
                Console.WriteLine("Nuget Inspector found {0} project elements, processed {1} project elements for data",
                    projectLines.Count(), projects.Count());
        }
        else
        {
            throw new Exception("Solution File " + solutionPath + " not found");
        }

        return projects;
    }
}