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
    public string? ProjectDirectory { get; set; }
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
            Options.ProjectDirectory = Directory.GetParent(Options.ProjectFilePath)?.FullName;

        if (string.IsNullOrWhiteSpace(Options.PackagesConfigPath))
            Options.PackagesConfigPath = BuildProjectPackageConfigPath(Options.ProjectDirectory);

        if (string.IsNullOrWhiteSpace(Options.ProjectJsonPath))
            Options.ProjectJsonPath = BuildProjectJsonPath(Options.ProjectDirectory);

        if (string.IsNullOrWhiteSpace(Options.ProjectJsonLockPath))
            Options.ProjectJsonLockPath = BuildProjectJsonLockPath(Options.ProjectDirectory);

        if (string.IsNullOrWhiteSpace(Options.ProjectAssetsJsonPath))
            Options.ProjectAssetsJsonPath = BuildProjectAssetsJsonPath(Options.ProjectDirectory);

        if (string.IsNullOrWhiteSpace(Options.ProjectName))
            Options.ProjectName = Path.GetFileNameWithoutExtension(Options.ProjectFilePath);

        if (string.IsNullOrWhiteSpace(Options.ProjectUniqueId))
            Options.ProjectUniqueId = Path.GetFileNameWithoutExtension(Options.ProjectFilePath);

        if (string.IsNullOrWhiteSpace(Options.ProjectVersion))
            Options.ProjectVersion = AssemblyInfoParser.GetProjectAssemblyVersion(Options.ProjectDirectory);

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
                packages = new List<Package> { package };
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
        var stopWatch = Stopwatch.StartNew();
        if (Config.TRACE)
        {
            Console.WriteLine("Processing Project: {0}", Options.ProjectName);
            if (Options.ProjectDirectory != null)
            {
                Console.WriteLine("Using Project Directory: {0}", Options.ProjectDirectory);
            }
        }
        var projectNode = new Package
        {
            Name = Options.ProjectUniqueId,
            Version = Options.ProjectVersion,
            SourcePath = Options.ProjectFilePath,
            Type = "xproj-file"
        };

        var projectTargetFramework = ParseTargetFramework();
        try
        {
            projectNode.OutputPaths = FindOutputPaths();
        }
        catch (Exception)
        {
            if (Config.TRACE) Console.WriteLine("Unable to determine output paths for this project.");
        }
        
        var packagesConfigExists = !string.IsNullOrWhiteSpace(Options.PackagesConfigPath) && File.Exists(Options.PackagesConfigPath);
        var projectJsonExists = !string.IsNullOrWhiteSpace(Options.ProjectJsonPath) && File.Exists(Options.ProjectJsonPath);
        var projectJsonLockExists = !string.IsNullOrWhiteSpace(Options.ProjectJsonLockPath) && File.Exists(Options.ProjectJsonLockPath);
        var projectAssetsJsonExists = !string.IsNullOrWhiteSpace(Options.ProjectAssetsJsonPath) && File.Exists(Options.ProjectAssetsJsonPath);

        if (packagesConfigExists)
        {
            if (Config.TRACE) Console.WriteLine("Using packages.config: " + Options.PackagesConfigPath);
            var packagesConfigResolver = new LegacyPackagesConfigResolver(Options.PackagesConfigPath, NugetApiService);
            var packagesConfigResult = packagesConfigResolver.Process();
            projectNode.Packages = packagesConfigResult.Packages;
            projectNode.Dependencies = packagesConfigResult.Dependencies;
            projectNode.DatasourceId = LegacyPackagesConfigResolver.DatasourceId;
        }
        else if (projectJsonLockExists)
        {
            if (Config.TRACE) Console.WriteLine("Using projects.json.lock: " + Options.ProjectJsonLockPath);
            var projectJsonLockResolver = new LegacyProjectLockJsonHandler(Options.ProjectJsonLockPath);
            var projectJsonLockResult = projectJsonLockResolver.Process();
            projectNode.Packages = projectJsonLockResult.Packages;
            projectNode.Dependencies = projectJsonLockResult.Dependencies;
            projectNode.DatasourceId = LegacyProjectLockJsonHandler.DatasourceId;
        }
        else if (projectAssetsJsonExists)
        {
            if (Config.TRACE) Console.WriteLine("Using project.assets.json file: " + Options.ProjectAssetsJsonPath);
            var projectAssetsJsonResolver = new ProjectAssetsJsonHandler(Options.ProjectAssetsJsonPath);
            var projectAssetsJsonResult = projectAssetsJsonResolver.Process();
            projectNode.Packages = projectAssetsJsonResult.Packages;
            projectNode.Dependencies = projectAssetsJsonResult.Dependencies;
            projectNode.DatasourceId = ProjectAssetsJsonHandler.DatasourceId;
        }
        else if (projectJsonExists)
        {
            if (Config.TRACE) Console.WriteLine("Using project.json: " + Options.ProjectJsonPath);
            var projectJsonResolver = new LegacyProjectJsonHandler(Options.ProjectName, Options.ProjectJsonPath);
            var projectJsonResult = projectJsonResolver.Process();
            projectNode.Packages = projectJsonResult.Packages;
            projectNode.Dependencies = projectJsonResult.Dependencies;
            projectNode.DatasourceId = LegacyProjectJsonHandler.DatasourceId;
        }
        else
        {
            if (Config.TRACE) Console.WriteLine("Attempting porject-reference resolver: " + Options.ProjectFilePath);
            var referenceResolver =
                new PackageReferenceResolver(Options.ProjectFilePath, NugetApiService, projectTargetFramework);
            var projectReferencesResult = referenceResolver.Process();
            if (projectReferencesResult.Success)
            {
                if (Config.TRACE) Console.WriteLine("Reference resolver succeeded.");
                projectNode.Packages = projectReferencesResult.Packages;
                projectNode.Dependencies = projectReferencesResult.Dependencies;
                projectNode.DatasourceId = PackageReferenceResolver.DatasourceId;
            }
            else
            {
                if (Config.TRACE) Console.WriteLine("Using backup XML proj file  resolver.");
                var xmlResolver = new ProjectXmlFallBackResolver(Options.ProjectFilePath, NugetApiService, projectTargetFramework);
                var xmlResult = xmlResolver.Process();
                projectNode.Version = xmlResult.ProjectVersion;
                projectNode.Packages = xmlResult.Packages;
                projectNode.Dependencies = xmlResult.Dependencies;
                projectNode.DatasourceId = ProjectXmlFallBackResolver.DatasourceId;
            }
        }

        if (projectNode != null && projectNode.Dependencies != null && projectNode.Packages != null)
            if (Config.TRACE)
                Console.WriteLine("Found {0} dependencies among {1} packages.", projectNode.Dependencies.Count,
                    projectNode.Packages.Count);
        if (Config.TRACE)
            Console.WriteLine("Finished processing project {0} which took {1} ms.", Options.ProjectName,
                stopWatch.ElapsedMilliseconds);

        return projectNode;
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
                Console.WriteLine("Warning - Target Framework: Could not extract a target framework for " +
                                  projectFilePath);
            return string.Empty;
        }

        if (targetFrameworks.Count > 1 && Config.TRACE)
        {
            Console.WriteLine("Warning - Target Framework: Found multiple target frameworks for " + projectFilePath);
        }

        if (Config.TRACE)
            Console.WriteLine("Found the following TargetFramework(s): {0}",
                string.Join(Environment.NewLine, targetFrameworks));

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
                if (Config.TRACE) Console.WriteLine("Found path: " + fullPath);
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

    private string BuildProjectPackageConfigPath(string? projectDirectory)
    {
        return Path.Combine(projectDirectory, "packages.config").Replace("\\", "/");
    }

    private string BuildProjectJsonPath(string? projectDirectory)
    {
        return Path.Combine(projectDirectory, "project.json").Replace("\\", "/");
    }

    private string BuildProjectJsonLockPath(string? projectDirectory)
    {
        return Path.Combine(projectDirectory, "project.lock.json").Replace("\\", "/");
    }

    private string BuildProjectAssetsJsonPath(string? projectDirectory)
    {
        return Path.Combine(projectDirectory, "obj", "project.assets.json").Replace("\\", "/");
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