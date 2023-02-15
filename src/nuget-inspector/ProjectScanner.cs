using System.Diagnostics;
using System.Xml.Linq;
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
    public ProjectScannerOptions? Options;
    public List<BasePackage> Packages = new();
    public ResultStatus Status;
}

/// <summary>
/// An Options subclass that track project scan-specific options.
/// </summary>
public class ProjectScannerOptions : Options
{
    public ProjectScannerOptions(Options options)
    {
        ProjectFilePath = options.ProjectFilePath;
        TargetFramework = options.TargetFramework;
        ProjectDirectory = Directory.GetParent(path: options.ProjectFilePath)?.FullName ?? string.Empty;
        Verbose = options.Verbose;
        NugetApiFeedUrl = options.NugetApiFeedUrl;
        NugetConfigPath = options.NugetConfigPath;
        OutputFilePath = options.OutputFilePath;
    }

    public string? ProjectName { get; set; }
    public string? ProjectVersion { get; set; }
    public string? ProjectDirectory { get; set; }
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
        static string combine_paths(string? project_directory, string file_name)
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

        if (string.IsNullOrWhiteSpace(value: Options.ProjectVersion))
        {
            Options.ProjectVersion = AssemblyInfoParser.GetProjectAssemblyVersion(project_directory);
            if (Config.TRACE)
                Console.WriteLine($"Using AssemblyInfoParser for project version: {Options.ProjectVersion}");
        }
    }

    /// <summary>
    /// Run the scan proper
    /// </summary>
    /// <returns>ScanResult</returns>
    public ScanResult RunScan()
    {
        try
        {
            var package = ScanProject();
            List<BasePackage> packages = new();
            if (package != null)
            {
                packages.Add(item: package);
            }

            return new ScanResult
            {
                Status = ScanResult.ResultStatus.Success,
                Options = Options,
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

    /// <summary>
    /// Enhance the packages in scan results with metadata fetched from the NuGet API.
    /// </summary>
    /// <param name="scan_result"></param>
    /// <returns></returns>
    public void FetchMetadata(ScanResult scan_result)
    {
        foreach (BasePackage package in scan_result.Packages)
        {
            try
            {
                if (Config.TRACE)
                    Console.WriteLine($"FetchMetadata for '{package.purl}'");

                package.Update(nugetApi: NugetApiService);
            }
            catch (Exception ex)
            {
                if (Config.TRACE)
                    Console.WriteLine($"Failed to fetch NuGet API for package: {package.purl}: {ex}");
            }

            foreach (BasePackage subpack in package.packages)
            {
                try
                {
                    subpack.Update(nugetApi: NugetApiService);
                }
                catch (Exception ex)
                {
                    if (Config.TRACE)
                        Console.WriteLine($"Failed to fetch NuGet API for subpackage: {subpack.purl}: {ex}");
                }

                foreach (BasePackage subdep in subpack.dependencies)
                {
                    try
                    {
                        subdep.Update(nugetApi: NugetApiService);
                    }
                    catch (Exception ex)
                    {
                        if (Config.TRACE)
                            Console.WriteLine($"Failed to fetch NuGet API for subdep: {subdep.purl}: {ex}");
                    }
                }
            }

            foreach (BasePackage dep in package.dependencies)
            {
                try
                {
                    dep.Update(nugetApi: NugetApiService);
                }
                catch (Exception ex)
                {
                    if (Config.TRACE)
                        Console.WriteLine($"Failed to fetch NuGet API for dep: {dep.purl}: {ex}");
                }
            }
        }
    }

    /// <summary>
    /// Scan an return the root BasePackage being scanned for this project.
    /// </summary>
    /// <returns></returns>
    public BasePackage? ScanProject()
    {
        Stopwatch? stopWatch = null;
        if (Config.TRACE)
        {
            stopWatch = Stopwatch.StartNew();
            Console.WriteLine($"Processing Project: {Options.ProjectName} using Directory: {Options.ProjectDirectory}");
        }

        var package = new BasePackage(
            name: Options.ProjectName!,
            version: Options.ProjectVersion,
            datafile_path: Options.ProjectFilePath
        );
        // Force using the provided framework if present
        NuGetFramework? project_target_framework;
        if (!string.IsNullOrWhiteSpace(Options.TargetFramework))
        {
            string option_target_framework = Options.TargetFramework.ToLowerInvariant();
            project_target_framework = NuGetFramework.ParseFolder(folderName: option_target_framework);
        }
        else
        {
            // use the 1st framework found in the project
            project_target_framework = ParseTargetFramework();
        }
        if (Config.TRACE)
        {
            Console.WriteLine($"project_target_framework: {project_target_framework}");
        }

        bool hasPackagesConfig = FileExists(path: Options.PackagesConfigPath!);
        bool hasProjectAssetsJson = FileExists(path: Options.ProjectAssetsJsonPath!);
        // legacy formats
        bool hasProjectJson = FileExists(path: Options.ProjectJsonPath!);
        bool hasProjectJsonLock = FileExists(path: Options.ProjectJsonLockPath!);

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
            var projectAssetsJsonResult = projectAssetsJsonResolver.Resolve();
            package.packages = projectAssetsJsonResult.Packages;
            package.dependencies = projectAssetsJsonResult.Dependencies;
            package.datasource_id = ProjectAssetsJsonHandler.DatasourceId;
        }
        else if (hasProjectJsonLock)
        {
            // projects.json.lock is legacy but should be used if present
            if (Config.TRACE)
                Console.WriteLine($"Using legacy projects.json.lock: {Options.ProjectJsonLockPath}");
            var projectJsonLockResolver =
                new LegacyProjectLockJsonHandler(projectLockJsonPath: Options.ProjectJsonLockPath!);
            var projectJsonLockResult = projectJsonLockResolver.Resolve();
            package.packages = projectJsonLockResult.Packages;
            package.dependencies = projectJsonLockResult.Dependencies;
            package.datasource_id = LegacyProjectLockJsonHandler.DatasourceId;
        }
        else if (hasPackagesConfig)
        {
            // packages.config is legacy but should be used if present
            if (Config.TRACE)
                Console.WriteLine($"Using packages.config: {Options.PackagesConfigPath}");
            var packagesConfigResolver = new PackagesConfigHandler(
                packages_config_path: Options.PackagesConfigPath!,
                nuget_api: NugetApiService,
                project_target_framework: project_target_framework!);
            var packagesConfigResult = packagesConfigResolver.Resolve();
            package.packages = packagesConfigResult.Packages;
            package.dependencies = packagesConfigResult.Dependencies;
            package.datasource_id = PackagesConfigHandler.DatasourceId;
        }
        else if (hasProjectJson)
        {
            // project.json is legacy but should be used if present
            if (Config.TRACE) Console.WriteLine($"Using legacy project.json: {Options.ProjectJsonPath}");
            var projectJsonResolver = new LegacyProjectJsonHandler(
                projectName: Options.ProjectName,
                projectJsonPath: Options.ProjectJsonPath!);
            var projectJsonResult = projectJsonResolver.Resolve();
            package.packages = projectJsonResult.Packages;
            package.dependencies = projectJsonResult.Dependencies;
            package.datasource_id = LegacyProjectJsonHandler.DatasourceId;
        }
        else
        {
            // In the most common case we use the *proj file and its PackageReference
            if (Config.TRACE)
                Console.WriteLine($"Attempting package-reference resolver: {Options.ProjectFilePath}");
            var pkgRefResolver = new ProjFileStandardPackageReferenceHandler(
                projectPath: Options.ProjectFilePath,
                nugetApi: NugetApiService,
                projectTargetFramework: project_target_framework);

            var projectReferencesResult = pkgRefResolver.Resolve();

            if (projectReferencesResult.Success)
            {
                if (Config.TRACE) Console.WriteLine("ProjFileStandardPackageReferenceHandler success.");
                package.packages = projectReferencesResult.Packages;
                package.dependencies = projectReferencesResult.Dependencies;
                package.datasource_id = ProjFileStandardPackageReferenceHandler.DatasourceId;
            }
            else
            {
                if (Config.TRACE) Console.WriteLine("Using Fallback XML project file reader and resolver.");
                var xmlResolver =
                    new ProjFileXmlParserPackageReferenceHandler(projectPath: Options.ProjectFilePath,
                        nugetApi: NugetApiService, project_target_framework: project_target_framework);
                var xmlResult = xmlResolver.Resolve();
                package.version = xmlResult.ProjectVersion;
                package.packages = xmlResult.Packages;
                package.dependencies = xmlResult.Dependencies;
                package.datasource_id = ProjFileXmlParserPackageReferenceHandler.DatasourceId;
            }
        }

        if (Config.TRACE)
        {
            Console.WriteLine(
                $"Found #{package.dependencies.Count} dependencies for #{package.packages.Count} packages.");
            Console.WriteLine($"Project resolved: {Options.ProjectName} in {stopWatch!.ElapsedMilliseconds} ms.");
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

    /// <summary>
    /// Return a NuGetFramework string or null as found in the *.*proj XML file.
    /// Handles new and legacy style target framework references.
    /// </summary>
    private NuGetFramework? ParseTargetFramework()
    {
        try
        {
            var (target_framework, is_legacy) = ExtractTargetFramework(projectFilePath: Options.ProjectFilePath);
            if (!is_legacy)
            {
                return NuGetFramework.ParseFolder(folderName: target_framework);
            }
            else
            {
                var version = Version.Parse(target_framework.Trim('v', 'V'));
                return new NuGetFramework(FrameworkConstants.FrameworkIdentifiers.Net, version);
            }
        }
        catch (Exception)
        {
            if (Config.TRACE) Console.WriteLine("Failed to parse ParseTargetFramework.");
            return null;
        }
    }

    /// <summary>
    /// Return the first TargetFramework string found in the *.*proj XML file
    /// and a legacy flag if the project file is an older style using the
    /// "targetframeworkversion" XML element.
    /// </summary>
    /// <param name="projectFilePath"></param>
    /// <returns></returns>
    private static (string, bool) ExtractTargetFramework(string projectFilePath)
    {
        var csproj = XElement.Load(uri: projectFilePath);

        bool is_legacy = csproj
            .Descendants()
            .Where(predicate: e => string.Equals(e.Name.LocalName, "targetframeworkversion", StringComparison.InvariantCultureIgnoreCase))
            .ToList().Any();

        IEnumerable<string> target_framework_tags = new[] { "targetframeworkversion", "targetframework", "targetframeworks" };
        var targetFrameworks = csproj.Descendants()
            .Where(predicate: e => target_framework_tags.Contains(value: e.Name.LocalName.ToLowerInvariant()))
            .ToList();

        if (!targetFrameworks.Any())
        {
            if (Config.TRACE)
                Console.WriteLine($"Warning - Target Framework: Could not extract a target framework for {projectFilePath}");
            return (string.Empty, false);
        }

        if (Config.TRACE)
        {
            if (targetFrameworks.Count > 1)
                Console.WriteLine($"Warning - Multiple target frameworks for {projectFilePath}");
            Console.WriteLine($"TargetFramework(s): {string.Join(separator: Environment.NewLine, values: targetFrameworks)}");
        }

        return (targetFrameworks[0].Value, is_legacy);
    }
}