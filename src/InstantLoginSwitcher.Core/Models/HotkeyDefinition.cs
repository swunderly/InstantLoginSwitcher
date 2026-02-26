namespace InstantLoginSwitcher.Core.Models;

public sealed class HotkeyToken
{
    public required string Name { get; init; }
    public required IReadOnlyList<int> VirtualKeys { get; init; }
}

public sealed class HotkeyDefinition
{
    public required string SourceText { get; init; }
    public required string CanonicalText { get; init; }
    public required IReadOnlyList<HotkeyToken> Tokens { get; init; }

    public bool IsPressed(ISet<int> pressedKeys)
    {
        foreach (var token in Tokens)
        {
            var tokenMatched = false;
            foreach (var key in token.VirtualKeys)
            {
                if (pressedKeys.Contains(key))
                {
                    tokenMatched = true;
                    break;
                }
            }

            if (!tokenMatched)
            {
                return false;
            }
        }

        return true;
    }
}
