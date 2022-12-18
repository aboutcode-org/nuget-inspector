namespace NugetInspector;

internal interface IDependencyResolver
{
    /// <summary>
    /// Resolve dependencies and return a DependencyResolution
    /// </summary>
    /// <returns>DependencyResolution</returns>
    DependencyResolution Process();
}

public class DependencyResolution
{
    public bool Success { get; set; } = true;
    public string? ProjectVersion { get; set; }
    public string? ErrorMessage { get; set; } = "";
    public List<PackageSet> Packages { get; set; } = new();
    public List<BasePackage> Dependencies { get; set; } = new();
}