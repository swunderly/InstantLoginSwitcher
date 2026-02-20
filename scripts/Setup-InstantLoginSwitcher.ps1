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
$listenerTaskName = 'InstantLoginSwitcher-Hotkey-Listener'
$entropySeed = 'InstantLoginSwitcher-v2'
$usersGroupSid = 'S-1-5-32-545'

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
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
    }
}

function Assert-LocalAdministratorAccount {
    param([Parameter(Mandatory)][string]$UserName)

    try {
        $admins = Get-LocalGroupMember -Group 'Administrators' -ErrorAction Stop
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

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringUni($bstr)
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

function Protect-PlainTextLocalMachine {
    param(
        [Parameter(Mandatory)][string]$PlainText,
        [Parameter(Mandatory)][byte[]]$Entropy
    )

    $plainBytes = [Text.Encoding]::UTF8.GetBytes($PlainText)
    $protected = [System.Security.Cryptography.ProtectedData]::Protect(
        $plainBytes,
        $Entropy,
        [System.Security.Cryptography.DataProtectionScope]::LocalMachine
    )
    return [Convert]::ToBase64String($protected)
}

function ConvertTo-SingleQuotedLiteral {
    param([Parameter(Mandatory)][string]$Value)
    return "'" + ($Value -replace "'", "''") + "'"
}

function New-EncodedSwitchCommand {
    param(
        [Parameter(Mandatory)][string]$ConfigFilePath,
        [Parameter(Mandatory)][string]$EntropySeedValue
    )

    $configLiteral = ConvertTo-SingleQuotedLiteral -Value $ConfigFilePath
    $entropyLiteral = ConvertTo-SingleQuotedLiteral -Value $EntropySeedValue

    $template = @'
$ErrorActionPreference = 'Stop'
$configPath = __CONFIG_PATH__
$baseDir = [IO.Path]::GetDirectoryName($configPath)
$logPath = Join-Path $baseDir 'switch.log'
$entropy = [Text.Encoding]::UTF8.GetBytes(__ENTROPY_SEED__)

function Write-Log([string]$Message) {
    try {
        $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        Add-Content -LiteralPath $logPath -Value "$timestamp $Message" -Encoding UTF8
    }
    catch {
    }
}

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Hotkey listener is not running elevated.'
    }
}

function Unprotect-Secret([string]$CipherText) {
    $cipherBytes = [Convert]::FromBase64String($CipherText)
    $plainBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $cipherBytes,
        $entropy,
        [System.Security.Cryptography.DataProtectionScope]::LocalMachine
    )
    return [Text.Encoding]::UTF8.GetString($plainBytes)
}

try {
    Assert-Admin

    if (-not (Test-Path -LiteralPath $configPath)) {
        throw "Missing configuration file: $configPath"
    }

    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $currentUser = [Environment]::UserName

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
    Set-ItemProperty -Path $winlogonPath -Name 'DefaultUserName' -Type String -Value $targetUser
    Set-ItemProperty -Path $winlogonPath -Name 'DefaultPassword' -Type String -Value $password
    Set-ItemProperty -Path $winlogonPath -Name 'DefaultDomainName' -Type String -Value $domain

    Write-Log "Prepared AutoAdminLogon for '$targetUser' (triggered by '$currentUser')."
    Start-Sleep -Milliseconds 150
    Start-Process -FilePath shutdown.exe -ArgumentList '/l /f' -WindowStyle Hidden
}
catch {
    Write-Log ("ERROR: " + $_.Exception.Message)
}
'@

    $script = $template.Replace('__CONFIG_PATH__', $configLiteral).Replace('__ENTROPY_SEED__', $entropyLiteral)
    $bytes = [Text.Encoding]::Unicode.GetBytes($script)
    return [Convert]::ToBase64String($bytes)
}

