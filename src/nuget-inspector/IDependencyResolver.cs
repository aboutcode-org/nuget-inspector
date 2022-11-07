namespace NugetInspector;

internal interface IDependencyResolver
{
    /// <summary>
    /// Resolve dependencies and return a DependencyResolution
    /// </summary>
    /// <returns>DependencyResolution</returns>
    DependencyResolution Process();
}