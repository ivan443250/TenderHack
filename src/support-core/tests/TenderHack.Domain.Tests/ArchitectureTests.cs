using TenderHack.Domain;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DomainAssemblyHasNoRuntimeDependencies()
    {
        Assert.NotNull(typeof(AssemblyMarker).Assembly);
        Assert.DoesNotContain(typeof(AssemblyMarker).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is "TenderHack.Application" or "TenderHack.Infrastructure");
    }
}
