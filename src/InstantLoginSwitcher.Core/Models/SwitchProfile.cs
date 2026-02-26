namespace InstantLoginSwitcher.Core.Models;

public sealed class SwitchProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string UserA { get; set; } = string.Empty;
    public string UserB { get; set; } = string.Empty;
    public string Hotkey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
