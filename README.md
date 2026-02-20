# InstantLoginSwitcher

Windows 11 one-button account switching for two local users.

## What this does

- Listens for a global hotkey chord: **Numpad 4 + Numpad 5 + Numpad 6**.
- Detects who is currently logged in.
- Looks up the *other* user's credentials from Windows Credential Manager.
- Sets Windows AutoAdminLogon to the other user.
- Force-closes the current user's applications and logs out.
- Windows auto-signs into the target account.

## Security note

Credentials are stored in **Windows Credential Manager** (DPAPI-protected) under custom targets.
To perform automatic sign-in after logout, Windows AutoAdminLogon registry keys are updated during the switch process.

## Files

- `scripts/Setup-InstantLoginSwitcher.ps1` — one-time installer.
- `scripts/Switch-Login.ps1` — performs the account flip.
- `scripts/CredentialStore.psm1` — reads/writes credentials in Credential Manager.
- `scripts/InstantLoginSwitcher.ahk` — global hotkey listener.

## Quick start on your PC (Samuel + Lizzy)

1. Download this repo to your Windows PC (for example to `C:\Tools\InstantLoginSwitcher`).
2. Install [AutoHotkey v2](https://www.autohotkey.com/) with default options.
3. Open **PowerShell as Administrator**.
4. Run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
cd C:\Tools\InstantLoginSwitcher\scripts
.\Setup-InstantLoginSwitcher.ps1 -PrimaryUser "Samuel Wunderly" -SecondaryUser "Lizzy Wunderly"
```

5. Enter each account's password when prompted.
6. Sign out once, sign back in, then test hotkey: press **Numpad 4 + 5 + 6 together**.

## Validate it is installed

In PowerShell (Admin):

```powershell
schtasks /Query /TN "InstantLoginSwitcher-Hotkey-Listener"
```

You should see the listener task present.

## Daily use

- Press **Numpad 4 + 5 + 6** together.
- Current user is force-logged out (unsaved work is lost).
- Windows logs into the other configured account automatically.

## Troubleshooting

- **AutoHotkey not found**:
  - Reinstall AutoHotkey v2, then rerun setup.
- **Script is not digitally signed / PSSecurityException**:
  - In the same PowerShell window, run: `Set-ExecutionPolicy -Scope Process Bypass -Force`
  - If you downloaded a ZIP, also run: `Get-ChildItem .\*.ps1 | Unblock-File`
  - Then run setup again.
- **Hotkey does nothing**:
  - Sign out and back in once after setup.
  - Check task exists with `schtasks /Query /TN "InstantLoginSwitcher-Hotkey-Listener"`.
  - Confirm AutoHotkey is running after login with `Get-Process AutoHotkey64`.
  - Open Task Scheduler and confirm Last Run Result is `0x0`.
- **Uninstall fails with "file is being used by another process"**:
  - The setup script now stops the running listener before uninstall/reinstall.
  - If you still hit it, run `Get-Process AutoHotkey64,AutoHotkey -ErrorAction SilentlyContinue | Stop-Process -Force` then retry `-Uninstall`.
- **Wrong password after account password change**:
  - Rerun setup script and provide new passwords.

## Uninstall

Run as Administrator:

```powershell
cd C:\Tools\InstantLoginSwitcher\scripts
.\Setup-InstantLoginSwitcher.ps1 -Uninstall
```
