param(
    [ValidateSet('Install', 'Uninstall')]
    [string]$Mode = 'Install',
    [string]$DefaultPrimaryUser,
    [string]$DefaultSecondaryUser,
    [string]$DefaultHotkey = 'Numpad4+Numpad5+Numpad6'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramData 'InstantLoginSwitcher'
$configPath = Join-Path $installDir 'config.json'
$commandsDir = Join-Path $installDir 'commands'
$listenerScriptPath = Join-Path $installDir 'InstantLoginSwitcher.ahk'
$listenerTaskPrefix = 'InstantLoginSwitcher-Hotkey'
$legacyListenerTaskName = 'InstantLoginSwitcher-Hotkey-Listener'
$localAdministratorsSid = 'S-1-5-32-544'
$defaultSwitchMode = 'Logoff'

if ([string]::IsNullOrWhiteSpace($DefaultHotkey)) {
    $DefaultHotkey = 'Numpad4+Numpad5+Numpad6'
}

$hotkeyAliases = @{
    'CTRL'        = 'Ctrl'
    'CONTROL'     = 'Ctrl'
    'LCONTROL'    = 'LCtrl'
    'RCONTROL'    = 'RCtrl'
    'LCTRL'       = 'LCtrl'
    'RCTRL'       = 'RCtrl'
    'ALT'         = 'Alt'
    'LALT'        = 'LAlt'
    'RALT'        = 'RAlt'
    'SHIFT'       = 'Shift'
    'LSHIFT'      = 'LShift'
    'RSHIFT'      = 'RShift'
    'WIN'         = 'LWin'
    'WINDOWS'     = 'LWin'
    'LWIN'        = 'LWin'
    'RWIN'        = 'RWin'
    'ENTER'       = 'Enter'
    'RETURN'      = 'Enter'
    'ESC'         = 'Escape'
    'ESCAPE'      = 'Escape'
    'SPACE'       = 'Space'
    'SPACEBAR'    = 'Space'
    'TAB'         = 'Tab'
    'BACKSPACE'   = 'Backspace'
    'BS'          = 'Backspace'
    'DELETE'      = 'Delete'
    'DEL'         = 'Delete'
    'INSERT'      = 'Insert'
    'INS'         = 'Insert'
    'HOME'        = 'Home'
    'END'         = 'End'
    'PGUP'        = 'PgUp'
    'PAGEUP'      = 'PgUp'
    'PGDN'        = 'PgDn'
    'PAGEDOWN'    = 'PgDn'
    'UP'          = 'Up'
    'DOWN'        = 'Down'
    'LEFT'        = 'Left'
    'RIGHT'       = 'Right'
    'NUMLOCK'     = 'NumLock'
    'SCROLLLOCK'  = 'ScrollLock'
    'CAPSLOCK'    = 'CapsLock'
    'PRINTSCREEN' = 'PrintScreen'
    'PRTSC'       = 'PrintScreen'
    'PAUSE'       = 'Pause'
    'BREAK'       = 'Pause'
    'NUMPADADD'   = 'NumpadAdd'
    'NUMPADSUB'   = 'NumpadSub'
    'NUMPADMULT'  = 'NumpadMult'
    'NUMPADDIV'   = 'NumpadDiv'
    'NUMPADDOT'   = 'NumpadDot'
    'NUMPADDEL'   = 'NumpadDot'
}

$modifierKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($modifier in @(
    'Ctrl', 'LCtrl', 'RCtrl',
    'Alt', 'LAlt', 'RAlt',
    'Shift', 'LShift', 'RShift',
    'LWin', 'RWin'
)) {
    [void]$modifierKeys.Add($modifier)
}

function Test-Admin {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-AutoHotkeyV2Executable {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $probeScriptPath = Join-Path $env:TEMP ("ils-ahk-probe-{0}.ahk" -f ([System.Guid]::NewGuid().ToString('N')))
    $probeContent = "#Requires AutoHotkey v2.0`r`nExitApp`r`n"
    $encoding = New-Object System.Text.UTF8Encoding($false)

    try {
        [System.IO.File]::WriteAllText($probeScriptPath, $probeContent, $encoding)
        $probeProcess = Start-Process -FilePath $Path -ArgumentList ('"{0}"' -f $probeScriptPath) -WindowStyle Hidden -Wait -PassThru -ErrorAction Stop
        return ($probeProcess.ExitCode -eq 0)
    }
    catch {
        return $false
    }
    finally {
        Remove-Item -LiteralPath $probeScriptPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-AutoHotkeyExecutable {
    $roots = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $roots += $env:ProgramFiles
    }
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $roots += ${env:ProgramFiles(x86)}
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $roots += (Join-Path $env:LOCALAPPDATA 'Programs')
    }

    foreach ($root in $roots | Select-Object -Unique) {
        foreach ($relative in @(
            'AutoHotkey\v2\AutoHotkey64.exe',
            'AutoHotkey\v2\AutoHotkey.exe',
            'AutoHotkey\AutoHotkey64.exe',
            'AutoHotkey\AutoHotkey.exe'
        )) {
            $candidate = Join-Path $root $relative
            if ((Test-Path -LiteralPath $candidate) -and (Test-AutoHotkeyV2Executable -Path $candidate)) {
                return $candidate
            }
        }
    }

    foreach ($commandName in @('AutoHotkey64.exe', 'AutoHotkey.exe')) {
        $command = Get-Command -Name $commandName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command -and (Test-Path -LiteralPath $command.Source) -and (Test-AutoHotkeyV2Executable -Path $command.Source)) {
            return $command.Source
        }
    }

    return $null
}

function Get-LocalAdministratorUserNames {
    try {
        $adminsGroup = Get-LocalGroup -ErrorAction Stop |
            Where-Object { $_.SID -and $_.SID.Value -eq $localAdministratorsSid } |
            Select-Object -First 1

        if (-not $adminsGroup) {
            throw "Could not resolve local Administrators group from SID $localAdministratorsSid."
        }

        $admins = Get-LocalGroupMember -Group $adminsGroup.Name -ErrorAction Stop
    }
    catch {
        throw "Could not read local Administrators group membership: $($_.Exception.Message)"
    }

    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($member in $admins) {
        $name = [string]$member.Name
        if ([string]::IsNullOrWhiteSpace($name)) {
            continue
        }

        $shortName = ($name -split '\\')[-1]
        if (-not [string]::IsNullOrWhiteSpace($shortName)) {
            [void]$names.Add($shortName)
        }
    }

    return $names
}

function Resolve-LocalAccount {
    param(
        [Parameter(Mandatory)][string]$InputName,
        [Parameter(Mandatory)][System.Collections.IEnumerable]$LocalUsers
    )

    $machine = $env:COMPUTERNAME
    $candidate = $InputName.Trim()

    if ($candidate.Contains('\\')) {
        $candidate = $candidate.Split('\\', 2)[1]
    }

    $matched = $LocalUsers | Where-Object { $_.Name -ieq $candidate } | Select-Object -First 1
    if (-not $matched) {
        $matched = $LocalUsers | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.FullName) -and $_.FullName -ieq $candidate
        } | Select-Object -First 1
    }

    if (-not $matched) {
        throw "Could not find a local account matching '$InputName'. Use one of the listed local administrator account names."
    }

    if (-not $matched.Enabled) {
        throw "Local account '$($matched.Name)' is disabled. Enable it before configuring InstantLoginSwitcher."
    }

    [pscustomobject]@{
        InputName = $InputName
        UserName  = [string]$matched.Name
        FullName  = [string]$matched.FullName
        Qualified = "$machine\$($matched.Name)"
        SidValue  = if ($matched.SID) { [string]$matched.SID.Value } else { $null }
    }
}

