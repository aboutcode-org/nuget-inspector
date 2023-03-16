using System.Diagnostics;
using System.Xml;
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
    public string? ProjectFramework { get; set; } = "";
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

        // TODO: Also rarer files named packahes.<project name>.congig
        // See CommandLineUtility.IsValidConfigFileName(Path.GetFileName(path) 
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
        // if (string.IsNullOrWhiteSpace(value: Options.ProjectVersion))
        // {
        //     Options.ProjectVersion = "1.0.0";
        //     if (Config.TRACE)
        //         Console.WriteLine("Fallback to default .Net version of 1.0.0");
        // }
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
    public void FetchMetadata(ScanResult scan_result)
    {
        foreach (BasePackage package in scan_result.Packages)
        {
            try
            {
                if (Config.TRACE)
                    Console.WriteLine($"FetchMetadata for '{package.purl}'");
                if (!string.IsNullOrWhiteSpace(package.purl))
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
    /// Enhance the packages in scan results with metadata fetched from the NuGet API.
    /// </summary>
    /// <param name="scan_result"></param>
    /// <returns></returns>
    public void FetchMetadataNew(ScanResult scan_result)
    {
    if (Config.TRACE_META)
        {
            int c = scan_result.Packages.Count;
            Console.WriteLine($"\nFetchMetadata for {c} package(s)");
        }

    // There is always only one top level package 
    foreach (BasePackage package in scan_result.Packages)
        {
            Update(package);
        }
    }

    private void Update(BasePackage package)
    {
        var all_packages = package.dependencies.Concat(package.packages).ToList();
        foreach (BasePackage pack in all_packages)
        {
            if (Config.TRACE_META)
                Console.WriteLine($"        Fetching for dep purl: '{pack.purl}'");
            if (pack.has_updated_metadata)
                continue;
            try
            {
                pack.Update(nugetApi: NugetApiService);

                // recursively update the whole packages and deps tree
                Update(pack);
            }
            catch (Exception ex)
            {
                if (Config.TRACE_META)
                    Console.WriteLine($"        Failed to fetch NuGet API for dep: {pack.purl}: {ex}");
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

        NuGetFramework? project_target_framework = null;

        // Force using the provided framework if present
        if (!string.IsNullOrWhiteSpace(Options.TargetFramework))
            project_target_framework = NuGetFramework.ParseFolder(folderName: Options.TargetFramework.ToLower());

        // ... Or use the 1st framework found in the project
        if (project_target_framework == null || project_target_framework.Framework == "Unsupported")
            project_target_framework = FindProjectTargetFramework(Options.ProjectFilePath);

        // ... Or fallback to "any" framework meaning really anything flies
        if (project_target_framework == null || project_target_framework.Framework == "Unsupported")
            project_target_framework = NuGetFramework.AnyFramework;

        if (Config.TRACE)
            Console.WriteLine($"  project_target_framework: {project_target_framework.GetShortFolderName()}");

        Options.ProjectFramework = project_target_framework.GetShortFolderName();

        bool hasPackagesConfig = FileExists(path: Options.PackagesConfigPath!);
        bool hasProjectAssetsJson = FileExists(path: Options.ProjectAssetsJsonPath!);
        // legacy lockfile formats
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
            var projectAssetsJsonResolver = new ProjectAssetsJsonProcessor(
                projectAssetsJsonPath: Options.ProjectAssetsJsonPath!);
            var projectAssetsJsonResult = projectAssetsJsonResolver.Resolve();
            package.packages = projectAssetsJsonResult.Packages;
            package.dependencies = projectAssetsJsonResult.Dependencies;
            package.datasource_id = ProjectAssetsJsonProcessor.DatasourceId;
        }
        else if (hasProjectJsonLock)
        {
            // projects.json.lock is legacy but should be used if present
            if (Config.TRACE)
                Console.WriteLine($"Using legacy projects.json.lock: {Options.ProjectJsonLockPath}");
            var projectJsonLockResolver = new ProjectLockJsonProcessor(
                projectLockJsonPath: Options.ProjectJsonLockPath!);
            var projectJsonLockResult = projectJsonLockResolver.Resolve();
            package.packages = projectJsonLockResult.Packages;
            package.dependencies = projectJsonLockResult.Dependencies;
            package.datasource_id = ProjectLockJsonProcessor.DatasourceId;
        }
        else if (hasPackagesConfig)
        {
            // packages.config is legacy but should be used if present
            if (Config.TRACE)
                Console.WriteLine($"Using packages.config: {Options.PackagesConfigPath}");
            var packagesConfigResolver = new PackagesConfigProcessor(
                packages_config_path: Options.PackagesConfigPath!,
                nuget_api: NugetApiService,
                project_target_framework: project_target_framework!);
            var packagesConfigResult = packagesConfigResolver.Resolve();
            package.packages = packagesConfigResult.Packages;
            package.dependencies = packagesConfigResult.Dependencies;
            package.datasource_id = PackagesConfigProcessor.DatasourceId;
        }
        else if (hasProjectJson)
        {
            // project.json is legacy but should be used if present
            if (Config.TRACE) Console.WriteLine($"Using legacy project.json: {Options.ProjectJsonPath}");
            var projectJsonResolver = new ProjectJsonProcessor(
                projectName: Options.ProjectName,
                projectJsonPath: Options.ProjectJsonPath!);
            var projectJsonResult = projectJsonResolver.Resolve();
            package.packages = projectJsonResult.Packages;
            package.dependencies = projectJsonResult.Dependencies;
            package.datasource_id = ProjectJsonProcessor.DatasourceId;
        }
        else
        {
            // In the most common case we use the *proj file and its PackageReference
            if (Config.TRACE)
                Console.WriteLine($"Attempting proj file and package-reference resolver: {Options.ProjectFilePath}");

            ProjectFileProcessor projfile_resolver = new(
                projectPath: Options.ProjectFilePath,
                nugetApi: NugetApiService,
                projectTargetFramework: project_target_framework);

            DependencyResolution dependency_resolution = projfile_resolver.Resolve();

            if (dependency_resolution.Success)
            {
                if (Config.TRACE) Console.WriteLine("ProjFileStandardPackageReferenceHandler success.");
                package.packages = dependency_resolution.Packages;
                package.dependencies = dependency_resolution.Dependencies;
                package.datasource_id = ProjectFileProcessor.DatasourceId;
            }
            else
            {
                if (Config.TRACE){
                    Console.WriteLine($"Failed to use projfile_resolver: {dependency_resolution.ErrorMessage}");
                    Console.WriteLine("Using Fallback XML project file reader and resolver.");
                }
                var xmlResolver =
                    new ProjectXmlFileProcessor(
                        projectPath: Options.ProjectFilePath,
                        nugetApi: NugetApiService,
                        project_target_framework: project_target_framework);
                DependencyResolution xml_dependecy_resolution = xmlResolver.Resolve();
                package.version = xml_dependecy_resolution.ProjectVersion;
                package.packages = xml_dependecy_resolution.Packages;
                package.dependencies = xml_dependecy_resolution.Dependencies;
                package.datasource_id = ProjectXmlFileProcessor.DatasourceId;
            }
        }

        if (Config.TRACE)
        {
            Console.WriteLine($"Found #{package.dependencies.Count} dependencies for #{package.packages.Count} packages.");
            Console.WriteLine($"Project resolved: {Options.ProjectName} in {stopWatch!.ElapsedMilliseconds} ms.");
        }

        return package;
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

    /// <summary>
    /// Return the first NuGetFramework found in the *.*proj XML file or null.
    /// Handles new and legacy style target framework references.
    /// </summary>
    /// <param name="projectFilePath"></param>
    /// <returns></returns>
    private static NuGetFramework? FindProjectTargetFramework(string projectFilePath)
    {
        var doc = new XmlDocument();
        doc.Load(filename: projectFilePath);

        var target_framework_version = doc.GetElementsByTagName(name: "TargetFrameworkVersion");
        foreach (XmlNode tfv in target_framework_version)
        {
            var framework_version = tfv.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(framework_version))
            {
                var version = Version.Parse(framework_version.Trim('v', 'V'));
                return new NuGetFramework(FrameworkConstants.FrameworkIdentifiers.Net, version);
            }
        }

        var target_framework = doc.GetElementsByTagName(name: "TargetFramework");
        foreach (XmlNode tf in target_framework)
        {
            var framework_moniker = tf.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(framework_moniker))
            {
                return NuGetFramework.ParseFolder(framework_moniker);
            }
        }

        var target_frameworks = doc.GetElementsByTagName(name: "TargetFrameworks");
        foreach (XmlNode tf in target_frameworks)
        {
            var framework_monikers = tf.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(framework_monikers))
            {
                var monikers = framework_monikers.Split(";", StringSplitOptions.RemoveEmptyEntries);
                foreach (var moniker in monikers)
                {
                     if (!string.IsNullOrWhiteSpace(moniker))
                        return NuGetFramework.ParseFolder(moniker.Trim());
                }
            }
        }

        return null;
    }
}
