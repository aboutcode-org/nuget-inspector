using NuGet.ProjectModel;

namespace NugetInspector;

internal class ProjectAssetsJsonHandler : IDependencyResolver
{
    public const string DatasourceId = "dotnet-project.assets.json";
    private readonly string ProjectAssetsJsonPath;

    public ProjectAssetsJsonHandler(string projectAssetsJsonPath)
    {
        ProjectAssetsJsonPath = projectAssetsJsonPath;
    }

    public DependencyResolution Process()
    {
        var lockFile = LockFileUtilities.GetLockFile(ProjectAssetsJsonPath, null);

        var resolver = new LockFileHandler(lockFile);

        return resolver.Process();
    }
}