using System.Management;
using InstantLoginSwitcher.Core.Models;

namespace InstantLoginSwitcher.Core.Services;

public sealed class AccountPictureService
{
    public string GetPicturePath(LocalAdminAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.SidValue))
        {
            var fromProfile = TryResolveFromUserProfile(account.SidValue);
            if (!string.IsNullOrWhiteSpace(fromProfile))
            {
                return fromProfile;
            }
        }

        foreach (var extension in new[] { "png", "jpg", "jpeg", "bmp" })
        {
            var candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft",
                "User Account Pictures",
                $"{account.UserName}.{extension}");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "User Account Pictures",
            "user.png");

        return File.Exists(fallback) ? fallback : string.Empty;
    }

    private static string TryResolveFromUserProfile(string sidValue)
    {
        try
        {
            var escapedSid = sidValue.Replace("\\", "\\\\").Replace("'", "''");
            using var searcher = new ManagementObjectSearcher(
                $"SELECT LocalPath FROM Win32_UserProfile WHERE SID='{escapedSid}'");

            foreach (ManagementObject profile in searcher.Get())
            {
                var localPath = profile["LocalPath"]?.ToString();
                if (string.IsNullOrWhiteSpace(localPath))
                {
                    continue;
                }

                var accountPicturesDir = Path.Combine(
                    localPath,
                    "AppData",
                    "Roaming",
                    "Microsoft",
                    "Windows",
                    "AccountPictures");

                if (!Directory.Exists(accountPicturesDir))
                {
                    continue;
                }

                var picture = Directory
                    .GetFiles(accountPicturesDir)
                    .Where(path =>
                    {
                        var extension = Path.GetExtension(path);
                        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (picture is not null)
                {
                    return picture.FullName;
                }
            }
        }
        catch
        {
            // Best effort only.
        }

        return string.Empty;
    }
}
