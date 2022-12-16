﻿using System.Diagnostics;
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
    public ProjectScannerOptions? Options;
    public List<Package> Packages = new();
    public string? ResultName;
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
            name = Options.ProjectUniqueId,
            version = Options.ProjectVersion,
            project_file = Options.ProjectFilePath,
        };

        var projectTargetFramework = ParseTargetFramework();
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
            var projectAssetsJsonResult = projectAssetsJsonResolver.Process();
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
            var projectJsonLockResult = projectJsonLockResolver.Process();
            package.packages = projectJsonLockResult.Packages;
            package.dependencies = projectJsonLockResult.Dependencies;
            package.datasource_id = LegacyProjectLockJsonHandler.DatasourceId;
        }
        else if (hasPackagesConfig)
        {
            // packages.config is legacy but should be used if present
            if (Config.TRACE) Console.WriteLine($"Using packages.config: {Options.PackagesConfigPath}");
            var packagesConfigResolver = new PackagesConfigHandler(
                packages_config_path: Options.PackagesConfigPath!, nuget_api: NugetApiService);
            var packagesConfigResult = packagesConfigResolver.Process();
            package.packages = packagesConfigResult.Packages;
            package.dependencies = packagesConfigResult.Dependencies;
            package.datasource_id = PackagesConfigHandler.DatasourceId;
        }
        else if (hasProjectJson)
        {
            // project.json is legacy but should be used if present
            if (Config.TRACE) Console.WriteLine($"Using legacy project.json: {Options.ProjectJsonPath}");
            var projectJsonResolver = new LegacyProjectJsonHandler(projectName: Options.ProjectName,
                projectJsonPath: Options.ProjectJsonPath!);
            var projectJsonResult = projectJsonResolver.Process();
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
                projectTargetFramework: projectTargetFramework);

            var projectReferencesResult = pkgRefResolver.Process();

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
                        nugetApi: NugetApiService, projectTargetFramework: projectTargetFramework);
                var xmlResult = xmlResolver.Process();
                package.version = xmlResult.ProjectVersion;
                package.packages = xmlResult.Packages;
                package.dependencies = xmlResult.Dependencies;
                package.datasource_id = ProjFileXmlParserPackageReferenceHandler.DatasourceId;
            }
        }

        if (Config.TRACE)
        {
            Console.WriteLine(
                value: $"Found #{package.dependencies.Count} dependencies for #{package.packages.Count} packages.");
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
    /// Return the first TargetFramework found in the *.*proj XML file 
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
}