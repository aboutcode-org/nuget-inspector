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
        var resolver = new LockFileHandler(
            lockFile: LockFileUtilities.GetLockFile(ProjectLockJsonPath, logger: null));
        return resolver.Process();
    }
}