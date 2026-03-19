using SysCleaner.Domain.Repair;

namespace SysCleaner.Tests.Application;

public sealed class WindowsUpdateEventParserTests
{
    [Fact]
    public void ExtractKbId_ReturnsKbIdentifier_WhenPresentInMessage()
    {
        var kbId = WindowsUpdateEventParser.ExtractKbId("安装失败: Windows 安全更新 (KB5034765)");

        Assert.Equal("KB5034765", kbId);
    }

    [Fact]
    public void BuildTitle_FallsBackToFirstLine_WhenNoKbExists()
    {
        var title = WindowsUpdateEventParser.BuildTitle("Feature update installation failed\r\nError code: 0x800f081f");

        Assert.Equal("Feature update installation failed", title);
    }
}