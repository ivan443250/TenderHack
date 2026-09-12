using TenderHack.Application;
using Xunit;

namespace TenderHack.Application.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ApplicationAssemblyIsPresent()
    {
        Assert.NotNull(typeof(AssemblyMarker).Assembly);
    }
}
