using NuGet.Frameworks;

namespace NugetInspector;

public class ScanResult
{
    public enum ResultStatus
    {
        Success,
        Error,
        Warning,
    }

    public Exception? Exception;
    public ProjectScannerOptions? Options;
    public BasePackage project_package = new();
    public ResultStatus Status;

    public List<string> warnings = new();
    public List<string> errors = new();

    public void Sort()
    {
        project_package.Sort();
    }
}

/// <summary>
/// An Options subclass that track project scan-specific options.
/// </summary>
public class ProjectScannerOptions : Options
{
    public string? ProjectName { get; set; }
    public string? ProjectVersion { get; set; }
    public string ProjectDirectory { get; set; } = "";
    public string? PackagesConfigPath { get; set; }
    public string? ProjectJsonPath { get; set; }
    public string? ProjectJsonLockPath { get; set; }
    public string? ProjectAssetsJsonPath { get; set; }
    public string? ProjectFramework { get; set; } = "";

    public ProjectScannerOptions(Options options)
    {
        ProjectFilePath = options.ProjectFilePath;
        TargetFramework = options.TargetFramework;
        ProjectDirectory = Directory.GetParent(path: options.ProjectFilePath)?.FullName ?? string.Empty;
        Verbose = options.Verbose;
        NugetConfigPath = options.NugetConfigPath;
        OutputFilePath = options.OutputFilePath;
    }
}

internal class ProjectScanner
{
    public ProjectScannerOptions ScannerOptions;
    public NugetApi NugetApiService;
    public NuGetFramework project_framework;

    /// <summary>
    /// A Scanner for project "*proj" project file input such as .csproj file
    /// </summary>
    /// <param name="options"></param>
    /// <param name="nuget_api_service"></param>
    /// <exception cref="Exception"></exception>
    public ProjectScanner(
        ProjectScannerOptions options,
        NugetApi nuget_api_service,
        NuGetFramework project_framework)
    {
        static string combine_paths(string? project_directory, string file_name)
        {
            return Path
                .Combine(path1: project_directory ?? string.Empty, path2: file_name)
                .Replace(oldValue: "\\", newValue: "/");
        }

        this.ScannerOptions = options;
        this.NugetApiService = nuget_api_service;
        this.project_framework = project_framework;

        if (string.IsNullOrWhiteSpace(value: ScannerOptions.OutputFilePath))
        {
            throw new Exception(message: "Missing required output JSON file path.");
        }

        if (string.IsNullOrWhiteSpace(value: ScannerOptions.ProjectDirectory))
            ScannerOptions.ProjectDirectory = Directory.GetParent(path: ScannerOptions.ProjectFilePath)?.FullName ?? string.Empty;

        string project_directory = ScannerOptions.ProjectDirectory;

        // TODO: Also rarer files named packahes.<project name>.congig
        // See CommandLineUtility.IsValidConfigFileName(Path.GetFileName(path) 
        if (string.IsNullOrWhiteSpace(value: ScannerOptions.PackagesConfigPath))
            ScannerOptions.PackagesConfigPath = combine_paths(project_directory, "packages.config");

        if (string.IsNullOrWhiteSpace(value: ScannerOptions.ProjectAssetsJsonPath))
            ScannerOptions.ProjectAssetsJsonPath = combine_paths(project_directory, "obj/project.assets.json");

        if (string.IsNullOrWhiteSpace(value: ScannerOptions.ProjectJsonPath))
            ScannerOptions.ProjectJsonPath = combine_paths(project_directory, "project.json");

        if (string.IsNullOrWhiteSpace(value: ScannerOptions.ProjectJsonLockPath))
            ScannerOptions.ProjectJsonLockPath = combine_paths(project_directory, "project.lock.json");

        if (string.IsNullOrWhiteSpace(value: ScannerOptions.ProjectName))
        {
            ScannerOptions.ProjectName = Path.GetFileNameWithoutExtension(path: ScannerOptions.ProjectFilePath);
            if (Config.TRACE)
                Console.WriteLine($"\nProjectScanner: Using filename as project name: {ScannerOptions.ProjectName}");
        }

        if (string.IsNullOrWhiteSpace(value: ScannerOptions.ProjectVersion))
        {
            ScannerOptions.ProjectVersion = AssemblyInfoParser.GetProjectAssemblyVersion(project_directory);
            if (Config.TRACE)
            {
                if (!string.IsNullOrWhiteSpace(ScannerOptions.ProjectVersion))
                    Console.WriteLine($"      Using AssemblyInfoParser for project version: {ScannerOptions.ProjectVersion}");
                else
                    Console.WriteLine("      No project version found");
            }
        }
    }

