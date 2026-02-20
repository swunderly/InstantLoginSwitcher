param(
    [ValidateSet('Install', 'Uninstall')]
    [string]$Mode = 'Install',
    [string]$PrimaryUser,
    [string]$SecondaryUser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramData 'InstantLoginSwitcher'
$configPath = Join-Path $installDir 'config.json'
$encodedSwitchPath = Join-Path $installDir 'switch-command.b64'
$listenerScriptPath = Join-Path $installDir 'InstantLoginSwitcher.ahk'
$listenerTaskPrefix = 'InstantLoginSwitcher-Hotkey'
$legacyListenerTaskName = 'InstantLoginSwitcher-Hotkey-Listener'
$localAdministratorsSid = 'S-1-5-32-544'
$defaultSwitchMode = 'Restart'

function Test-Admin {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
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
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    foreach ($commandName in @('AutoHotkey64.exe', 'AutoHotkey.exe')) {
        $command = Get-Command -Name $commandName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command -and (Test-Path -LiteralPath $command.Source)) {
            return $command.Source
        }
    }

    return $null
}

function Resolve-LocalAccount {
    param([Parameter(Mandatory)][string]$InputName)

    $machine = $env:COMPUTERNAME
    $candidate = $InputName.Trim()

    if ($candidate.Contains('\')) {
        $candidate = $candidate.Split('\', 2)[1]
    }

    $localUsers = Get-LocalUser
    $matched = $localUsers | Where-Object { $_.Name -eq $candidate } | Select-Object -First 1
    if (-not $matched) {
        $matched = $localUsers | Where-Object { $_.FullName -eq $candidate } | Select-Object -First 1
    }

    if (-not $matched) {
        throw "Could not find a local account matching '$InputName'. Use 'Get-LocalUser | Select Name, FullName' and pass Name."
    }

    if (-not $matched.Enabled) {
        throw "Local account '$($matched.Name)' is disabled. Enable it before configuring InstantLoginSwitcher."
    }

    [pscustomobject]@{
        InputName = $InputName
        UserName  = $matched.Name
        FullName  = $matched.FullName
        Qualified = "$machine\$($matched.Name)"
        SidValue  = if ($matched.SID) { $matched.SID.Value } else { $null }
    }
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

function Assert-LocalAdministratorAccount {
    param([Parameter(Mandatory)][string]$UserName)

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

    $isAdmin = $admins | Where-Object {
        $_.Name -ieq "$env:COMPUTERNAME\$UserName" -or
        (($_.Name -split '\\')[-1] -ieq $UserName)
    } | Select-Object -First 1

    if (-not $isAdmin) {
        throw "Account '$env:COMPUTERNAME\$UserName' is not in local Administrators. Both switch accounts must be administrators."
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

    $parts = $QualifiedUser.Split('\', 2)
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

    # Keep storage format portable across Windows PowerShell builds by avoiding DPAPI type dependencies.
    $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($PlainText)
    return 'B64:' + [System.Convert]::ToBase64String($plainBytes)
}

function ConvertTo-SingleQuotedLiteral {
    param([Parameter(Mandatory)][string]$Value)
    return "'" + ($Value -replace "'", "''") + "'"
}

function New-EncodedSwitchCommand {
    param([Parameter(Mandatory)][string]$ConfigFilePath)

    $configLiteral = ConvertTo-SingleQuotedLiteral -Value $ConfigFilePath

    $template = @'
$ErrorActionPreference = 'Stop'
$configPath = __CONFIG_PATH__
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

try {
    Assert-Admin

    if (-not (Test-Path -LiteralPath $configPath)) {
        throw "Missing configuration file: $configPath"
    }

    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $currentUser = [System.Environment]::UserName

    if ($currentUser -ieq $config.PrimaryUser) {
        $targetUser = [string]$config.SecondaryUser
        $encryptedPassword = [string]$config.SecondaryPasswordEnc
    }
    elseif ($currentUser -ieq $config.SecondaryUser) {
        $targetUser = [string]$config.PrimaryUser
        $encryptedPassword = [string]$config.PrimaryPasswordEnc
    }
    else {
        throw "Current user '$currentUser' is not part of configured switch pair."
    }

    $password = Unprotect-Secret -CipherText $encryptedPassword
    if ([string]::IsNullOrWhiteSpace($password)) {
        throw 'Target password could not be decrypted.'
    }

    $domain = if ($config.MachineName) { [string]$config.MachineName } else { $env:COMPUTERNAME }
    $winlogonPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'

    Set-ItemProperty -Path $winlogonPath -Name 'AutoAdminLogon' -Type String -Value '1'
    Set-ItemProperty -Path $winlogonPath -Name 'ForceAutoLogon' -Type String -Value '1'
    Set-ItemProperty -Path $winlogonPath -Name 'DefaultUserName' -Type String -Value $targetUser
    Set-ItemProperty -Path $winlogonPath -Name 'DefaultPassword' -Type String -Value $password
    Set-ItemProperty -Path $winlogonPath -Name 'DefaultDomainName' -Type String -Value $domain
    Set-ItemProperty -Path $winlogonPath -Name 'AltDefaultUserName' -Type String -Value $targetUser
    Set-ItemProperty -Path $winlogonPath -Name 'AltDefaultDomainName' -Type String -Value $domain
    Set-ItemProperty -Path $winlogonPath -Name 'LastUsedUsername' -Type String -Value ($domain + '\' + $targetUser)
    Remove-ItemProperty -Path $winlogonPath -Name 'AutoLogonCount' -ErrorAction SilentlyContinue

    $snapshot = Get-ItemProperty -Path $winlogonPath -ErrorAction SilentlyContinue
    if ($snapshot) {
        Write-Log ("Winlogon snapshot: AutoAdminLogon={0}; ForceAutoLogon={1}; DefaultUserName={2}; DefaultDomainName={3}" -f $snapshot.AutoAdminLogon, $snapshot.ForceAutoLogon, $snapshot.DefaultUserName, $snapshot.DefaultDomainName)
    }

    $policyPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
    if (Test-Path -LiteralPath $policyPath) {
        $policy = Get-ItemProperty -Path $policyPath -ErrorAction SilentlyContinue
        if ($policy) {
            $caption = [string]$policy.legalnoticecaption
            $text = [string]$policy.legalnoticetext
            if (-not [string]::IsNullOrWhiteSpace($caption) -or -not [string]::IsNullOrWhiteSpace($text)) {
                Write-Log 'WARNING: Legal notice policy is enabled and can block automatic sign-in.'
            }
        }
    }

    $switchMode = if ($config.SwitchMode) { [string]$config.SwitchMode } else { 'Restart' }
    if ($switchMode -ieq 'Logoff') {
        $shutdownArgs = '/l /f'
    }
    else {
        $shutdownArgs = '/r /f /t 0'
        $switchMode = 'Restart'
    }

    Write-Log "Prepared AutoAdminLogon+ForceAutoLogon for '$targetUser' (triggered by '$currentUser')."
    Write-Log "Switch action: $switchMode ($shutdownArgs)"
    Start-Sleep -Milliseconds 150
    Start-Process -FilePath shutdown.exe -ArgumentList $shutdownArgs -WindowStyle Hidden
}
catch {
    Write-Log ("ERROR: " + $_.Exception.Message)
}
'@

    $script = $template.Replace('__CONFIG_PATH__', $configLiteral)
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
    param([Parameter(Mandatory)][string]$Path)

    $content = @'
#Requires AutoHotkey v2.0
#SingleInstance Force

encodedPath := A_ScriptDir . "\switch-command.b64"
triggered := false

CheckCombo() {
    global triggered

    if triggered {
        return
    }

    if GetKeyState("Numpad4", "P") && GetKeyState("Numpad5", "P") && GetKeyState("Numpad6", "P") {
        triggered := true
        RunSwitch()
    }
}

Numpad4::CheckCombo()
Numpad5::CheckCombo()
Numpad6::CheckCombo()

Numpad4 Up::ResetTrigger
Numpad5 Up::ResetTrigger
Numpad6 Up::ResetTrigger

ResetTrigger(*) {
    global triggered
    if !GetKeyState("Numpad4", "P") && !GetKeyState("Numpad5", "P") && !GetKeyState("Numpad6", "P") {
        triggered := false
    }
}

RunSwitch() {
    global encodedPath

    if !FileExist(encodedPath) {
        return
    }

    encoded := Trim(FileRead(encodedPath, "UTF-8"))
    if (encoded = "") {
        return
    }

    if (SubStr(encoded, 1, 1) = Chr(0xFEFF)) {
        encoded := SubStr(encoded, 2)
    }

    try {
        Run("powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " . encoded, , "Hide")
    }
    catch {
    }
}
'@

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

if ([string]::IsNullOrWhiteSpace($PrimaryUser) -or [string]::IsNullOrWhiteSpace($SecondaryUser)) {
    throw 'Both PrimaryUser and SecondaryUser are required for install.'
}

$primaryAccount = Resolve-LocalAccount -InputName $PrimaryUser
$secondaryAccount = Resolve-LocalAccount -InputName $SecondaryUser

if ($primaryAccount.UserName -eq $secondaryAccount.UserName) {
    throw 'Primary and secondary users must be different.'
}

Assert-LocalAdministratorAccount -UserName $primaryAccount.UserName
Assert-LocalAdministratorAccount -UserName $secondaryAccount.UserName

$ahkExe = Get-AutoHotkeyExecutable
if (-not $ahkExe) {
    throw 'AutoHotkey v2 was not found. Install AutoHotkey v2 first.'
}

$primaryPasswordSecure = Read-Host "Password for $($primaryAccount.Qualified)" -AsSecureString
$secondaryPasswordSecure = Read-Host "Password for $($secondaryAccount.Qualified)" -AsSecureString

$primaryPasswordPlain = Convert-SecureStringToPlainText -SecureString $primaryPasswordSecure
$secondaryPasswordPlain = Convert-SecureStringToPlainText -SecureString $secondaryPasswordSecure

if ([string]::IsNullOrWhiteSpace($primaryPasswordPlain)) {
    throw "Password for $($primaryAccount.Qualified) cannot be blank."
}

if ([string]::IsNullOrWhiteSpace($secondaryPasswordPlain)) {
    throw "Password for $($secondaryAccount.Qualified) cannot be blank."
}

$primaryCredentialCheck = Test-LocalCredential -QualifiedUser $primaryAccount.Qualified -Password $primaryPasswordPlain
if (-not $primaryCredentialCheck.Success) {
    throw "Password validation failed for $($primaryAccount.Qualified) (Win32 error $($primaryCredentialCheck.Win32Error)). Use the Windows account password, not a PIN."
}

$secondaryCredentialCheck = Test-LocalCredential -QualifiedUser $secondaryAccount.Qualified -Password $secondaryPasswordPlain
if (-not $secondaryCredentialCheck.Success) {
    throw "Password validation failed for $($secondaryAccount.Qualified) (Win32 error $($secondaryCredentialCheck.Win32Error)). Use the Windows account password, not a PIN."
}

$primaryPasswordEncrypted = Protect-PlainTextLocalMachine -PlainText $primaryPasswordPlain
$secondaryPasswordEncrypted = Protect-PlainTextLocalMachine -PlainText $secondaryPasswordPlain

New-Item -Path $installDir -ItemType Directory -Force | Out-Null

$config = [pscustomobject]@{
    Version              = 2
    PrimaryUser          = $primaryAccount.UserName
    SecondaryUser        = $secondaryAccount.UserName
    PrimaryQualifiedUser = $primaryAccount.Qualified
    SecondaryQualifiedUser = $secondaryAccount.Qualified
    PrimaryPasswordEnc   = $primaryPasswordEncrypted
    SecondaryPasswordEnc = $secondaryPasswordEncrypted
    SwitchMode           = $defaultSwitchMode
    MachineName          = $env:COMPUTERNAME
    UpdatedAtUtc         = [System.DateTime]::UtcNow.ToString('o')
}

$config | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configPath -Encoding UTF8

$encodedCommand = New-EncodedSwitchCommand -ConfigFilePath $configPath
Write-Utf8NoBomFile -Path $encodedSwitchPath -Content $encodedCommand

Write-ListenerScript -Path $listenerScriptPath
Stop-ListenerProcesses -ScriptPath $listenerScriptPath

$staleTaskNames = @(Get-ScheduledTask -ErrorAction SilentlyContinue |
    Where-Object { $_.TaskName -like "$listenerTaskPrefix-*" } |
    Select-Object -ExpandProperty TaskName -Unique)
$staleTaskNames += $legacyListenerTaskName

foreach ($taskName in $staleTaskNames) {
    Remove-TaskIfExists -TaskName $taskName
}

$primaryTaskName = New-ListenerTaskName -SidValue $primaryAccount.SidValue -UserName $primaryAccount.UserName
$secondaryTaskName = New-ListenerTaskName -SidValue $secondaryAccount.SidValue -UserName $secondaryAccount.UserName

Register-ListenerTask -TaskName $primaryTaskName -AutoHotkeyExe $ahkExe -ListenerScriptPath $listenerScriptPath -UserId $primaryAccount.Qualified
Register-ListenerTask -TaskName $secondaryTaskName -AutoHotkeyExe $ahkExe -ListenerScriptPath $listenerScriptPath -UserId $secondaryAccount.Qualified

try {
    Start-ScheduledTask -TaskName $primaryTaskName -ErrorAction SilentlyContinue
    Start-ScheduledTask -TaskName $secondaryTaskName -ErrorAction SilentlyContinue
}
catch {
}

$primaryPasswordPlain = $null
$secondaryPasswordPlain = $null

Write-Host "Resolved primary account: $($primaryAccount.Qualified)"
Write-Host "Resolved secondary account: $($secondaryAccount.Qualified)"
Write-Host "Listener task (primary): $primaryTaskName"
Write-Host "Listener task (secondary): $secondaryTaskName"
Write-Host "Switch action: $defaultSwitchMode"
Write-Host 'InstantLoginSwitcher installed.'
Write-Host 'Hotkey: Numpad4 + Numpad5 + Numpad6'
