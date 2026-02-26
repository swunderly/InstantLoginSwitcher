namespace InstantLoginSwitcher.Core.Models;

public sealed class SwitcherConfig
{
    public int Version { get; set; } = 1;
    public string MachineName { get; set; } = Environment.MachineName;
    public string UpdatedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public List<StoredUserCredential> Users { get; set; } = new();
    public List<SwitchProfile> Profiles { get; set; } = new();
}