function Assert-LocalAdministratorAccount {
    param(
        [Parameter(Mandatory)][string]$UserName,
        [Parameter(Mandatory)][System.Collections.Generic.HashSet[string]]$AdminUserNames
    )

    if (-not $AdminUserNames.Contains($UserName)) {
        throw "Account '$env:COMPUTERNAME\$UserName' is not in local Administrators. All configured switch users must be administrators."
    }
}

function Convert-SecureStringToPlainText {
    param([Parameter(Mandatory)][SecureString]$SecureString)

    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        return [System.Runtime.InteropServices.Marshal]::PtrToStringUni($bstr)
    }
    finally {
        if ($bstr -ne [System.IntPtr]::Zero) {
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

function Test-LocalCredential {
    param(
        [Parameter(Mandatory)][string]$QualifiedUser,
        [Parameter(Mandatory)][string]$Password
    )

    if ([string]::IsNullOrWhiteSpace($Password)) {
        return [pscustomobject]@{
            Success    = $false
            Win32Error = 0
        }
    }

    $parts = $QualifiedUser.Split('\\', 2)
    if ($parts.Count -ne 2) {
        throw "Invalid qualified user value '$QualifiedUser'. Expected COMPUTERNAME\\UserName."
    }

    if (-not ('InstantLoginSwitcher.NativeMethods' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace InstantLoginSwitcher {
    public static class NativeMethods {
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            string lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out IntPtr phToken
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
'@ -ErrorAction Stop
    }

    $token = [System.IntPtr]::Zero
    $success = [InstantLoginSwitcher.NativeMethods]::LogonUser(
        $parts[1],
        $parts[0],
        $Password,
        2,
        0,
        [ref]$token
    )

    $win32Error = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()

    if ($token -ne [System.IntPtr]::Zero) {
        [InstantLoginSwitcher.NativeMethods]::CloseHandle($token) | Out-Null
    }

    return [pscustomobject]@{
        Success    = $success
        Win32Error = $win32Error
    }
}

function Protect-PlainTextLocalMachine {
    param([Parameter(Mandatory)][string]$PlainText)

    $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($PlainText)
    return 'B64:' + [System.Convert]::ToBase64String($plainBytes)
}

function ConvertTo-SingleQuotedLiteral {
    param([Parameter(Mandatory)][string]$Value)
    return "'" + ($Value -replace "'", "''") + "'"
}

function ConvertTo-AhkStringLiteral {
    param([Parameter(Mandatory)][string]$Value)
    return '"' + ($Value -replace '"', '""') + '"'
}

function Normalize-HotkeyToken {
    param([Parameter(Mandatory)][string]$Token)

    $trimmed = $Token.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        throw 'Hotkey tokens cannot be blank.'
    }

    $upper = $trimmed.ToUpperInvariant()

    if ($hotkeyAliases.ContainsKey($upper)) {
        return $hotkeyAliases[$upper]
    }

    if ($upper -match '^[A-Z]$') {
        return $upper
    }

    if ($upper -match '^[0-9]$') {
        return $upper
    }

    if ($upper -match '^F([1-9]|1[0-9]|2[0-4])$') {
        return $upper
    }

    if ($upper -match '^NUMPAD([0-9])$') {
        return "Numpad$($Matches[1])"
    }

    throw "Unsupported hotkey token '$Token'. Use letters, digits, F-keys, numpad keys, arrows, and standard modifiers."
}

function ConvertFrom-HotkeyText {
    param([Parameter(Mandatory)][string]$InputText)

    if ([string]::IsNullOrWhiteSpace($InputText)) {
        throw 'Hotkey cannot be blank.'
    }

    $parts = @($InputText.Split('+') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })

    if ($parts.Count -lt 2) {
        throw 'Hotkey must include at least two keys separated by + (example: Ctrl+Alt+S).'
    }

    if ($parts.Count -gt 4) {
        throw 'Hotkey can include at most four keys.'
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $keys = New-Object System.Collections.Generic.List[string]

    foreach ($part in $parts) {
        $normalized = Normalize-HotkeyToken -Token $part
        if (-not $seen.Add($normalized)) {
            throw "Hotkey contains duplicate key '$normalized'."
        }

        [void]$keys.Add($normalized)
    }

    $hasNonModifier = $false
    foreach ($key in $keys) {
        if (-not $modifierKeys.Contains($key)) {
            $hasNonModifier = $true
            break
        }
    }

    if (-not $hasNonModifier) {
        throw 'Hotkey must include at least one non-modifier key (for example: Ctrl+Alt+S).'
    }

    return [pscustomobject]@{
        Canonical = ($keys -join '+')
        Keys      = @($keys)
    }
}

function Read-ProfileCount {
    while ($true) {
        $inputValue = Read-Host 'How many switch profiles do you want to configure? [1]'
        if ([string]::IsNullOrWhiteSpace($inputValue)) {
            return 1
        }

        $parsed = 0
        if (-not [int]::TryParse($inputValue, [ref]$parsed)) {
            Write-Host 'Enter a whole number (for example: 1, 2, 3).' -ForegroundColor Yellow
            continue
        }

        if ($parsed -lt 1 -or $parsed -gt 20) {
            Write-Host 'Enter a value from 1 to 20.' -ForegroundColor Yellow
            continue
        }

        return $parsed
    }
}

function Read-LocalAccountFromPrompt {
    param(
        [Parameter(Mandatory)][string]$PromptText,
        [string]$DefaultValue,
        [Parameter(Mandatory)][System.Collections.IEnumerable]$LocalUsers,
        [Parameter(Mandatory)][System.Collections.Generic.HashSet[string]]$AdminUserNames
    )

    while ($true) {
        $prompt = if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
            $PromptText
        }
        else {
            "$PromptText [$DefaultValue]"
        }

        $inputValue = Read-Host $prompt
        if ([string]::IsNullOrWhiteSpace($inputValue)) {
            if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
                Write-Host 'Value is required.' -ForegroundColor Yellow
                continue
            }

            $inputValue = $DefaultValue
        }

        try {
            $account = Resolve-LocalAccount -InputName $inputValue -LocalUsers $LocalUsers
            Assert-LocalAdministratorAccount -UserName $account.UserName -AdminUserNames $AdminUserNames
            return $account
        }
        catch {
            Write-Host $_.Exception.Message -ForegroundColor Yellow
        }
    }
}

function Read-HotkeyFromPrompt {
    param([Parameter(Mandatory)][string]$DefaultValue)

    while ($true) {
        $inputValue = Read-Host "Hotkey (plus-separated keys, example Ctrl+Alt+S) [$DefaultValue]"
        if ([string]::IsNullOrWhiteSpace($inputValue)) {
            $inputValue = $DefaultValue
        }

        try {
            return ConvertFrom-HotkeyText -InputText $inputValue
        }
        catch {
            Write-Host $_.Exception.Message -ForegroundColor Yellow
        }
    }
}

function Get-UserPicturePath {
    param(
        [string]$SidValue,
        [Parameter(Mandatory)][string]$UserName
    )

    if (-not [string]::IsNullOrWhiteSpace($SidValue)) {
        try {
            $profile = Get-CimInstance Win32_UserProfile -Filter ("SID='{0}'" -f $SidValue) -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($profile -and -not [string]::IsNullOrWhiteSpace([string]$profile.LocalPath)) {
                $accountPictureDir = Join-Path ([string]$profile.LocalPath) 'AppData\Roaming\Microsoft\Windows\AccountPictures'
                if (Test-Path -LiteralPath $accountPictureDir) {
                    $picture = Get-ChildItem -LiteralPath $accountPictureDir -File -ErrorAction SilentlyContinue |
                        Where-Object { $_.Extension -match '^\.(png|jpg|jpeg|bmp)$' } |
                        Sort-Object LastWriteTime -Descending |
                        Select-Object -First 1
                    if ($picture) {
                        return $picture.FullName
                    }
                }
            }
        }
        catch {
        }
    }

    foreach ($extension in @('png', 'jpg', 'jpeg', 'bmp')) {
        $candidate = Join-Path $env:ProgramData ("Microsoft\User Account Pictures\{0}.{1}" -f $UserName, $extension)
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $fallback = Join-Path $env:ProgramData 'Microsoft\User Account Pictures\user.png'
    if (Test-Path -LiteralPath $fallback) {
        return $fallback
    }

    return $null
}

function New-ListenerTaskName {
    param(
        [string]$SidValue,
        [Parameter(Mandatory)][string]$UserName
    )

    $nameSource = if ([string]::IsNullOrWhiteSpace($SidValue)) { $UserName } else { $SidValue }
    $cleanSid = ($nameSource -replace '[^A-Za-z0-9\-]', '_')
    return "$listenerTaskPrefix-$cleanSid"
}

function New-EncodedSwitchCommand {
    param(
        [Parameter(Mandatory)][string]$ConfigFilePath,
        [Parameter(Mandatory)][string]$HotkeyId
    )

    $configLiteral = ConvertTo-SingleQuotedLiteral -Value $ConfigFilePath
    $hotkeyLiteral = ConvertTo-SingleQuotedLiteral -Value $HotkeyId

    $template = @'
$ErrorActionPreference = 'Stop'
$configPath = __CONFIG_PATH__
$hotkeyId = __HOTKEY_ID__
$baseDir = [System.IO.Path]::GetDirectoryName($configPath)
$logPath = Join-Path $baseDir 'switch.log'

function Write-Log([string]$Message) {
    try {
        $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        Add-Content -LiteralPath $logPath -Value "$timestamp $Message" -Encoding UTF8
    }
    catch {
    }
}

function Assert-Admin {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Hotkey listener is not running elevated.'
    }
}

function Unprotect-Secret([string]$CipherText) {
    if ([string]::IsNullOrWhiteSpace($CipherText)) {
        return ''
    }

    $payload = $CipherText
    if ($CipherText.StartsWith('B64:')) {
        $payload = $CipherText.Substring(4)
    }

    $plainBytes = [System.Convert]::FromBase64String($payload)
    return [System.Text.Encoding]::UTF8.GetString($plainBytes)
}

function Get-UserRecord([object]$Config, [string]$UserName) {
    foreach ($entry in @($Config.Users)) {
        if ([string]$entry.UserName -ieq $UserName) {
            return $entry
        }
    }

    return $null
}

function Select-TargetFromUi([array]$Candidates) {
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
    }
    catch {
        Write-Log ('UI assemblies unavailable: ' + $_.Exception.Message)
        return [pscustomobject]@{
            Status   = 'Unavailable'
            UserName = $null
        }
    }

    $script:selectedUser = $null
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Choose Account'
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
    $form.Width = 520
    $form.Height = 380
    $form.TopMost = $true

    $flow = New-Object System.Windows.Forms.FlowLayoutPanel
    $flow.Dock = [System.Windows.Forms.DockStyle]::Top
    $flow.FlowDirection = [System.Windows.Forms.FlowDirection]::TopDown
    $flow.WrapContents = $false
    $flow.AutoScroll = $true
    $flow.Width = 500
    $flow.Height = 300

    foreach ($candidate in $Candidates) {
        $button = New-Object System.Windows.Forms.Button
        $button.Width = 470
        $button.Height = 72

        $displayName = [string]$candidate.FullName
        if ([string]::IsNullOrWhiteSpace($displayName)) {
            $displayName = [string]$candidate.UserName
        }

        $button.Text = "$displayName`r`n$([string]$candidate.Qualified)"
        $button.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft

        $picturePath = [string]$candidate.PicturePath
        if (-not [string]::IsNullOrWhiteSpace($picturePath) -and (Test-Path -LiteralPath $picturePath)) {
            try {
                $image = [System.Drawing.Image]::FromFile($picturePath)
                $button.Image = $image
                $button.ImageAlign = [System.Drawing.ContentAlignment]::MiddleLeft
                $button.TextImageRelation = [System.Windows.Forms.TextImageRelation]::ImageBeforeText
            }
            catch {
            }
        }

        $button.Tag = [string]$candidate.UserName
        $button.Add_Click({
            param($sender, $eventArgs)
            $script:selectedUser = [string]$sender.Tag
            $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
            $form.Close()
        })

        [void]$flow.Controls.Add($button)
    }

    $cancelButton = New-Object System.Windows.Forms.Button
    $cancelButton.Text = 'Cancel'
    $cancelButton.Width = 100
    $cancelButton.Height = 32
    $cancelButton.Top = 305
    $cancelButton.Left = 390
    $cancelButton.Add_Click({
        $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
        $form.Close()
    })

    [void]$form.Controls.Add($flow)
    [void]$form.Controls.Add($cancelButton)
    [void]$form.ShowDialog()

    if ([string]::IsNullOrWhiteSpace($script:selectedUser)) {
        return [pscustomobject]@{
            Status   = 'Cancelled'
            UserName = $null
        }
    }

    return [pscustomobject]@{
        Status   = 'Selected'
        UserName = $script:selectedUser
    }
}

try {
    Assert-Admin

    if (-not (Test-Path -LiteralPath $configPath)) {
        throw "Missing configuration file: $configPath"
    }

    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $currentUser = [System.Environment]::UserName

    $profiles = @($config.Profiles | Where-Object { [string]$_.HotkeyId -eq $hotkeyId })
    if ($profiles.Count -eq 0) {
        throw "No profile is mapped to hotkey id '$hotkeyId'."
    }

    $candidateMap = @{}
    foreach ($profile in $profiles) {
        $userA = [string]$profile.UserA
        $userB = [string]$profile.UserB

        if ($currentUser -ieq $userA) {
            $targetUser = $userB
        }
        elseif ($currentUser -ieq $userB) {
            $targetUser = $userA
        }
        else {
            continue
        }

        if (-not $candidateMap.ContainsKey($targetUser)) {
            $record = Get-UserRecord -Config $config -UserName $targetUser
            if ($record) {
                $candidateMap[$targetUser] = $record
            }
        }
    }

    $candidates = @($candidateMap.GetEnumerator() | ForEach-Object { $_.Value } | Sort-Object UserName)
    if ($candidates.Count -eq 0) {
        throw "Current user '$currentUser' is not part of any profile for hotkey id '$hotkeyId'."
    }

    if ($candidates.Count -eq 1) {
        $targetRecord = $candidates[0]
    }
    else {
        $uiResult = Select-TargetFromUi -Candidates $candidates
        if ([string]$uiResult.Status -eq 'Unavailable') {
            $targetRecord = $candidates[0]
            Write-Log ("Chooser UI unavailable; falling back to first target '{0}'." -f [string]$targetRecord.UserName)
        }
        elseif ([string]$uiResult.Status -eq 'Cancelled') {
            Write-Log 'Switch cancelled from chooser UI.'
            return
        }
        else {
            $targetRecord = Get-UserRecord -Config $config -UserName ([string]$uiResult.UserName)
        }
        if (-not $targetRecord) {
            throw "UI selected unknown user '$([string]$uiResult.UserName)'."
        }
    }

    $targetUser = [string]$targetRecord.UserName
    $password = Unprotect-Secret -CipherText ([string]$targetRecord.PasswordEnc)
    if ([string]::IsNullOrWhiteSpace($password)) {
        throw "Target password for '$targetUser' could not be decrypted."
    }

    $targetSid = [string]$targetRecord.SidValue
    $domain = $env:COMPUTERNAME
    $winlogonPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'

    Set-ItemProperty -Path $winlogonPath -Name 'AutoAdminLogon' -Type String -Value '1'
    Set-ItemProperty -Path $winlogonPath -Name 'ForceAutoLogon' -Type String -Value '1'
    Set-ItemProperty -Path $winlogonPath -Name 'DefaultUserName' -Type String -Value $targetUser
    Set-ItemProperty -Path $winlogonPath -Name 'DefaultPassword' -Type String -Value $password
    Set-ItemProperty -Path $winlogonPath -Name 'DefaultDomainName' -Type String -Value $domain
    Set-ItemProperty -Path $winlogonPath -Name 'AltDefaultUserName' -Type String -Value $targetUser
    Set-ItemProperty -Path $winlogonPath -Name 'AltDefaultDomainName' -Type String -Value $domain
    Set-ItemProperty -Path $winlogonPath -Name 'LastUsedUsername' -Type String -Value ($domain + '\' + $targetUser)

    if (-not [string]::IsNullOrWhiteSpace($targetSid)) {
        Set-ItemProperty -Path $winlogonPath -Name 'AutoLogonSID' -Type String -Value $targetSid
    }
    else {
        Remove-ItemProperty -Path $winlogonPath -Name 'AutoLogonSID' -ErrorAction SilentlyContinue
    }

    Remove-ItemProperty -Path $winlogonPath -Name 'AutoLogonCount' -ErrorAction SilentlyContinue

    $switchMode = if ($config.SwitchMode) { [string]$config.SwitchMode } else { 'Logoff' }
    if ($switchMode -ieq 'Restart') {
        $shutdownArgs = '/r /f /t 0'
        $switchMode = 'Restart'
    }
    else {
        $shutdownArgs = '/l /f'
        $switchMode = 'Logoff'
    }

    Write-Log "Prepared auto sign-in for '$targetUser' (triggered by '$currentUser', hotkey id '$hotkeyId')."
    Write-Log "Switch action: $switchMode ($shutdownArgs)"

    Start-Sleep -Milliseconds 150
    Start-Process -FilePath shutdown.exe -ArgumentList $shutdownArgs -WindowStyle Hidden
}
catch {
    Write-Log ('ERROR: ' + $_.Exception.Message)
}
'@

    $script = $template.Replace('__CONFIG_PATH__', $configLiteral).Replace('__HOTKEY_ID__', $hotkeyLiteral)
    $bytes = [System.Text.Encoding]::Unicode.GetBytes($script)
    return [System.Convert]::ToBase64String($bytes)
}

function Write-Utf8NoBomFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Write-ListenerScript {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][array]$Hotkeys
    )

    $sortedHotkeys = @($Hotkeys | Sort-Object { -1 * $_.Keys.Count })

    $comboEntries = foreach ($hotkey in $sortedHotkeys) {
        $keyLiterals = @($hotkey.Keys | ForEach-Object { ConvertTo-AhkStringLiteral -Value $_ }) -join ', '
        ('    Map("id", {0}, "keys", Array({1}), "triggered", false)' -f (ConvertTo-AhkStringLiteral -Value $hotkey.HotkeyId), $keyLiterals)
    }

    $commandEntries = foreach ($hotkey in $sortedHotkeys) {
        ('    {0}, commandsDir . "\{1}.b64"' -f (ConvertTo-AhkStringLiteral -Value $hotkey.HotkeyId), $hotkey.HotkeyId)
    }

    $comboBlock = [string]::Join(",`r`n", $comboEntries)
    $commandBlock = [string]::Join(",`r`n", $commandEntries)

$content = @"
#Requires AutoHotkey v2.0
#SingleInstance Force

commandsDir := A_ScriptDir . "\commands"
logPath := A_ScriptDir . "\listener.log"
switchInProgress := false

combos := Array(
$comboBlock
)

commandMap := Map(
$commandBlock
)

WriteLog("Listener started. combos=" . combos.Length)
SetTimer(CheckCombos, 35)

CheckCombos(*) {
    global combos

    for combo in combos {
        if combo["triggered"] {
            if !IsAnyKeyDown(combo["keys"]) {
                combo["triggered"] := false
            }
            continue
        }

        if IsComboPressed(combo["keys"]) {
            combo["triggered"] := true
            RunSwitch(combo["id"])
            return
        }
    }
}

IsComboPressed(keys) {
    for keyName in keys {
        if !IsKeyPressed(keyName) {
            return false
        }
    }

    return true
}

IsAnyKeyDown(keys) {
    for keyName in keys {
        if IsKeyPressed(keyName) {
            return true
        }
    }

    return false
}

IsKeyPressed(keyName) {
    if GetKeyState(keyName, "P") {
        return true
    }

    altKey := ""
    switch keyName {
        case "Numpad0":
            altKey := "NumpadIns"
        case "Numpad1":
            altKey := "NumpadEnd"
        case "Numpad2":
            altKey := "NumpadDown"
        case "Numpad3":
            altKey := "NumpadPgDn"
        case "Numpad4":
            altKey := "NumpadLeft"
        case "Numpad5":
            altKey := "NumpadClear"
        case "Numpad6":
            altKey := "NumpadRight"
        case "Numpad7":
            altKey := "NumpadHome"
        case "Numpad8":
            altKey := "NumpadUp"
        case "Numpad9":
            altKey := "NumpadPgUp"
        case "NumpadDot":
            altKey := "NumpadDel"
    }

    if (altKey != "" && GetKeyState(altKey, "P")) {
        return true
    }

    return false
}

ResetSwitchGuard(*) {
    global switchInProgress
    switchInProgress := false
}

WriteLog(message) {
    global logPath

    try {
        timestamp := FormatTime(A_Now, "yyyy-MM-dd HH:mm:ss")
        FileAppend(timestamp . " " . message . "`r`n", logPath, "UTF-8")
    }
    catch {
    }
}

RunSwitch(hotkeyId) {
    global commandMap, switchInProgress

    if switchInProgress {
        return
    }

    if !commandMap.Has(hotkeyId) {
        WriteLog("Missing command map entry for " . hotkeyId)
        return
    }

    encodedPath := commandMap[hotkeyId]
    if !FileExist(encodedPath) {
        WriteLog("Command file missing: " . encodedPath)
        return
    }

    encoded := Trim(FileRead(encodedPath, "UTF-8"))
    if (encoded = "") {
        WriteLog("Command file empty: " . encodedPath)
        return
    }

    if (SubStr(encoded, 1, 1) = Chr(0xFEFF)) {
        encoded := SubStr(encoded, 2)
    }

    switchInProgress := true
    SetTimer(ResetSwitchGuard, -3000)
    WriteLog("Triggering switch for " . hotkeyId)

    psExe := A_WinDir . "\System32\WindowsPowerShell\v1.0\powershell.exe"
    command := '"' . psExe . '" -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -STA -EncodedCommand ' . encoded

    try {
        Run(command, , "Hide")
    }
    catch {
        WriteLog("Run failed for " . hotkeyId)
        ResetSwitchGuard()
    }
}
"@

    Write-Utf8NoBomFile -Path $Path -Content $content
}

