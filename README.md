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

During save, the app prompts for missing account passwords and validates them.

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

## Multi-Target Chooser UI

If one hotkey maps to multiple valid targets for the current user, the app shows a chooser window so the user can pick the target account.

## Startup Behavior

`Save And Apply` automatically updates per-user startup tasks so listener mode starts at logon.

Listener mode runs the same executable with:

- `--listener`

## Logs

Runtime logs:

- `C:\ProgramData\InstantLoginSwitcher\listener.log`
- `C:\ProgramData\InstantLoginSwitcher\switch.log`
- `C:\ProgramData\InstantLoginSwitcher\config.json`

## Security

Passwords are encrypted with DPAPI (machine scope) before storage in `config.json`.

## Legacy Scripts

The older script-based installer files still exist in this repository, but the Visual Studio application is the primary supported path moving forward.
