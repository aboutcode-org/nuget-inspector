using NuGet.ProjectModel;

namespace NugetInspector;

/// <summary>
/// See https://learn.microsoft.com/en-us/nuget/archive/project-json#projectlockjson
/// </summary>
internal class LegacyProjectLockJsonHandler : IDependencyResolver
{
    public const string DatasourceId = "dotnet-project.lock.json";
    private readonly string ProjectLockJsonPath;

    public LegacyProjectLockJsonHandler(string projectLockJsonPath)
    {
        ProjectLockJsonPath = projectLockJsonPath;
    }

    public DependencyResolution Process()
    {
        var lockFile = LockFileUtilities.GetLockFile(ProjectLockJsonPath, logger: new NugetLogger());
        if (lockFile == null)
        {
            throw new Exception(message: "Failed to get GetLockFile at path: ProjectLockJsonPath");
        }

        var resolver = new LockFileHandler(lockFile: lockFile);
        if (Config.TRACE)
        {
            Console.WriteLine($"resolver: {resolver}");
        }

        return resolver.Process();
    }
}