function Stop-ListenerProcesses {
    param([Parameter(Mandatory)][string]$ScriptPath)

    $scriptName = [System.IO.Path]::GetFileName($ScriptPath)
    $scriptPattern = [System.Text.RegularExpressions.Regex]::Escape($scriptName)
    $fullPathPattern = $null

    if (Test-Path -LiteralPath $ScriptPath) {
        $fullPathPattern = [System.Text.RegularExpressions.Regex]::Escape([System.IO.Path]::GetFullPath($ScriptPath))
    }

    $processes = Get-CimInstance Win32_Process -Filter "Name LIKE 'AutoHotkey%.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and (
                ($fullPathPattern -and $_.CommandLine -match $fullPathPattern) -or
                $_.CommandLine -match $scriptPattern
            )
        }

    foreach ($proc in $processes) {
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Test-ListenerProcessRunning {
    param([Parameter(Mandatory)][string]$ScriptPath)

    $scriptName = [System.IO.Path]::GetFileName($ScriptPath)
    $scriptPattern = [System.Text.RegularExpressions.Regex]::Escape($scriptName)
    $fullPathPattern = $null

    if (Test-Path -LiteralPath $ScriptPath) {
        $fullPathPattern = [System.Text.RegularExpressions.Regex]::Escape([System.IO.Path]::GetFullPath($ScriptPath))
    }

    $process = Get-CimInstance Win32_Process -Filter "Name LIKE 'AutoHotkey%.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and (
                ($fullPathPattern -and $_.CommandLine -match $fullPathPattern) -or
                $_.CommandLine -match $scriptPattern
            )
        } |
        Select-Object -First 1

    return ($null -ne $process)
}