    /// <summary>
    /// Enhance the dependencies recursively in scan results with metadata
    /// fetched from the NuGet API.
    /// </summary>
    /// <param name="scan_result"></param>
    public void FetchDependenciesMetadata(ScanResult scan_result, bool with_details = false)
    {
        if (Config.TRACE_META)
            Console.WriteLine($"\nFetchDependenciesMetadata: with_details: {with_details}");

        foreach (BasePackage dep in scan_result.project_package.dependencies)
        {
            dep.Update(nugetApi: NugetApiService, with_details: with_details);

            if (Config.TRACE_META)
                Console.WriteLine($"    Fetched for {dep.name}@{dep.version}");
        }
    }

    /// <summary>
    /// Scan the project proper and return ScanResult for this project.
    /// </summary>
    /// <returns></returns>
    public ScanResult RunScan()
    {
        if (Config.TRACE)
            Console.WriteLine($"\nRunning scan of: {ScannerOptions.ProjectFilePath} with fallback: {ScannerOptions.WithFallback}");

        var project = new BasePackage(
            name: ScannerOptions.ProjectName!,
            version: ScannerOptions.ProjectVersion,
            datafile_path: ScannerOptions.ProjectFilePath
        );

        var scan_result = new ScanResult() {
            Options = ScannerOptions,
            project_package = project
        };

        /*
         * Try each data file in sequence to resolve packages for a project:
         * 1. start with modern lockfiles such as project-assets.json and older projects.json.lock
         * 2. Then semi-legacy package.config package references
         * 3. Then legacy formats such as projects.json
         * 4. Then modern package references or semi-modern references using MSbuild
         * 4. Then package references as bare XML
         */

        DependencyResolution resolution;
        IDependencyProcessor resolver;

        // project.assets.json is the gold standard when available
        // TODO: make the use of lockfiles optional
        if (FileExists(path: ScannerOptions.ProjectAssetsJsonPath!))
        {
            if (Config.TRACE)
                Console.WriteLine($"  Using project-assets.json lockfile at: {ScannerOptions.ProjectAssetsJsonPath}");
            try
            {
                resolver = new ProjectAssetsJsonProcessor(projectAssetsJsonPath: ScannerOptions.ProjectAssetsJsonPath!);
                resolution = resolver.Resolve();
                project.datasource_id = ProjectAssetsJsonProcessor.DatasourceId;
                project.dependencies = resolution.Dependencies;
                if (Config.TRACE)
                {
                    Console.WriteLine($"    Found #{project.dependencies.Count} dependencies with data_source_id: {project.datasource_id}");
                    Console.WriteLine($"    Project resolved: {ScannerOptions.ProjectName}");
                }
                return scan_result;
            }
            catch (Exception ex)
            {
                string message = $"    Failed to process project-assets.json lockfile: {ScannerOptions.ProjectAssetsJsonPath} with: {ex}";
                scan_result.warnings.Add(message);
                if (Config.TRACE) Console.WriteLine($"    {message}");
            }
        }

        // projects.json.lock is legacy but should be used if present
        if (FileExists(path: ScannerOptions.ProjectJsonLockPath!))
        {
            if (Config.TRACE)
                Console.WriteLine($"  Using projects.json.lock lockfile: {ScannerOptions.ProjectJsonLockPath}");
            try
            {
                resolver = new ProjectLockJsonProcessor(projectLockJsonPath: ScannerOptions.ProjectJsonLockPath!);
                resolution = resolver.Resolve();
                project.datasource_id = ProjectLockJsonProcessor.DatasourceId;
                project.dependencies = resolution.Dependencies;
                if (Config.TRACE)
                {
                    Console.WriteLine($"    Found #{project.dependencies.Count} dependencies with data_source_id: {project.datasource_id}");
                    Console.WriteLine($"    Project resolved: {ScannerOptions.ProjectName}");
                }
                return scan_result;
            }
            catch (Exception ex)
            {
                string message = $"    Failed to process projects.json.lock lockfile: {ScannerOptions.ProjectJsonLockPath} with: {ex}";
                scan_result.warnings.Add(message);
                if (Config.TRACE) Console.WriteLine($"    {message}");
            }
        }

        // packages.config is semi-legacy but should be used if present over a project file
        if (FileExists(path: ScannerOptions.PackagesConfigPath!))
        {
            if (Config.TRACE)
                Console.WriteLine($"  Using packages.config references: {ScannerOptions.PackagesConfigPath}");
            try
            {
                resolver = new PackagesConfigProcessor(
                    packages_config_path: ScannerOptions.PackagesConfigPath!,
                    nuget_api: NugetApiService,
                    project_framework: project_framework);
                resolution = resolver.Resolve();
                project.datasource_id = PackagesConfigProcessor.DatasourceId;
                project.dependencies = resolution.Dependencies;
                if (Config.TRACE)
                {
                    Console.WriteLine($"    Found #{project.dependencies.Count} dependencies with data_source_id: {project.datasource_id}");
                    Console.WriteLine($"    Project resolved: {ScannerOptions.ProjectName}");
                }
                return scan_result;
            }
            catch (Exception ex)
            {
                string message = $"Failed to process packages.config references: {ScannerOptions.PackagesConfigPath} with: {ex}";
                scan_result.errors.Add(message);
                if (Config.TRACE) Console.WriteLine($"    {message}");
            }
        }

        // project.json is legacy but should be used if present
        if (FileExists(path: ScannerOptions.ProjectJsonPath!))
        {
            if (Config.TRACE) Console.WriteLine($"  Using legacy project.json lockfile: {ScannerOptions.ProjectJsonPath}");
            try
            {
                resolver = new ProjectJsonProcessor(
                    projectName: ScannerOptions.ProjectName,
                    projectJsonPath: ScannerOptions.ProjectJsonPath!);
                resolution = resolver.Resolve();
                project.datasource_id = ProjectJsonProcessor.DatasourceId;
                project.dependencies = resolution.Dependencies;
                if (Config.TRACE)
                {
                    Console.WriteLine($"    Found #{project.dependencies.Count} dependencies with data_source_id: {project.datasource_id}");
                    Console.WriteLine($"    Project resolved: {ScannerOptions.ProjectName}");
                }
                return scan_result;
            }
            catch (Exception ex)
            {
                string message = $"Failed to process project.json lockfile: {ScannerOptions.ProjectJsonPath} with: {ex}";
                scan_result.warnings.Add(message);
                if (Config.TRACE) Console.WriteLine($"    {message}");
            }
        }

        // In the most common case we use the *proj file and its PackageReference

        // first we try using MSbuild to read the project
        if (Config.TRACE)
            Console.WriteLine($"  Using project file: {ScannerOptions.ProjectFilePath}");

        try
        {
            resolver = new ProjectFileProcessor(
                projectPath: ScannerOptions.ProjectFilePath,
                nugetApi: NugetApiService,
                project_framework: project_framework);

            resolution = resolver.Resolve();

            if (resolution.Success)
            {
                project.datasource_id = ProjectFileProcessor.DatasourceId;
                project.dependencies = resolution.Dependencies;
                scan_result.Status = ScanResult.ResultStatus.Success;
                if (Config.TRACE)
                {
                    Console.WriteLine($"    Found #{project.dependencies.Count} dependencies with data_source_id: {project.datasource_id}");
                    Console.WriteLine($"    Project resolved: {ScannerOptions.ProjectName}");
                }
                return scan_result;
            }
        }
        catch (Exception ex)
        {
            string message = $"Failed to process project file: {ScannerOptions.ProjectFilePath} with:\n{ex}";
            scan_result.errors.Add(message);
            scan_result.Status = ScanResult.ResultStatus.Error;
            if (Config.TRACE) Console.WriteLine($"\nERROR: {message}\n");
        }

        if (!ScannerOptions.WithFallback)
            return scan_result;

        // In the case of older proj file we process the bare XML as a last resort option
        // bare XML is a fallback considered as an error even if returns something
        if (Config.TRACE)
            Console.WriteLine($"  Using fallback processor of project file as bare XML: {ScannerOptions.ProjectFilePath}");

        try
        {
            resolver = new ProjectXmlFileProcessor(
            projectPath: ScannerOptions.ProjectFilePath,
            nugetApi: NugetApiService,
            project_framework: project_framework);

            resolution = resolver.Resolve();

            project.datasource_id = ProjectXmlFileProcessor.DatasourceId;
            project.dependencies = resolution.Dependencies;

            if (resolution.Success)
            {
                project.datasource_id = ProjectFileProcessor.DatasourceId;
                project.dependencies = resolution.Dependencies;
                if (Config.TRACE)
                {
                    Console.WriteLine($"    Found #{project.dependencies.Count} dependencies with data_source_id: {project.datasource_id}");
                    Console.WriteLine($"    Project resolved: {ScannerOptions.ProjectName}");
                }
                // even success here is a failure as we could not get the full power of a project resolution
                scan_result.Status = ScanResult.ResultStatus.Success;
                return scan_result;
            }
        }
        catch (Exception ex)
        {
            string message = $"Failed to process *.*proj project file as bare XML: {ScannerOptions.ProjectFilePath} with:\n{ex}";
            scan_result.errors.Add(message);
            scan_result.Status = ScanResult.ResultStatus.Error;
            if (Config.TRACE) Console.WriteLine($"\nERROR: {message}\n");
        }

        return scan_result;
    }

    /// <summary>
    /// Return true if the "path" strings is an existing file.
    /// </summary>
    /// <param name="path"></param>
    /// <returns>bool</returns>
    private static bool FileExists(string path)
    {
        return !string.IsNullOrWhiteSpace(value: path) && File.Exists(path: path);
    }
}
