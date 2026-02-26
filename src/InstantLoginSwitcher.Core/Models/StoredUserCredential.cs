namespace InstantLoginSwitcher.Core.Models;

public sealed class StoredUserCredential
{
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Qualified { get; set; } = string.Empty;
    public string SidValue { get; set; } = string.Empty;
    public string PasswordEncrypted { get; set; } = string.Empty;
    public string PicturePath { get; set; } = string.Empty;
}
