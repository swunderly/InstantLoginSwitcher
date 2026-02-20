# InstantLoginSwitcher

One-hotkey local user switching for Windows 11.

## How it works

When you press **Numpad4 + Numpad5 + Numpad6** together:

1. The tool detects which configured user is currently logged in.
2. It sets Windows AutoAdminLogon for the other configured user.
3. It force-logs out the current user.
4. Windows immediately begins signing in the other user.

This is a forced logout + automatic sign-in to the paired account.  
It is not Fast User Switching.

## Requirements

- Windows 11
- AutoHotkey v2 installed
- Local Administrator rights for install/uninstall
- Two local Windows accounts to switch between (both must be in local `Administrators`)

## Install

1. Download this repository ZIP from GitHub.
2. Unzip it anywhere.
3. Install AutoHotkey v2 (if not already installed).
4. Right-click `Install-InstantLoginSwitcher.cmd` and choose **Run as administrator**.
5. Enter the two account names and passwords when prompted.
   Use each account's Windows password, not a PIN.
6. Sign out and sign back in once.
7. Test the hotkey: **Numpad4 + Numpad5 + Numpad6**.

## Uninstall

1. Right-click `Uninstall-InstantLoginSwitcher.cmd` and choose **Run as administrator**.

Uninstall removes:

- Scheduled tasks matching `InstantLoginSwitcher-Hotkey-*`
- Installed runtime files in `C:\ProgramData\InstantLoginSwitcher`
- AutoAdminLogon registry values set by this tool

## Troubleshooting

- Console opens then closes:
  - Run as admin using right-click.
  - Check logs:
    - `%TEMP%\InstantLoginSwitcher-install.log`
    - `%TEMP%\InstantLoginSwitcher-uninstall.log`
- Script signing errors in PowerShell:
  - Use the `.cmd` files above. Do not run `.ps1` directly.
- Hotkey does nothing:
  - Check tasks exist:
    - `schtasks /Query /FO LIST | findstr /I "InstantLoginSwitcher-Hotkey"`
  - Check switch log:
    - `C:\ProgramData\InstantLoginSwitcher\switch.log`
