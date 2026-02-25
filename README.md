# InstantLoginSwitcher

Configurable hotkey-based local user switching for Windows 11.

## What it does

- Lets you configure one or more switch profiles.
- Each profile links two local administrator accounts to one hotkey.
- Pressing the hotkey prepares auto sign-in for the other user and logs off the current user.
- If a hotkey can switch you to multiple users, a chooser window appears.

This is a forced sign-out plus automatic sign-in flow.
It is not native Fast User Switching.

## Requirements

- Windows 11
- AutoHotkey v2 installed
- Run installer/uninstaller as local Administrator
- Local accounts used for switching must be enabled and in local `Administrators`

## Install

1. Download the repository ZIP from GitHub.
2. Unzip anywhere.
3. Install AutoHotkey v2 (if needed).
4. Right-click `Install-InstantLoginSwitcher.cmd` and choose **Run as administrator**.
5. In the installer prompts:
   - choose how many profiles to create,
   - choose the two users for each profile,
   - choose a hotkey for each profile,
   - enter each selected account password when prompted.
6. Sign out and sign back in once.
7. Test your configured hotkeys.

## Hotkey format

Use plus-separated keys, for example:

- `Numpad4+Numpad5+Numpad6`
- `Ctrl+Alt+S`
- `Shift+F12`

Rules:

- 2 to 4 keys per hotkey
- no duplicate keys inside one hotkey
- at least one key must be a non-modifier key

## Uninstall

1. Right-click `Uninstall-InstantLoginSwitcher.cmd` and choose **Run as administrator**.

Uninstall removes:

- scheduled tasks named `InstantLoginSwitcher-Hotkey-*`
- runtime files in `C:\ProgramData\InstantLoginSwitcher`
- auto-logon registry values set by this tool

## Runtime files

- `C:\ProgramData\InstantLoginSwitcher\config.json`
- `C:\ProgramData\InstantLoginSwitcher\InstantLoginSwitcher.ahk`
- `C:\ProgramData\InstantLoginSwitcher\commands\*.b64`
- `C:\ProgramData\InstantLoginSwitcher\switch.log`

## Troubleshooting

- Installer/uninstaller window closes immediately:
  - Run via right-click -> **Run as administrator**.
  - Check logs:
    - `%TEMP%\InstantLoginSwitcher-install.log`
    - `%TEMP%\InstantLoginSwitcher-uninstall.log`
- Hotkey does nothing:
  - Confirm tasks exist:
    - `schtasks /Query /FO LIST | findstr /I "InstantLoginSwitcher-Hotkey"`
  - Confirm AutoHotkey v2 is installed (v1 is not supported).
  - Check listener log:
    - `C:\ProgramData\InstantLoginSwitcher\listener.log`
  - Check switch log:
    - `C:\ProgramData\InstantLoginSwitcher\switch.log`
- Password validation fails:
  - Use the Windows account password, not PIN.

## Security note

Configured account passwords are stored in reversible format under `C:\ProgramData\InstantLoginSwitcher\config.json` so the switch action can run non-interactively. Only install on systems you trust and keep local admin access restricted.