function Remove-TaskIfExists {
    param([Parameter(Mandatory)][string]$TaskName)

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $task) {
        return
    }

    try {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }
    catch {
    }

    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

function Disable-AutoAdminLogon {
    $path = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
    if (-not (Test-Path -LiteralPath $path)) {
        return
    }

    Set-ItemProperty -Path $path -Name 'AutoAdminLogon' -Type String -Value '0' -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $path -Name 'ForceAutoLogon' -Type String -Value '0' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'DefaultPassword' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'DefaultUserName' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'DefaultDomainName' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'AltDefaultUserName' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'AltDefaultDomainName' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'AutoLogonSID' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'AutoLogonCount' -ErrorAction SilentlyContinue
}

function Register-ListenerTask {
    param(
        [Parameter(Mandatory)][string]$TaskName,
        [Parameter(Mandatory)][string]$AutoHotkeyExe,
        [Parameter(Mandatory)][string]$ListenerScriptPath,
        [Parameter(Mandatory)][string]$UserId
    )

    $action = New-ScheduledTaskAction -Execute $AutoHotkeyExe -Argument ('"{0}"' -f $ListenerScriptPath)
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $UserId
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

    $principal = New-ScheduledTaskPrincipal -UserId $UserId -LogonType Interactive -RunLevel Highest

    Remove-TaskIfExists -TaskName $TaskName
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force -ErrorAction Stop | Out-Null
}

