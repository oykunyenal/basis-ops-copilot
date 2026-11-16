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

    [Fact]
    public void InfrastructureAssembly_ReferencedAssemblies_DoesNotReferenceSimulatorOrHosts()
    {
        // The SAP wire format is copied into Infrastructure/Sap; the simulator is only reached over HTTP.
        var infrastructure = Assembly.Load("BasisOps.Infrastructure");

        var forbidden = infrastructure.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n is "FakeSap" or "BasisOps.Api" or "BasisOps.Worker")
            .ToList();

        Assert.Empty(forbidden);
    }

    private static bool IsFramework(string name) =>
        name is "netstandard" or "mscorlib"
        || name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Win32", StringComparison.Ordinal);
}