function Write-Utf8NoBomFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Write-ListenerScript {
    param([Parameter(Mandatory)][string]$Path)

    $content = @'
#Requires AutoHotkey v2.0
#SingleInstance Ignore

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

    $scriptName = [IO.Path]::GetFileName($ScriptPath)
    $scriptPattern = [Regex]::Escape($scriptName)
    $fullPathPattern = $null

    if (Test-Path -LiteralPath $ScriptPath) {
        $fullPathPattern = [Regex]::Escape([IO.Path]::GetFullPath($ScriptPath))
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
    Remove-ItemProperty -Path $path -Name 'DefaultPassword' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'DefaultUserName' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'DefaultDomainName' -ErrorAction SilentlyContinue
}

function Register-ListenerTask {
    param(
        [Parameter(Mandatory)][string]$TaskName,
        [Parameter(Mandatory)][string]$AutoHotkeyExe,
        [Parameter(Mandatory)][string]$ListenerScriptPath
    )

    $action = New-ScheduledTaskAction -Execute $AutoHotkeyExe -Argument ('"{0}"' -f $ListenerScriptPath)
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

    try {
        $principal = New-ScheduledTaskPrincipal -GroupId $usersGroupSid -RunLevel Highest
    }
    catch {
        $principal = New-ScheduledTaskPrincipal -GroupId 'Users' -RunLevel Highest
    }

    Remove-TaskIfExists -TaskName $TaskName
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force -ErrorAction Stop | Out-Null
}

if (-not (Test-Admin)) {
    throw 'Run as Administrator.'
}

if ($Mode -eq 'Uninstall') {
    Stop-ListenerProcesses -ScriptPath $listenerScriptPath

    $taskNames = @($listenerTaskName)
    $taskNames += (Get-ScheduledTask -ErrorAction SilentlyContinue |
        Where-Object { $_.TaskName -like 'InstantLoginSwitcher-Hotkey-*' } |
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

$entropy = [Text.Encoding]::UTF8.GetBytes($entropySeed)
$primaryPasswordEncrypted = Protect-PlainTextLocalMachine -PlainText $primaryPasswordPlain -Entropy $entropy
$secondaryPasswordEncrypted = Protect-PlainTextLocalMachine -PlainText $secondaryPasswordPlain -Entropy $entropy

New-Item -Path $installDir -ItemType Directory -Force | Out-Null

$config = [pscustomobject]@{
    Version              = 2
    PrimaryUser          = $primaryAccount.UserName
    SecondaryUser        = $secondaryAccount.UserName
    PrimaryQualifiedUser = $primaryAccount.Qualified
    SecondaryQualifiedUser = $secondaryAccount.Qualified
    PrimaryPasswordEnc   = $primaryPasswordEncrypted
    SecondaryPasswordEnc = $secondaryPasswordEncrypted
    MachineName          = $env:COMPUTERNAME
    UpdatedAtUtc         = [DateTime]::UtcNow.ToString('o')
}

$config | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configPath -Encoding UTF8

$encodedCommand = New-EncodedSwitchCommand -ConfigFilePath $configPath -EntropySeedValue $entropySeed
Write-Utf8NoBomFile -Path $encodedSwitchPath -Content $encodedCommand

Write-ListenerScript -Path $listenerScriptPath
Stop-ListenerProcesses -ScriptPath $listenerScriptPath

$staleTaskNames = Get-ScheduledTask -ErrorAction SilentlyContinue |
    Where-Object { $_.TaskName -like 'InstantLoginSwitcher-Hotkey-*' } |
    Select-Object -ExpandProperty TaskName -Unique

foreach ($taskName in $staleTaskNames) {
    Remove-TaskIfExists -TaskName $taskName
}

Register-ListenerTask -TaskName $listenerTaskName -AutoHotkeyExe $ahkExe -ListenerScriptPath $listenerScriptPath

try {
    Start-ScheduledTask -TaskName $listenerTaskName -ErrorAction SilentlyContinue
}
catch {
}

$primaryPasswordPlain = $null
$secondaryPasswordPlain = $null

Write-Host "Resolved primary account: $($primaryAccount.Qualified)"
Write-Host "Resolved secondary account: $($secondaryAccount.Qualified)"
Write-Host "Listener task: $listenerTaskName"
Write-Host 'InstantLoginSwitcher installed.'
Write-Host 'Hotkey: Numpad4 + Numpad5 + Numpad6'
