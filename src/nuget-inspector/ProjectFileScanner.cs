using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;

namespace NugetInspector;

internal class ProjectScannerOptions : ScanOptions
{
    public ProjectScannerOptions(ScanOptions old)
    {
        ProjectFilePath = old.ProjectFilePath;
        Verbose = old.Verbose;
        NugetApiFeedUrl = old.NugetApiFeedUrl;
        OutputFilePath = old.OutputFilePath;
    }

    public string? ProjectName { get; set; }
    public string? ProjectUniqueId { get; set; }
    public string ProjectDirectory { get; set; }
    public string? ProjectVersion { get; set; }
    public string PackagesConfigPath { get; set; }
    public string ProjectJsonPath { get; set; }
    public string ProjectJsonLockPath { get; set; }
    public string ProjectAssetsJsonPath { get; set; }
}

internal class ProjectFileScanner : IScanner
{
    public NugetApi NugetApiService;
    public ProjectScannerOptions Options;

    /// <summary>
    /// A Scanner for project "*proj" project file input such as .csproj file
    /// </summary>
    /// <param name="options"></param>
    /// <param name="nugetApiService"></param>
    /// <exception cref="Exception"></exception>
    public ProjectFileScanner(ProjectScannerOptions options, NugetApi nugetApiService)
    {
        Options = options;
        NugetApiService = nugetApiService;
        if (Options == null) throw new Exception("Must provide a valid options object.");

        if (string.IsNullOrWhiteSpace(Options.ProjectDirectory))
            Options.ProjectDirectory = Directory.GetParent(Options.ProjectFilePath)?.FullName ?? string.Empty;

        var optionsProjectDirectory = Options.ProjectDirectory;

        if (string.IsNullOrWhiteSpace(Options.PackagesConfigPath))
            Options.PackagesConfigPath = Path.Combine(optionsProjectDirectory ?? string.Empty, "packages.config").Replace("\\", "/");

        if (string.IsNullOrWhiteSpace(Options.ProjectAssetsJsonPath))
            Options.ProjectAssetsJsonPath = Path.Combine(optionsProjectDirectory ?? string.Empty, "obj", "project.assets.json").Replace("\\", "/");

        if (string.IsNullOrWhiteSpace(Options.ProjectJsonPath))
            Options.ProjectJsonPath = Path.Combine(optionsProjectDirectory ?? string.Empty, "project.json").Replace("\\", "/");

        if (string.IsNullOrWhiteSpace(Options.ProjectJsonLockPath))
            Options.ProjectJsonLockPath = Path.Combine(optionsProjectDirectory ?? string.Empty, "project.lock.json").Replace("\\", "/");

        if (string.IsNullOrWhiteSpace(Options.ProjectName))
            Options.ProjectName = Path.GetFileNameWithoutExtension(Options.ProjectFilePath);

        if (string.IsNullOrWhiteSpace(Options.ProjectUniqueId))
            Options.ProjectUniqueId = Path.GetFileNameWithoutExtension(Options.ProjectFilePath);

        if (string.IsNullOrWhiteSpace(Options.ProjectVersion))
            Options.ProjectVersion = AssemblyInfoParser.GetProjectAssemblyVersion(optionsProjectDirectory);

        if (string.IsNullOrWhiteSpace(Options.OutputFilePath))
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            Options.OutputFilePath = Path.Combine(currentDirectory, "nuget-inpector-results.json").Replace("\\", "/");
        }
    }

    public Scan RunScan()
    {
        try
        {
            var package = GetPackage();
            List<Package> packages = new List<Package>();
            if (package != null)
            {
                packages.Add(package);
            }

            return new Scan
            {
                Status = Scan.ResultStatus.Success,
                ResultName = Options.ProjectUniqueId,
                OutputFilePath = Options.OutputFilePath,
                Packages = packages
            };
        }
        catch (Exception ex)
        {
            if (Config.TRACE) Console.WriteLine($"{ex}");
            return new Scan
            {
                Status = Scan.ResultStatus.Error,
                Exception = ex
            };
        }
    }

    public Package? GetPackage()
    {
        var stopWatch = Stopwatch.StartNew();
        if (Config.TRACE)
        {
            Console.WriteLine($"Processing Project: {Options.ProjectName} using Project Directory: {Options.ProjectDirectory}");
        }
        var package = new Package
        {
            Name = Options.ProjectUniqueId,
            Version = Options.ProjectVersion,
            SourcePath = Options.ProjectFilePath,
            Type = "xproj-file"
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

        bool hasPackagesConfig = FileExists(path: Options.PackagesConfigPath);
        bool hasProjectJson = FileExists(path: Options.ProjectJsonPath);
        bool hasProjectJsonLock = FileExists(path: Options.ProjectJsonLockPath);
        bool hasProjectAssetsJson = FileExists(path: Options.ProjectAssetsJsonPath);

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
            if (Config.TRACE) Console.WriteLine($"Using project.assets.json file: {Options.ProjectAssetsJsonPath}");
            var projectAssetsJsonResolver = new ProjectAssetsJsonHandler(Options.ProjectAssetsJsonPath);
            var projectAssetsJsonResult = projectAssetsJsonResolver.Process();
            package.Packages = projectAssetsJsonResult.Packages;
            package.Dependencies = projectAssetsJsonResult.Dependencies;
            package.DatasourceId = ProjectAssetsJsonHandler.DatasourceId;
        }
        else if (hasPackagesConfig)
        {
            // packages.config is legacy but should be used if present
            if (Config.TRACE) Console.WriteLine($"Using packages.config: {Options.PackagesConfigPath}");
            var packagesConfigResolver = new LegacyPackagesConfigResolver(Options.PackagesConfigPath, NugetApiService);
            var packagesConfigResult = packagesConfigResolver.Process();
            package.Packages = packagesConfigResult.Packages;
            package.Dependencies = packagesConfigResult.Dependencies;
            package.DatasourceId = LegacyPackagesConfigResolver.DatasourceId;
        }
        else if (hasProjectJsonLock)
        {
            // projects.json.lock is legacy but should be used if present
            if (Config.TRACE) Console.WriteLine($"Using legacy projects.json.lock: {Options.ProjectJsonLockPath}");
            var projectJsonLockResolver = new LegacyProjectLockJsonHandler(Options.ProjectJsonLockPath);
            var projectJsonLockResult = projectJsonLockResolver.Process();
            package.Packages = projectJsonLockResult.Packages;
            package.Dependencies = projectJsonLockResult.Dependencies;
            package.DatasourceId = LegacyProjectLockJsonHandler.DatasourceId;
        }
        else if (hasProjectJson)
        {
            // project.json is legacy but should be used if present
            if (Config.TRACE) Console.WriteLine($"Using legacy project.json: {Options.ProjectJsonPath}");
            var projectJsonResolver = new LegacyProjectJsonHandler(Options.ProjectName, Options.ProjectJsonPath);
            var projectJsonResult = projectJsonResolver.Process();
            package.Packages = projectJsonResult.Packages;
            package.Dependencies = projectJsonResult.Dependencies;
            package.DatasourceId = LegacyProjectJsonHandler.DatasourceId;
        }
        else
        {
            // In the most common case we use the *proj file and its PackageReference
            if (Config.TRACE) Console.WriteLine($"Attempting package-reference resolver: {Options.ProjectFilePath}");
            var pkgRefResolver = new PackageReferenceResolver(
                projectPath: Options.ProjectFilePath, 
                nugetApi: NugetApiService, 
                projectTargetFramework: projectTargetFramework);

            var projectReferencesResult = pkgRefResolver.Process();

            if (projectReferencesResult.Success)
            {
                if (Config.TRACE) Console.WriteLine("PackageReferenceResolver success.");
                package.Packages = projectReferencesResult.Packages;
                package.Dependencies = projectReferencesResult.Dependencies;
                package.DatasourceId = PackageReferenceResolver.DatasourceId;
            }
            else
            {
                if (Config.TRACE) Console.WriteLine("Using Fallback XML project file reader and resolver.");
                var xmlResolver = new ProjectXmlFallBackResolver(Options.ProjectFilePath, NugetApiService, projectTargetFramework);
                var xmlResult = xmlResolver.Process();
                package.Version = xmlResult.ProjectVersion;
                package.Packages = xmlResult.Packages;
                package.Dependencies = xmlResult.Dependencies;
                package.DatasourceId = ProjectXmlFallBackResolver.DatasourceId;
            }
        }

        if (Config.TRACE)
        {
            Console.WriteLine($"Found #{package.Dependencies.Count} dependencies for #{package.Packages.Count} packages.");
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
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private NuGetFramework? ParseTargetFramework()
    {
        var targetFramework = ExtractTargetFramework(Options.ProjectFilePath);
        var projectTargetFramework = NuGetFramework.ParseFolder(targetFramework);
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
            { "TargetFrameworkVersion", "TargetFramework", "TargetFrameworks" }.Select(x => x.ToLowerInvariant());

        var csProj = XElement.Load(projectFilePath);
        var targetFrameworks = csProj.Descendants()
            .Where(e => targetFrameworkNodeNames.Contains(e.Name.LocalName.ToLowerInvariant())).ToList();

        if (!targetFrameworks.Any())
        {
            if (Config.TRACE)
                Console.WriteLine(
                    $"Warning - Target Framework: Could not extract a target framework for {projectFilePath}");
            return string.Empty;
        }

        if (targetFrameworks.Count > 1 && Config.TRACE)
        {
            Console.WriteLine($"Warning - Target Framework: Found multiple target frameworks for {projectFilePath}");
        }

        if (Config.TRACE)
            Console.WriteLine($"Found the following TargetFramework(s): {string.Join(Environment.NewLine, targetFrameworks)}");

        return targetFrameworks.First().Value;
    }

    public List<string> FindOutputPaths()
    {
        if (Config.TRACE)
        {
            Console.WriteLine("Attempting to parse configuration output paths.");
            Console.WriteLine("Project File: " + Options.ProjectFilePath);
        }

        try
        {
            var proj = new Microsoft.Build.Evaluation.Project(Options.ProjectFilePath);
            var outputPaths = new List<string>();
            List<string>? configurations;
            proj.ConditionedProperties.TryGetValue("Configuration", out configurations);
            if (configurations == null) configurations = new List<string>();
            foreach (var config in configurations)
            {
                proj.SetProperty("Configuration", config);
                proj.ReevaluateIfNecessary();
                var path = proj.GetPropertyValue("OutputPath");
                var fullPath = Path.Combine(proj.DirectoryPath, path).Replace("\\", "/");
                outputPaths.Add(fullPath);
                if (Config.TRACE) Console.WriteLine($"Found path: {fullPath}");
            }

            ProjectCollection.GlobalProjectCollection.UnloadProject(proj);
            if (Config.TRACE) Console.WriteLine($"Found {outputPaths.Count} paths.");
            return outputPaths;
        }
        catch (Exception)
        {
            if (Config.TRACE) Console.WriteLine("Skipping configuration output paths.");
            return new List<string>();
        }
    }

    public static readonly List<string> SupportedProjectGlobs = new()
    {
        //C#
        "*.csproj",
        //F#
        "*.fsproj",
        //VB
        "*.vbproj",
        //Azure Stream Analytics
        "*.asaproj",
        //Docker Compose
        "*.dcproj",
        //Shared Projects
        "*.shproj",
        //Cloud Computing
        "*.ccproj",
        //Fabric Application
        "*.sfproj",
        //Node.js
        "*.njsproj",
        //VC++
        "*.vcxproj",
        //VC++
        "*.vcproj",
        //.NET Core
        "*.xproj",
        //Python
        "*.pyproj",
        //Hive
        "*.hiveproj",
        //Pig
        "*.pigproj",
        //JavaScript
        "*.jsproj",
        //U-SQL
        "*.usqlproj",
        //Deployment
        "*.deployproj",
        //Common Project System Files
        "*.msbuildproj",
        //SQL
        "*.sqlproj",
        //SQL Project Files
        "*.dbproj",
        //RStudio
        "*.rproj"
    };
}