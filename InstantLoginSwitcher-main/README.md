# InstantLoginSwitcher

Windows 11 two-user hotkey switcher.

## What it does

- Starts a background hotkey listener at login.
- Hotkey is **Numpad4 + Numpad5 + Numpad6** pressed together.
- When triggered:
  - Detects the current logged-in user from the configured pair.
  - Configures AutoAdminLogon for the other user.
  - Forces logout.
  - Windows immediately starts signing in the other user.

This is not Fast User Switching. It is a forced logout + immediate autologon to the paired account.

## Important requirements

- AutoHotkey v2 must be installed.
- Installer/uninstaller must run as Administrator.
- The user session running the listener must have rights to set:
  - `HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`

## Files

- `Install-InstantLoginSwitcher.cmd` - one-click installer.
- `Uninstall-InstantLoginSwitcher.cmd` - one-click uninstaller.
- `scripts/Setup-InstantLoginSwitcher.ps1` - core logic called in-memory by the `.cmd` wrappers.
- `scripts/InstantLoginSwitcher.ahk` - listener template script.

## Install (recommended)

1. Download ZIP from GitHub.
2. Unzip anywhere.
3. Install AutoHotkey v2 if needed.
4. Double-click `Install-InstantLoginSwitcher.cmd`.
5. If prompted, right-click and run it as Administrator.
6. Enter the two local account names and passwords.
7. Sign out and back in once.
8. Press **Numpad4 + Numpad5 + Numpad6** together.

## Uninstall

1. Double-click `Uninstall-InstantLoginSwitcher.cmd`.
2. If prompted, right-click and run it as Administrator.

Uninstall removes:

- Scheduled task listener.
- Installed runtime folder at `C:\ProgramData\InstantLoginSwitcher`.
- AutoAdminLogon password value.

## Troubleshooting

- **Black console window appears then disappears**:
  - Use right-click -> **Run as administrator**.
  - Install log: `%TEMP%\InstantLoginSwitcher-install.log`
  - Uninstall log: `%TEMP%\InstantLoginSwitcher-uninstall.log`
- **"Unknown publisher / unknown developer" warning**:
  - Expected for unsigned local scripts. Choose **Run anyway**.
- **PowerShell says script is not digitally signed**:
  - Use the `.cmd` files only.
  - They execute setup logic in-memory and do not require direct `-File` execution of `.ps1`.
- **Hotkey does nothing**:
  - Check task exists:
    - `schtasks /Query /TN "InstantLoginSwitcher-Hotkey-Listener"`
  - Check log file:
    - `C:\ProgramData\InstantLoginSwitcher\switch.log`
  - Confirm AutoHotkey is running:
    - `Get-Process AutoHotkey64`
