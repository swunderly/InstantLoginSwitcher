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
When a new profile creates multiple targets for the same user on the same hotkey, the form now tells you that chooser UI mode will be used.
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
- Existing stored hotkeys are shown in canonical format when reloaded into the editor.

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
The window title shows `*` when there are unsaved edits.

## Multi-Target Chooser UI

If one hotkey maps to multiple valid targets for the current user, the app shows a chooser window so the user can pick the target account.

## Startup Behavior

`Save And Apply` automatically updates per-user startup tasks so listener mode starts at logon.
If your signed-in account is not part of any enabled profile, the app now tells you and does not attempt to start a listener for that account.
When current user is in enabled profiles, `Save And Apply` and `Repair Startup Tasks` now attempt to confirm listener runtime and show a warning if not confirmed.
If task-based listener startup does not confirm quickly, the app now falls back to direct listener start automatically.
`Quick Fix Current User` provides a one-click recovery path for current-user task + listener runtime issues.

Listener mode runs the same executable with:

- `--listener`

If listener startup tasks become broken, use `Repair Startup Tasks` in the app.
If there are no enabled profiles, repair will remove startup tasks and clear auto-logon values.
If unsaved edits exist, the app now warns that repair uses the currently shown profiles.
`Start Listener For Current User` is a manual test shortcut and uses the saved config on disk.

`Remove All Startup Tasks` also warns when unsaved edits exist, because it changes tasks/auto-logon but does not save or delete profile edits.

## Logs And Diagnostics

Use the built-in buttons in the main window:

- `Open Data Folder`
- `Open Config File`
- `Restore Backup Config`
- `Open Listener Log`
- `Open Switch Log`
- `Repair + Check Setup` (repairs startup tasks and immediately runs setup validation)
- `Quick Fix Current User` (repairs startup tasks, starts listener for current user, then runs setup check)
- `Repair Credential Issues` (reprompts and rewrites missing/duplicate/unreadable saved credentials for users in enabled profiles)
- `Start Listener For Current User` (starts listener mode immediately for the signed-in account so you can test without signing out)
- `Check Setup` (flags missing credentials, invalid/duplicate enabled profiles, missing startup tasks per enabled user, stale old tasks, and missing active routes)
- `Refresh Runtime Status` (refreshes live current-user coverage + listener/task summary text at the bottom)
- `Copy Diagnostics` (copies profile/task summary plus recent log tails to clipboard)
- `Save Diagnostics To File` (writes a timestamped diagnostics text file into `C:\ProgramData\InstantLoginSwitcher`)

`Quick Fix Current User` is disabled until you save edits and have at least one enabled profile.
`Repair Credential Issues` is disabled while unsaved edits are present, because it works from saved config on disk.
`Start Listener For Current User` is disabled when no enabled profiles exist.
Quick Fix is also disabled when the signed-in user is not in any enabled profile in the current view.

`Start Listener For Current User` now waits briefly for startup confirmation in `listener.log` and warns if confirmation is not detected.
Listener startup confirmation now checks newly written log content, reducing stale false positives from older log entries.
`Start Listener For Current User` now avoids duplicate launches when possible by trying scheduled task start first, then direct fallback only when needed.
`Start Listener For Current User` now pre-checks whether the current user has any valid hotkey routes and points you to `Check Setup` when none are active.
`Check Setup` warns when unsaved UI edits are present because it validates the saved config on disk.
`Check Setup` now also flags when the current user should have listener coverage but the listener process is not currently running.
`Check Setup` now flags when current user is in enabled profiles but has no valid hotkey routes.
`Check Setup` now flags unreadable/corrupt saved password entries and duplicate credential entries.
`Save And Apply` now automatically re-prompts for any unreadable saved password entries instead of silently keeping broken credentials.
At switch time, if duplicate credential entries exist for a user, the app now uses the first readable one and reports failures more clearly.

Runtime files are in `C:\ProgramData\InstantLoginSwitcher`.
On smaller windows, the action button row is horizontally scrollable.
The bottom runtime summary line shows current-user coverage, listener state, and startup-task presence.
The runtime summary is color-coded (green = healthy, amber = warning, red = error); hover it for detailed route/task diagnostics.
When unsaved form edits exist, runtime summary now labels that diagnostics are showing saved-state behavior.
With unsaved edits, runtime summary also includes a draft preview (enabled profile count, current-user coverage, hotkey preview, invalid draft hotkeys).
Diagnostics now include validation issues such as invalid hotkeys or missing stored passwords.
Diagnostics also report the expected startup task name for the current user and whether it exists.
Diagnostics now include `ExpectedTasksByUser` and `UnexpectedStartupTasks` sections for faster task troubleshooting.
Diagnostics include chooser-route summaries so you can see where one hotkey opens a target picker.
Diagnostics include `ActiveHotkeysByUser` so you can confirm each user has switchable routes.
Diagnostics now include `CredentialHealth` and `CredentialReadFailures` so corrupt saved passwords are surfaced quickly.
If clipboard copy fails, `Copy Diagnostics` now falls back to saving a diagnostics file automatically.
Diagnostics include an internal errors section if any data source (for example task query) fails.
Diagnostics include config/backup file existence and last-write timestamps.
Diagnostics include whether unsaved UI edits were present when the report was generated.
Diagnostics include whether the current-user listener mutex is currently present.
`Restore Backup Config` is enabled only when a backup file exists.

If a hotkey appears to do nothing:

1. Click `Save And Apply`.
2. Confirm the signed-in user is included in at least one enabled profile.
3. Click `Quick Fix Current User`.
4. Click `Check Setup` and confirm it does not report "no valid hotkey routes" for the current user.
5. If needed, click `Start Listener For Current User` to test listener start directly.
6. If still needed, sign out and sign back in.
7. Open `listener.log` and `switch.log` (or use `Save Diagnostics To File`) and review the latest entries.

If `Save And Apply` reports startup-task failure, your profile changes are still saved; use `Quick Fix Current User` or `Repair + Check Setup` to retry task registration.

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

Advanced override:

- Set environment variable `INSTANT_LOGIN_SWITCHER_ROOT` to relocate the data/log folder.
- Diagnostics output includes the effective override value.

## Security

Passwords are encrypted with DPAPI (machine scope) before storage in `config.json`.
Legacy `B64:` password entries from older script-based installs are still readable and are rewritten in DPAPI format on next save.
Auto-logon registry values are now treated as one-shot state and are automatically cleared on next app startup after the switch completes.
In addition, `AutoLogonCount=1` is set during each switch so Windows auto-logon is one-time even if cleanup cannot run.

## Legacy Scripts

The older script-based installer files still exist in this repository, but the Visual Studio application is the primary supported path moving forward.