if (-not (Test-Admin)) {
    throw 'Run as Administrator.'
}

if ($Mode -eq 'Uninstall') {
    Stop-ListenerProcesses -ScriptPath $listenerScriptPath

    $taskNames = @($legacyListenerTaskName)
    $taskNames += (Get-ScheduledTask -ErrorAction SilentlyContinue |
        Where-Object { $_.TaskName -like "$listenerTaskPrefix-*" } |
        Select-Object -ExpandProperty TaskName)

    foreach ($taskName in $taskNames | Select-Object -Unique) {
        Remove-TaskIfExists -TaskName $taskName
    }

    Disable-AutoAdminLogon

    if (Test-Path -LiteralPath $installDir) {
        Remove-Item -LiteralPath $installDir -Recurse -Force
    }

    Write-Host 'InstantLoginSwitcher removed.'
    return
}

$ahkExe = Get-AutoHotkeyExecutable
if (-not $ahkExe) {
    throw 'AutoHotkey v2 was not found. Install AutoHotkey v2 first.'
}

$localUsers = @(Get-LocalUser | Where-Object { $_.Enabled })
if ($localUsers.Count -lt 2) {
    throw 'At least two enabled local users are required.'
}

$adminUserNames = Get-LocalAdministratorUserNames
$localAdminUsers = @($localUsers | Where-Object { $adminUserNames.Contains([string]$_.Name) })
if ($localAdminUsers.Count -lt 2) {
    throw 'At least two enabled local users in local Administrators are required.'
}

