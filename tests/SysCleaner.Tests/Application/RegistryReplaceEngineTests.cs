using SysCleaner.Domain.Models;
using SysCleaner.Domain.Registry;

namespace SysCleaner.Tests.Application;

public sealed class RegistryReplaceEngineTests
{
    [Fact]
    public void Replace_IgnoresCase_WhenMatchCaseDisabled()
    {
        var result = RegistryReplaceEngine.Replace(
            "Acme Cleaner launcher",
            new RegistryReplaceOptions("acme", "SysCleaner", MatchCase: false, MatchWholeWord: false));

        Assert.Equal("SysCleaner Cleaner launcher", result);
    }

    [Fact]
    public void Replace_UsesWholeWord_WhenEnabled()
    {
        var result = RegistryReplaceEngine.Replace(
            "tool toolbox tool",
            new RegistryReplaceOptions("tool", "kit", MatchCase: true, MatchWholeWord: true));

        Assert.Equal("kit toolbox kit", result);
    }
}