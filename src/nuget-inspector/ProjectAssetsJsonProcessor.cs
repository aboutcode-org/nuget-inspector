using NuGet.ProjectModel;

namespace NugetInspector;

internal class ProjectAssetsJsonProcessor : IDependencyProcessor
{
    public const string DatasourceId = "dotnet-project.assets.json";
    private readonly string ProjectAssetsJsonPath;

    public ProjectAssetsJsonProcessor(string projectAssetsJsonPath)
    {
        ProjectAssetsJsonPath = projectAssetsJsonPath;
    }

    public DependencyResolution Resolve()
    {
        var lockFile = LockFileUtilities.GetLockFile(lockFilePath: ProjectAssetsJsonPath, logger: null);
        var resolver = new LockFileHelper(lockfile: lockFile);
        return resolver.Process();
    }
}