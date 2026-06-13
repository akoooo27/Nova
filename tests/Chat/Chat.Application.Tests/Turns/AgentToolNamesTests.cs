using Chat.Application.Turns.Tools;

namespace Chat.Application.Tests.Turns;

public sealed class AgentToolNamesTests
{
    [Theory]
    [InlineData("web_search", true)]
    [InlineData("WEB_SEARCH", false)]
    [InlineData("unknown", false)]
    public void IsKnownRecognizesOnlyKnownToolNames(string name, bool expected)
    {
        Assert.Equal(expected, AgentToolNames.IsKnown(name));
    }
}