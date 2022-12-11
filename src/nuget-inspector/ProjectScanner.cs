using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;

namespace NugetInspector;

public class ScanResult
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

internal class ProjectScannerOptions : Options
{
    public ProjectScannerOptions(Options options)
    {
        ProjectFilePath = options.ProjectFilePath;
        ProjectDirectory = Directory.GetParent(path: ProjectFilePath)?.FullName ?? string.Empty;
        Verbose = options.Verbose;
        NugetApiFeedUrl = options.NugetApiFeedUrl;
        NugetConfigPath = options.NugetConfigPath;
        OutputFilePath = options.OutputFilePath;
    }

    public string? ProjectName { get; set; }
    public string? ProjectUniqueId { get; set; }
    public string? ProjectDirectory { get; set; }
    public string? ProjectVersion { get; set; }
    public string? PackagesConfigPath { get; set; }
    public string? ProjectJsonPath { get; set; }
    public string? ProjectJsonLockPath { get; set; }
    public string? ProjectAssetsJsonPath { get; set; }
}

internal class ProjectScanner
{
    public NugetApi NugetApiService;
    public ProjectScannerOptions Options;

    /// <summary>
    /// A Scanner for project "*proj" project file input such as .csproj file
    /// </summary>
    /// <param name="options"></param>
    /// <param name="nuget_api_service"></param>
    /// <exception cref="Exception"></exception>
    public ProjectScanner(ProjectScannerOptions options, NugetApi nuget_api_service)
    {
        string combine_paths(string? project_directory, string file_name)
        {
            return Path
                .Combine(path1: project_directory ?? string.Empty, path2: file_name)
                .Replace(oldValue: "\\", newValue: "/");
        }

        Options = options;
        NugetApiService = nuget_api_service;

        if (string.IsNullOrWhiteSpace(value: Options.OutputFilePath))
        {
            throw new Exception(message: "Missing required output JSON file path.");
        }

        if (string.IsNullOrWhiteSpace(value: Options.ProjectDirectory))
            Options.ProjectDirectory = Directory.GetParent(path: Options.ProjectFilePath)?.FullName ?? string.Empty;

        string project_directory = Options.ProjectDirectory;

        if (string.IsNullOrWhiteSpace(value: Options.PackagesConfigPath))
            Options.PackagesConfigPath = combine_paths(project_directory, "packages.config");

        if (string.IsNullOrWhiteSpace(value: Options.ProjectAssetsJsonPath))
            Options.ProjectAssetsJsonPath = combine_paths(project_directory, "obj/project.assets.json");

        if (string.IsNullOrWhiteSpace(value: Options.ProjectJsonPath))
            Options.ProjectJsonPath = combine_paths(project_directory, "project.json");

        if (string.IsNullOrWhiteSpace(value: Options.ProjectJsonLockPath))
            Options.ProjectJsonLockPath = combine_paths(project_directory, "project.lock.json");

        if (string.IsNullOrWhiteSpace(value: Options.ProjectName))
            Options.ProjectName = Path.GetFileNameWithoutExtension(path: Options.ProjectFilePath);

        if (string.IsNullOrWhiteSpace(value: Options.ProjectUniqueId))
            Options.ProjectUniqueId = Path.GetFileNameWithoutExtension(path: Options.ProjectFilePath);

        if (string.IsNullOrWhiteSpace(value: Options.ProjectVersion))
            Options.ProjectVersion = AssemblyInfoParser.GetProjectAssemblyVersion(project_directory);
    }

    public ScanResult RunScan()
    {
        try
        {
            var package = GetPackage();
            List<Package> packages = new List<Package>();
            if (package != null)
            {
                packages.Add(item: package);
            }

            return new ScanResult
            {
                Status = ScanResult.ResultStatus.Success,
                ResultName = Options.ProjectUniqueId,
                OutputFilePath = Options.OutputFilePath,
                Packages = packages
            };
        }
        catch (Exception ex)
        {
            if (Config.TRACE) Console.WriteLine($"{ex}");
            return new ScanResult
            {
                Status = ScanResult.ResultStatus.Error,
                Exception = ex
            };
        }
    }

