using System.Reflection;

namespace BasisOps.UnitTests;

// Layer rules: Domain ← Application ← Infrastructure ← Api / Worker.
public class ArchitectureTests
{
    [Fact]
    public void DomainAssembly_ReferencedAssemblies_OnlyFrameworkAssemblies()
    {
        var domain = Assembly.Load("BasisOps.Domain");

        var nonFramework = domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !IsFramework(n))
            .ToList();

        Assert.Empty(nonFramework);
    }

    [Fact]
    public void ApplicationAssembly_ReferencedAssemblies_DoesNotReferenceOuterLayers()
    {
        var application = Assembly.Load("BasisOps.Application");

        var outer = application.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("BasisOps.", StringComparison.Ordinal) && n != "BasisOps.Domain")
            .ToList();

        Assert.Empty(outer);
    }

    private static bool IsFramework(string name) =>
        name is "netstandard" or "mscorlib"
        || name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Win32", StringComparison.Ordinal);
}