Write-Host 'Available local administrator accounts:'
foreach ($adminUser in $localAdminUsers | Sort-Object Name) {
    if ([string]::IsNullOrWhiteSpace([string]$adminUser.FullName)) {
        Write-Host (" - {0}" -f $adminUser.Name)
    }
    else {
        Write-Host (" - {0} ({1})" -f $adminUser.Name, $adminUser.FullName)
    }
}

$defaultUserA = $null
$defaultUserB = $null

if (-not [string]::IsNullOrWhiteSpace($DefaultPrimaryUser)) {
    try {
        $defaultUserA = (Resolve-LocalAccount -InputName $DefaultPrimaryUser -LocalUsers $localUsers).UserName
    }
    catch {
    }
}
if ($defaultUserA -and -not $adminUserNames.Contains($defaultUserA)) {
    $defaultUserA = $null
}
if (-not $defaultUserA) {
    $defaultUserA = [string]$localAdminUsers[0].Name
}

if (-not [string]::IsNullOrWhiteSpace($DefaultSecondaryUser)) {
    try {
        $defaultUserB = (Resolve-LocalAccount -InputName $DefaultSecondaryUser -LocalUsers $localUsers).UserName
    }
    catch {
    }
}
if ($defaultUserB -and -not $adminUserNames.Contains($defaultUserB)) {
    $defaultUserB = $null
}
if (-not $defaultUserB -or $defaultUserB -ieq $defaultUserA) {
    $fallbackSecondary = $localAdminUsers | Where-Object { [string]$_.Name -ine $defaultUserA } | Select-Object -First 1
    if (-not $fallbackSecondary) {
        throw 'Could not determine a secondary default user. Make sure at least two admin users exist.'
    }

    $defaultUserB = [string]$fallbackSecondary.Name
}

