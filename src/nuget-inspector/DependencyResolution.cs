namespace NugetInspector;

internal interface IDependencyResolver
{
    /// <summary>
    /// Resolve dependencies and return a DependencyResolution
    /// </summary>
    /// <returns>DependencyResolution</returns>
    DependencyResolution Resolve();
}

public class DependencyResolution
{
    public bool Success { get; set; } = true;
    public string? ProjectVersion { get; set; }
    public string? ErrorMessage { get; set; } = "";
    public List<BasePackage> Packages { get; set; } = new();
    public List<BasePackage> Dependencies { get; set; } = new();
}