    public Package? GetPackage()
    {
        var stopWatch = Stopwatch.StartNew();
        if (Config.TRACE)
        {
            Console.WriteLine(
                value:
                $"Processing Project: {Options.ProjectName} using Project Directory: {Options.ProjectDirectory}");
        }

        var package = new Package
        {
            Name = Options.ProjectUniqueId,
            Version = Options.ProjectVersion,
            SourcePath = Options.ProjectFilePath,
        };

        var projectTargetFramework = ParseTargetFramework();
        try
        {
            package.OutputPaths = FindOutputPaths();
        }
        catch (Exception)
        {
            if (Config.TRACE) Console.WriteLine("Unable to determine output paths for this project.");
        }

        bool hasPackagesConfig = FileExists(path: Options.PackagesConfigPath!);
        bool hasProjectJson = FileExists(path: Options.ProjectJsonPath!);
        bool hasProjectJsonLock = FileExists(path: Options.ProjectJsonLockPath!);
        bool hasProjectAssetsJson = FileExists(path: Options.ProjectAssetsJsonPath!);

        /*
         * Try each data file in sequence to resolve packages for a project:
         * 1. start with lockfiles
         * 2. Then package.config package references
         * 3. Then legacy formats
         * 4. Then modern package references
         */
        if (hasProjectAssetsJson)
        {
            // project.assets.json is the gold standard when available
            if (Config.TRACE)
                Console.WriteLine($"Using project.assets.json file: {Options.ProjectAssetsJsonPath}");
            var projectAssetsJsonResolver =
                new ProjectAssetsJsonHandler(projectAssetsJsonPath: Options.ProjectAssetsJsonPath!);
            var projectAssetsJsonResult = projectAssetsJsonResolver.Process();
            package.Packages = projectAssetsJsonResult.Packages;
            package.Dependencies = projectAssetsJsonResult.Dependencies;
            package.DatasourceId = ProjectAssetsJsonHandler.DatasourceId;
        }
        else if (hasProjectJsonLock)
        {
            // projects.json.lock is legacy but should be used if present
            if (Config.TRACE)
                Console.WriteLine($"Using legacy projects.json.lock: {Options.ProjectJsonLockPath}");
            var projectJsonLockResolver =
                new LegacyProjectLockJsonHandler(projectLockJsonPath: Options.ProjectJsonLockPath!);
            var projectJsonLockResult = projectJsonLockResolver.Process();
            package.Packages = projectJsonLockResult.Packages;
            package.Dependencies = projectJsonLockResult.Dependencies;
            package.DatasourceId = LegacyProjectLockJsonHandler.DatasourceId;
        }
        else if (hasPackagesConfig)
        {
            // packages.config is legacy but should be used if present
            if (Config.TRACE) Console.WriteLine($"Using packages.config: {Options.PackagesConfigPath}");
            var packagesConfigResolver = new PackagesConfigHandler(
                packages_config_path: Options.PackagesConfigPath!, nuget_api: NugetApiService);
            var packagesConfigResult = packagesConfigResolver.Process();
            package.Packages = packagesConfigResult.Packages;
            package.Dependencies = packagesConfigResult.Dependencies;
            package.DatasourceId = PackagesConfigHandler.DatasourceId;
        }
        else if (hasProjectJson)
        {
            // project.json is legacy but should be used if present
            if (Config.TRACE) Console.WriteLine($"Using legacy project.json: {Options.ProjectJsonPath}");
            var projectJsonResolver = new LegacyProjectJsonHandler(projectName: Options.ProjectName,
                projectJsonPath: Options.ProjectJsonPath!);
            var projectJsonResult = projectJsonResolver.Process();
            package.Packages = projectJsonResult.Packages;
            package.Dependencies = projectJsonResult.Dependencies;
            package.DatasourceId = LegacyProjectJsonHandler.DatasourceId;
        }
        else
        {
            // In the most common case we use the *proj file and its PackageReference
            if (Config.TRACE)
                Console.WriteLine($"Attempting package-reference resolver: {Options.ProjectFilePath}");
            var pkgRefResolver = new ProjFileStandardPackageReferenceHandler(
                projectPath: Options.ProjectFilePath,
                nugetApi: NugetApiService,
                projectTargetFramework: projectTargetFramework);

            var projectReferencesResult = pkgRefResolver.Process();

            if (projectReferencesResult.Success)
            {
                if (Config.TRACE) Console.WriteLine("ProjFileStandardPackageReferenceHandler success.");
                package.Packages = projectReferencesResult.Packages;
                package.Dependencies = projectReferencesResult.Dependencies;
                package.DatasourceId = ProjFileStandardPackageReferenceHandler.DatasourceId;
            }
            else
            {
                if (Config.TRACE) Console.WriteLine("Using Fallback XML project file reader and resolver.");
                var xmlResolver =
                    new ProjFileXmlParserPackageReferenceHandler(projectPath: Options.ProjectFilePath,
                        nugetApi: NugetApiService, projectTargetFramework: projectTargetFramework);
                var xmlResult = xmlResolver.Process();
                package.Version = xmlResult.ProjectVersion;
                package.Packages = xmlResult.Packages;
                package.Dependencies = xmlResult.Dependencies;
                package.DatasourceId = ProjFileXmlParserPackageReferenceHandler.DatasourceId;
            }
        }

        if (Config.TRACE)
        {
            Console.WriteLine(
                value: $"Found #{package.Dependencies.Count} dependencies for #{package.Packages.Count} packages.");
            Console.WriteLine($"Project resolved: {Options.ProjectName} in {stopWatch.ElapsedMilliseconds} ms.");
        }

        return package;
    }