$defaultHotkeyNormalized = (ConvertFrom-HotkeyText -InputText $DefaultHotkey).Canonical

Write-Host ''
Write-Host 'Configure one or more switch profiles. Each profile links two users and one hotkey.'
Write-Host 'If the same hotkey has multiple valid targets for the current user, a chooser UI will appear.'
Write-Host ''

$profileCount = Read-ProfileCount
$profiles = @()
$hotkeys = @()
$hotkeyIdsByCanonical = @{}
$selectedAccounts = @{}
$profileSignature = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

$nextHotkeyId = 1
$lastHotkey = $defaultHotkeyNormalized
$nextDefaultA = $defaultUserA
$nextDefaultB = $defaultUserB

for ($index = 1; $index -le $profileCount; $index++) {
    Write-Host ''
    Write-Host ("Profile {0} of {1}" -f $index, $profileCount)

    $userA = Read-LocalAccountFromPrompt -PromptText 'First user' -DefaultValue $nextDefaultA -LocalUsers $localUsers -AdminUserNames $adminUserNames

    while ($true) {
        $userB = Read-LocalAccountFromPrompt -PromptText 'Second user' -DefaultValue $nextDefaultB -LocalUsers $localUsers -AdminUserNames $adminUserNames
        if ($userB.UserName -ieq $userA.UserName) {
            Write-Host 'First and second user must be different.' -ForegroundColor Yellow
            continue
        }

        break
    }

    $hotkey = Read-HotkeyFromPrompt -DefaultValue $lastHotkey
    $canonicalPair = @($userA.UserName, $userB.UserName) | Sort-Object
    $signature = ('{0}|{1}|{2}' -f $canonicalPair[0], $canonicalPair[1], $hotkey.Canonical)
    if ($profileSignature.Contains($signature)) {
        Write-Host 'Duplicate profile detected; skipping this duplicate entry.' -ForegroundColor Yellow
        $index--
        continue
    }

    [void]$profileSignature.Add($signature)

    if ($hotkeyIdsByCanonical.ContainsKey($hotkey.Canonical)) {
        $hotkeyId = [string]$hotkeyIdsByCanonical[$hotkey.Canonical]
    }
    else {
        $hotkeyId = ('HK{0}' -f $nextHotkeyId)
        $nextHotkeyId += 1
        $hotkeyIdsByCanonical[$hotkey.Canonical] = $hotkeyId

        $hotkeys += [pscustomobject]@{
            HotkeyId = $hotkeyId
            Hotkey   = $hotkey.Canonical
            Keys     = @($hotkey.Keys)
        }
    }

    $profileId = ('P{0}' -f $index)
    $profiles += [pscustomobject]@{
        ProfileId   = $profileId
        UserA       = $userA.UserName
        UserB       = $userB.UserName
        Hotkey      = $hotkey.Canonical
        HotkeyId    = $hotkeyId
        DisplayName = ('{0} <-> {1}' -f $userA.UserName, $userB.UserName)
    }

    $selectedAccounts[$userA.UserName] = $userA
    $selectedAccounts[$userB.UserName] = $userB

    $lastHotkey = $hotkey.Canonical
    $nextDefaultA = $userA.UserName
    $nextDefaultB = $userB.UserName

    Write-Host ("Added profile: {0} <-> {1} on {2}" -f $userA.UserName, $userB.UserName, $hotkey.Canonical)
}

