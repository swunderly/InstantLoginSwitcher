using InstantLoginSwitcher.Core.Services;

namespace InstantLoginSwitcher.Core.Tests;

public sealed class HotkeyParserTests
{
    private readonly HotkeyParser _parser = new();

    [Fact]
    public void Parse_NormalizesAliasTokens()
    {
        var definition = _parser.Parse("control+alt+s");

        Assert.Equal("Ctrl+Alt+S", definition.CanonicalText);
        Assert.Equal(3, definition.Tokens.Count);
    }

    [Fact]
    public void Parse_RejectsDuplicateTokens()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _parser.Parse("Ctrl+Ctrl+S"));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsBlankInput()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _parser.Parse("   "));

        Assert.Contains("blank", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
