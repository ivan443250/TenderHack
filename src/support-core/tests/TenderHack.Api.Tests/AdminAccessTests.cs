using TenderHack.Api.Admin;
using Xunit;

namespace TenderHack.Api.Tests;

/// <summary>The whole `/admin` gate is one pure function — cover its three outcomes without a host.</summary>
public sealed class AdminAccessTests
{
    [Fact]
    public void DisabledPanelIsAbsentRegardlessOfToken()
    {
        var options = new AdminOptions { Enabled = false, Token = "secret" };

        Assert.Equal(AdminAccessResult.Disabled, AdminAccess.Check(options, "secret"));
        Assert.Equal(AdminAccessResult.Disabled, AdminAccess.Check(options, null));
    }

    [Fact]
    public void EnabledWithoutTokenIsOpen()
    {
        var options = new AdminOptions { Enabled = true, Token = "" };

        Assert.Equal(AdminAccessResult.Ok, AdminAccess.Check(options, null));
        Assert.Equal(AdminAccessResult.Ok, AdminAccess.Check(options, "anything"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("secre")]
    public void ConfiguredTokenRejectsMissingOrWrongValue(string? presented)
    {
        var options = new AdminOptions { Enabled = true, Token = "secret" };

        Assert.Equal(AdminAccessResult.Unauthorized, AdminAccess.Check(options, presented));
    }

    [Fact]
    public void ConfiguredTokenAcceptsExactMatch()
    {
        var options = new AdminOptions { Enabled = true, Token = "secret" };

        Assert.Equal(AdminAccessResult.Ok, AdminAccess.Check(options, "secret"));
    }
}
