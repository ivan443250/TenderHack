using Xunit;

namespace TenderHack.Contract.Tests;

public sealed class ContractSmokeTests
{
    [Fact]
    public void FrozenContractArtifactsRemainInDocs()
    {
        var repositoryRoot = FindRepositoryRoot();
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "docs", "contracts", "knowledge-v0.openapi.yaml")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "docs", "contracts", "knowledge-v0.md")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "contracts", "knowledge-v0.openapi.yaml")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
