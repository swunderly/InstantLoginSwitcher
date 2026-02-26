# InstantLoginSwitcher (Visual Studio App)

InstantLoginSwitcher is now a native Windows 11 application built for Visual Studio (.NET 8 WPF).
It provides hotkey-based switching between local administrator accounts.

When a configured hotkey is pressed:
1. The app prepares auto sign-in for the target user.
2. The current user is logged off.
3. Windows signs in the selected target user.

This is a forced logoff + automatic sign-in workflow.
It is not native Fast User Switching.

## Project Layout

- `InstantLoginSwitcher.sln`
- `src/InstantLoginSwitcher.Core` (config, security, switching, task setup, hotkey parser)
- `src/InstantLoginSwitcher.App` (WPF settings UI + background listener mode)

## Requirements

- Windows 11
- Visual Studio 2022 (17.8+ recommended)
- .NET 8 SDK + desktop workload
- Local accounts used by profiles must be enabled and in local `Administrators`

## Open In Visual Studio

1. Open `InstantLoginSwitcher.sln`.
2. Set startup project to `InstantLoginSwitcher.App`.
3. Build and run.

The app manifest requires administrator rights, so Windows will show UAC.

Test project included:

- `src/InstantLoginSwitcher.Core.Tests`
- Run from Visual Studio Test Explorer or `dotnet test` on Windows.

## How To Configure

1. Launch `InstantLoginSwitcher.App`.
2. Add one or more profiles:
   - `First User`
   - `Second User`
   - `Hotkey` (example `Ctrl+Alt+S`)
3. Click `Add Profile`.
4. Repeat for additional user pairs/hotkeys.
5. Click `Save And Apply`.

The profile form now validates as you type. The `Add Profile`/`Update Profile` button stays disabled until the current form input is valid.
Tip: press `Ctrl+S` in the app to run `Save And Apply`.
Tip: press `Enter` while focused in profile inputs to run `Add Profile`/`Update Profile` when the form is valid.
Tip: press `Esc` while focused in profile inputs to clear the form quickly.
Tip: if your PC has exactly two enabled admin accounts, selecting one user auto-fills the other box.
Hotkeys are normalized automatically when the hotkey field loses focus.
Profile-only actions (`Remove Selected Hotkey`, `Update Passwords`) stay disabled until you select a profile row.
When disabled, those buttons now show a tooltip explaining what is needed.
When you click `Save And Apply` with unsaved form edits, the app now offers to apply the draft first, ignore it, or cancel.

During save, the app prompts for missing account passwords and validates them.
If no enabled profiles remain, save will remove startup tasks and clear auto-logon values.
The form defaults to `Numpad4+Numpad6` when you clear or reset profile input.

Hotkey rules:

- 2 to 4 keys.
- No duplicate key in one hotkey.
- No overlapping key meanings in one hotkey (example: `Delete+NumpadDot` is rejected).
- Must include at least one non-modifier key.
- Key order is normalized automatically (for example `Alt+Ctrl+S` becomes `Ctrl+Alt+S`).
- Generic modifiers (`Ctrl`, `Alt`, `Shift`, `Win`) match both left and right keys.
- Examples: `Ctrl+Alt+S`, `Numpad4+Numpad6`, `Shift+F12`.
- Duplicate profile checks use canonical hotkeys, so `Alt+Ctrl+S` and `Ctrl+Alt+S` are treated as the same combo.

## Three Users Example

If you have users `Alice`, `Bob`, and `Carol`, create multiple profiles:

- `Alice <-> Bob` on `Ctrl+Alt+1`
- `Alice <-> Carol` on `Ctrl+Alt+2`
- `Bob <-> Carol` on `Ctrl+Alt+3` (optional)

## Remove A Hotkey (No Uninstall Needed)

1. Select the profile in `Configured Hotkey Profiles`.
2. Click `Remove Selected Hotkey`.
3. Click `Save And Apply`.

This removes only that profile/hotkey mapping.

## Update Passwords Without Recreating Profiles

If a Windows account password changes:

1. Select a profile that includes that user.
2. Click `Update Passwords For Selected Profile`.
3. Re-enter passwords for the users in that profile.
4. Click `Save And Apply`.

If profile edits are not saved yet, the app now asks you to save first before updating passwords.

## Unsaved Changes Protection

If you close the app with unsaved profile edits, the app now prompts before closing.
If you click `Reload` with unsaved edits, the app also prompts before discarding changes.

## Multi-Target Chooser UI

If one hotkey maps to multiple valid targets for the current user, the app shows a chooser window so the user can pick the target account.

## Startup Behavior

`Save And Apply` automatically updates per-user startup tasks so listener mode starts at logon.
If your signed-in account is not part of any enabled profile, the app now tells you and does not attempt to start a listener for that account.

Listener mode runs the same executable with:

- `--listener`

If listener startup tasks become broken, use `Repair Startup Tasks` in the app.
If there are no enabled profiles, repair will remove startup tasks and clear auto-logon values.

## Logs And Diagnostics

Use the built-in buttons in the main window:

- `Open Data Folder`
- `Open Listener Log`
- `Open Switch Log`
- `Copy Diagnostics` (copies profile/task summary plus recent log tails to clipboard)

Runtime files are in `C:\ProgramData\InstantLoginSwitcher`.
On smaller windows, the action button row is horizontally scrollable.
Diagnostics now include validation issues such as invalid hotkeys or missing stored passwords.
Diagnostics also report the expected startup task name for the current user and whether it exists.

If a hotkey appears to do nothing:

1. Click `Save And Apply`.
2. Confirm the signed-in user is included in at least one enabled profile.
3. Click `Repair Startup Tasks`.
4. Sign out and sign back in.
5. Check `listener.log` for load/trigger errors.

## Publish Build

Use the included script:

- `scripts/Publish-VisualStudioApp.ps1`

Example:

- `powershell -ExecutionPolicy Bypass -File .\\scripts\\Publish-VisualStudioApp.ps1 -Configuration Release -Runtime win-x64 -SelfContained`

## Logs

Runtime logs:

- `C:\ProgramData\InstantLoginSwitcher\listener.log`
- `C:\ProgramData\InstantLoginSwitcher\switch.log`
- `C:\ProgramData\InstantLoginSwitcher\config.json`
- `C:\ProgramData\InstantLoginSwitcher\config.backup.json` (automatic recovery backup)

## Security

Passwords are encrypted with DPAPI (machine scope) before storage in `config.json`.
Auto-logon registry values are now treated as one-shot state and are automatically cleared on next app startup after the switch completes.
In addition, `AutoLogonCount=1` is set during each switch so Windows auto-logon is one-time even if cleanup cannot run.

## Legacy Scripts

The older script-based installer files still exist in this repository, but the Visual Studio application is the primary supported path moving forward.
