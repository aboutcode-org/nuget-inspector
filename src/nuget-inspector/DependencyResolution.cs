namespace NugetInspector;

public class DependencyResolution
{
    public bool Success { get; set; } = true;
    public string? ProjectVersion { get; set; }
    public List<PackageSet> Packages { get; set; } = new();
    public List<PackageId?> Dependencies { get; set; } = new();
}