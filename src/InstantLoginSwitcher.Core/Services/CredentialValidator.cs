using InstantLoginSwitcher.Core.Interop;
using System.Runtime.InteropServices;

namespace InstantLoginSwitcher.Core.Services;

public sealed class CredentialValidator
{
    public (bool Success, int Win32Error) Validate(string qualifiedUser, string password)
    {
        if (string.IsNullOrWhiteSpace(qualifiedUser))
        {
            throw new InvalidOperationException("Qualified user cannot be blank.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, 0);
        }

        var parts = qualifiedUser.Split('\\', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"Invalid qualified user format: {qualifiedUser}");
        }

        var token = IntPtr.Zero;
        try
        {
            var success = NativeMethods.LogonUser(
                parts[1],
                parts[0],
                password,
                dwLogonType: 2,
                dwLogonProvider: 0,
                out token);

            var error = Marshal.GetLastWin32Error();
            return (success, error);
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(token);
            }
        }
    }
}
