namespace NugetInspector;

internal interface IDependencyProcessor
{
    /// <summary>
    /// Process and resolve dependencies and return a DependencyResolution
    /// </summary>
    /// <returns>DependencyResolution</returns>
    DependencyResolution Resolve();
}

public class DependencyResolution
{
    public bool Success { get; set; } = true;
    public List<BasePackage> Dependencies { get; set; } = new();

    public DependencyResolution() {}

    public DependencyResolution(bool success)
    {
        Success = success;
    }

}