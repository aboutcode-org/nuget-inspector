namespace NugetInspector;

internal interface IDependencyProcessor
{
    /// <summary>
    /// RProcess and resolve dependencies and return a DependencyResolution
    /// </summary>
    /// <returns>DependencyResolution</returns>
    DependencyResolution Resolve();
}

public class DependencyResolution
{
    public DependencyResolution() {}

    public DependencyResolution(bool Success)
    {
        this.Success = Success;
    }

    public bool Success { get; set; } = true;
    public string? ProjectVersion { get; set; }
    public string? ErrorMessage { get; set; } = "";
    public List<BasePackage> Packages { get; set; } = new();
    public List<BasePackage> Dependencies { get; set; } = new();
}