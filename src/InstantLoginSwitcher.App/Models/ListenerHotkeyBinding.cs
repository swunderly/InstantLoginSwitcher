using InstantLoginSwitcher.Core.Models;

namespace InstantLoginSwitcher.App.Models;

public sealed class ListenerHotkeyBinding
{
    public required string CanonicalHotkey { get; init; }
    public required HotkeyDefinition Definition { get; init; }
    public bool IsTriggered { get; set; }
}