    /// <summary>
    /// Return true if the "path" strings is an existing file.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private static bool FileExists(string path)
    {
        return !string.IsNullOrWhiteSpace(value: path) && File.Exists(path: path);
    }

    private NuGetFramework? ParseTargetFramework()
    {
        var targetFramework = ExtractTargetFramework(projectFilePath: Options.ProjectFilePath);
        var projectTargetFramework = NuGetFramework.ParseFolder(folderName: targetFramework);
        return projectTargetFramework;
    }

    /// <summary>
    /// Return the first TargetFramework fourn in the *.*proj XML file 
    /// </summary>
    /// <param name="projectFilePath"></param>
    /// <returns></returns>
    private static string ExtractTargetFramework(string projectFilePath)
    {
        var targetFrameworkNodeNames = new[]
                { "TargetFrameworkVersion", "TargetFramework", "TargetFrameworks" }
            .Select(selector: x => x.ToLowerInvariant());

        var csProj = XElement.Load(uri: projectFilePath);
        var targetFrameworks = csProj.Descendants()
            .Where(predicate: e => targetFrameworkNodeNames.Contains(value: e.Name.LocalName.ToLowerInvariant()))
            .ToList();

        if (!targetFrameworks.Any())
        {
            if (Config.TRACE)
                Console.WriteLine(
                    value: $"Warning - Target Framework: Could not extract a target framework for {projectFilePath}");
            return string.Empty;
        }

        if (targetFrameworks.Count > 1 && Config.TRACE)
        {
            Console.WriteLine(
                value: $"Warning - Target Framework: Found multiple target frameworks for {projectFilePath}");
        }

        if (Config.TRACE)
            Console.WriteLine(
                value:
                $"Found the following TargetFramework(s): {string.Join(separator: Environment.NewLine, values: targetFrameworks)}");

        return targetFrameworks.First().Value;
    }

    public List<string> FindOutputPaths()
    {
        if (Config.TRACE)
        {
            Console.WriteLine("Attempting to parse configuration output paths.");
            Console.WriteLine($"Project File: {Options.ProjectFilePath}");
        }

        try
        {
            var proj = new Microsoft.Build.Evaluation.Project(projectFile: Options.ProjectFilePath);
            var outputPaths = new List<string>();
            List<string>? configurations;
            proj.ConditionedProperties.TryGetValue(key: "Configuration", value: out configurations);
            if (configurations == null) configurations = new List<string>();
            foreach (var config in configurations)
            {
                proj.SetProperty(name: "Configuration", unevaluatedValue: config);
                proj.ReevaluateIfNecessary();
                var path = proj.GetPropertyValue(name: "OutputPath");
                var fullPath = Path.Combine(path1: proj.DirectoryPath, path2: path)
                    .Replace(oldValue: "\\", newValue: "/");
                outputPaths.Add(item: fullPath);
                if (Config.TRACE) Console.WriteLine($"Found path: {fullPath}");
            }

            ProjectCollection.GlobalProjectCollection.UnloadProject(project: proj);
            if (Config.TRACE) Console.WriteLine($"Found {outputPaths.Count} paths.");
            return outputPaths;
        }
        catch (Exception)
        {
            if (Config.TRACE) Console.WriteLine("Skipping configuration output paths.");
            return new List<string>();
        }
    }
}