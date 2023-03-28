using NuGet.ProjectModel;

namespace NugetInspector;

/// <summary>
/// See https://learn.microsoft.com/en-us/nuget/archive/project-json#projectlockjson
/// </summary>
internal class ProjectLockJsonProcessor : IDependencyProcessor
{
    public const string DatasourceId = "dotnet-project.lock.json";
    private readonly string ProjectLockJsonPath;

    public ProjectLockJsonProcessor(string projectLockJsonPath)
    {
        ProjectLockJsonPath = projectLockJsonPath;
    }

    public DependencyResolution Resolve()
    {
        var lockFile = LockFileUtilities.GetLockFile(lockFilePath: ProjectLockJsonPath, logger: new NugetLogger());
        if (lockFile == null)
        {
            throw new Exception(message: "Failed to get GetLockFile at path: ProjectLockJsonPath");
        }

        var resolver = new LockFileHelper(lockfile: lockFile);
        if (Config.TRACE)
        {
            Console.WriteLine($"resolver: {resolver}");
        }

        return resolver.Process();
    }
}