if ($profiles.Count -eq 0) {
    throw 'No valid switch profiles were configured.'
}

$userRecords = @()
foreach ($userName in ($selectedAccounts.Keys | Sort-Object)) {
    $account = $selectedAccounts[$userName]

    $passwordSecure = Read-Host ("Password for {0}" -f $account.Qualified) -AsSecureString
    $passwordPlain = Convert-SecureStringToPlainText -SecureString $passwordSecure

    if ([string]::IsNullOrWhiteSpace($passwordPlain)) {
        throw "Password for $($account.Qualified) cannot be blank."
    }

    $credentialCheck = Test-LocalCredential -QualifiedUser $account.Qualified -Password $passwordPlain
    if (-not $credentialCheck.Success) {
        throw "Password validation failed for $($account.Qualified) (Win32 error $($credentialCheck.Win32Error)). Use the Windows account password, not a PIN."
    }

    $passwordEncrypted = Protect-PlainTextLocalMachine -PlainText $passwordPlain
    $picturePath = Get-UserPicturePath -SidValue $account.SidValue -UserName $account.UserName

    $userRecords += [pscustomobject]@{
        UserName    = $account.UserName
        FullName    = $account.FullName
        Qualified   = $account.Qualified
        SidValue    = $account.SidValue
        PasswordEnc = $passwordEncrypted
        PicturePath = $picturePath
    }

    $passwordPlain = $null
}

$userRecordsArray = @($userRecords)
$profilesArray = @($profiles)
$hotkeysArray = @($hotkeys)

New-Item -Path $installDir -ItemType Directory -Force | Out-Null
New-Item -Path $commandsDir -ItemType Directory -Force | Out-Null

Get-ChildItem -LiteralPath $commandsDir -File -Filter '*.b64' -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

$config = [pscustomobject]@{
    Version      = 3
    SwitchMode   = $defaultSwitchMode
    MachineName  = $env:COMPUTERNAME
    Users        = $userRecordsArray
    Profiles     = $profilesArray
    Hotkeys      = $hotkeysArray
    UpdatedAtUtc = [System.DateTime]::UtcNow.ToString('o')
}

$config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $configPath -Encoding UTF8

foreach ($hotkey in $hotkeysArray) {
    $encodedCommand = New-EncodedSwitchCommand -ConfigFilePath $configPath -HotkeyId $hotkey.HotkeyId
    $commandPath = Join-Path $commandsDir ("{0}.b64" -f $hotkey.HotkeyId)
    Write-Utf8NoBomFile -Path $commandPath -Content $encodedCommand
}

foreach ($hotkey in $hotkeysArray) {
    $commandPath = Join-Path $commandsDir ("{0}.b64" -f $hotkey.HotkeyId)
    if (-not (Test-Path -LiteralPath $commandPath)) {
        throw "Missing command payload file after generation: $commandPath"
    }

    $length = (Get-Item -LiteralPath $commandPath -ErrorAction Stop).Length
    if ($length -lt 32) {
        throw "Generated command payload file is unexpectedly small: $commandPath"
    }
}

Write-ListenerScript -Path $listenerScriptPath -Hotkeys $hotkeysArray
Stop-ListenerProcesses -ScriptPath $listenerScriptPath

$staleTaskNames = @(Get-ScheduledTask -ErrorAction SilentlyContinue |
    Where-Object { $_.TaskName -like "$listenerTaskPrefix-*" } |
    Select-Object -ExpandProperty TaskName -Unique)
$staleTaskNames += $legacyListenerTaskName

foreach ($taskName in $staleTaskNames | Select-Object -Unique) {
    Remove-TaskIfExists -TaskName $taskName
}

$registeredTaskNames = @()
foreach ($userRecord in $userRecordsArray) {
    $taskName = New-ListenerTaskName -SidValue ([string]$userRecord.SidValue) -UserName ([string]$userRecord.UserName)
    Register-ListenerTask -TaskName $taskName -AutoHotkeyExe $ahkExe -ListenerScriptPath $listenerScriptPath -UserId ([string]$userRecord.Qualified)
    $registeredTaskNames += $taskName
}

foreach ($taskName in $registeredTaskNames) {
    try {
        Start-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    }
    catch {
    }
}

$currentUserName = [System.Environment]::UserName
$currentUserConfigured = $userRecordsArray | Where-Object { [string]$_.UserName -ieq $currentUserName } | Select-Object -First 1
if ($currentUserConfigured) {
    try {
        Start-Process -FilePath $ahkExe -ArgumentList ('"{0}"' -f $listenerScriptPath) -WindowStyle Hidden | Out-Null
        Write-Host "Listener startup attempted for current user: $currentUserName"
    }
    catch {
        Write-Host "Could not start listener process directly for current user: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    Start-Sleep -Milliseconds 900
    if (-not (Test-ListenerProcessRunning -ScriptPath $listenerScriptPath)) {
        throw "Listener process failed to start for current user '$currentUserName'. Confirm AutoHotkey v2 is installed and not blocked by security policy."
    }
}
else {
    Write-Host "Current user '$currentUserName' is not configured. Listener will start when configured users log in."
}

Write-Host ''
Write-Host 'Configured profiles:'
foreach ($profile in $profilesArray) {
    Write-Host (" - {0}: {1} <-> {2} ({3})" -f $profile.ProfileId, $profile.UserA, $profile.UserB, $profile.Hotkey)
}

Write-Host ''
Write-Host 'InstantLoginSwitcher installed.'
Write-Host ("Listener log: {0}" -f (Join-Path $installDir 'listener.log'))
Write-Host ("Switch log:   {0}" -f (Join-Path $installDir 'switch.log'))
Write-Host 'Sign out and sign back in once, then test the configured hotkeys.'
