param(
    [string]$PrimaryUser,
    [string]$SecondaryUser,
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramData 'InstantLoginSwitcher'
$configPath = Join-Path $installDir 'config.json'
$listenerTaskName = 'InstantLoginSwitcher-Hotkey-Listener'
$credentialTargetPrefix = 'InstantLoginSwitcher:'
$usersGroupSid = 'S-1-5-32-545'

function Test-Admin {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
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

    $candidates = @()
    foreach ($root in $roots | Select-Object -Unique) {
        $candidates += (Join-Path $root 'AutoHotkey\v2\AutoHotkey64.exe')
        $candidates += (Join-Path $root 'AutoHotkey\v2\AutoHotkey.exe')
    }

    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    return $null
}

function Stop-ListenerProcesses {
    param([Parameter(Mandatory)][string]$InstallPath)

    $scriptName = 'InstantLoginSwitcher.ahk'
    $scriptPath = Join-Path $InstallPath $scriptName
    $scriptPathPattern = $null

    if (Test-Path -LiteralPath $scriptPath) {
        $scriptPathPattern = [Regex]::Escape([IO.Path]::GetFullPath($scriptPath))
    }

    $namePattern = [Regex]::Escape($scriptName)
    $listenerProcesses = Get-CimInstance Win32_Process -Filter "Name LIKE 'AutoHotkey%.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and (
                ($scriptPathPattern -and $_.CommandLine -match $scriptPathPattern) -or
                $_.CommandLine -match $namePattern
            )
        }

    foreach ($process in $listenerProcesses) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Remove-TaskIfExists {
    param([Parameter(Mandatory)][string]$TaskName)

    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing) {
        try {
            Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        }
        catch {
            # Task may not currently be running.
        }

        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    }
}

function Resolve-LocalAccount {
    param([Parameter(Mandatory)][string]$InputName)

    $machine = $env:COMPUTERNAME
    $candidate = $InputName
    if ($candidate.Contains('\')) {
        $parts = $candidate.Split('\', 2)
        $candidate = $parts[1]
    }

    $localUsers = Get-LocalUser

    $matched = $localUsers | Where-Object { $_.Name -eq $candidate } | Select-Object -First 1
    if (-not $matched) {
        $matched = $localUsers | Where-Object { $_.FullName -eq $candidate } | Select-Object -First 1
    }

    if (-not $matched) {
        throw "Could not find a local account matching '$InputName'. Run 'Get-LocalUser | Select Name, FullName' and use the Name value."
    }

    [pscustomobject]@{
        InputName = $InputName
        UserName  = $matched.Name
        FullName  = $matched.FullName
        Qualified = "$machine\$($matched.Name)"
    }
}

function Import-CredentialStoreModule {
    param(
        [string]$InstallPath,
        [string]$ScriptRootPath
    )

    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($InstallPath)) {
        $candidates += (Join-Path $InstallPath 'CredentialStore.psm1')
    }

    if (-not [string]::IsNullOrWhiteSpace($ScriptRootPath)) {
        $candidates += (Join-Path $ScriptRootPath 'CredentialStore.psm1')
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate) {
            Import-Module $candidate -Force
            return $true
        }
    }

    return $false
}

function Read-InstallConfig {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "Could not parse install config at '$Path'. Continuing without it."
        return $null
    }
}

function Write-InstallConfig {
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][string]$Path
    )

    $Config | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Remove-ConfiguredCredentials {
    param(
        $Config,
        [string]$PrimaryUserName,
        [string]$SecondaryUserName
    )

    if (-not (Get-Command Remove-StoredCredential -ErrorAction SilentlyContinue)) {
        return
    }

    $targets = @()
    if ($Config) {
        if (($Config.PSObject.Properties.Name -contains 'PrimaryUser') -and $Config.PrimaryUser) {
            $targets += "$credentialTargetPrefix$($Config.PrimaryUser)"
        }
        if (($Config.PSObject.Properties.Name -contains 'SecondaryUser') -and $Config.SecondaryUser) {
            $targets += "$credentialTargetPrefix$($Config.SecondaryUser)"
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($PrimaryUserName)) {
        $targets += "$credentialTargetPrefix$PrimaryUserName"
    }
    if (-not [string]::IsNullOrWhiteSpace($SecondaryUserName)) {
        $targets += "$credentialTargetPrefix$SecondaryUserName"
    }

    foreach ($target in $targets | Select-Object -Unique) {
        Remove-StoredCredential -Target $target
    }
}

function Disable-AutoAdminLogon {
    $path = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'

    if (-not (Test-Path -LiteralPath $path)) {
        return
    }

    Set-ItemProperty -Path $path -Name 'AutoAdminLogon' -Value '0' -Type String -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $path -Name 'DefaultPassword' -ErrorAction SilentlyContinue
}

if (-not (Test-Admin)) {
    throw 'Run this setup script in an elevated PowerShell session (Run as Administrator).'
}

if ($Uninstall) {
    $config = Read-InstallConfig -Path $configPath

    Stop-ListenerProcesses -InstallPath $installDir

    $knownTasks = @($listenerTaskName)
    $knownTasks += (Get-ScheduledTask -ErrorAction SilentlyContinue |
        Where-Object { $_.TaskName -like 'InstantLoginSwitcher-Hotkey-*' } |
        Select-Object -ExpandProperty TaskName)

    foreach ($taskName in $knownTasks | Select-Object -Unique) {
        Remove-TaskIfExists -TaskName $taskName
    }

    if (Import-CredentialStoreModule -InstallPath $installDir -ScriptRootPath $PSScriptRoot) {
        Remove-ConfiguredCredentials -Config $config -PrimaryUserName $PrimaryUser -SecondaryUserName $SecondaryUser
    }

    Disable-AutoAdminLogon

    if (Test-Path -LiteralPath $installDir) {
        Start-Sleep -Milliseconds 300
        Remove-Item -LiteralPath $installDir -Recurse -Force
    }

    Write-Host 'InstantLoginSwitcher removed.'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($PrimaryUser) -or [string]::IsNullOrWhiteSpace($SecondaryUser)) {
    throw 'Both -PrimaryUser and -SecondaryUser are required unless using -Uninstall.'
}

$primaryAccount = Resolve-LocalAccount -InputName $PrimaryUser
$secondaryAccount = Resolve-LocalAccount -InputName $SecondaryUser

if ($primaryAccount.UserName -eq $secondaryAccount.UserName) {
    throw 'Primary and secondary users must be different local accounts.'
}

$ahkExe = Get-AutoHotkeyExecutable
if (-not $ahkExe) {
    throw 'AutoHotkey v2 not found. Install AutoHotkey v2 first.'
}

if (-not (Import-CredentialStoreModule -InstallPath $null -ScriptRootPath $PSScriptRoot)) {
    throw 'CredentialStore.psm1 could not be loaded.'
}

$primaryPassword = Read-Host "Password for $($primaryAccount.Qualified)" -AsSecureString
$secondaryPassword = Read-Host "Password for $($secondaryAccount.Qualified)" -AsSecureString

New-Item -Path $installDir -ItemType Directory -Force | Out-Null

Copy-Item -Path (Join-Path $PSScriptRoot 'Switch-Login.ps1') -Destination (Join-Path $installDir 'Switch-Login.ps1') -Force
Copy-Item -Path (Join-Path $PSScriptRoot 'CredentialStore.psm1') -Destination (Join-Path $installDir 'CredentialStore.psm1') -Force

$template = Get-Content (Join-Path $PSScriptRoot 'InstantLoginSwitcher.ahk') -Raw
$template = $template.Replace('__PRIMARY_USER__', $primaryAccount.UserName).Replace('__SECONDARY_USER__', $secondaryAccount.UserName)
$ahkScriptPath = Join-Path $installDir 'InstantLoginSwitcher.ahk'

Stop-ListenerProcesses -InstallPath $installDir
Set-Content -Path $ahkScriptPath -Value $template -Encoding UTF8

Write-StoredCredential -Target "$credentialTargetPrefix$($primaryAccount.UserName)" -UserName $primaryAccount.Qualified -Password $primaryPassword
Write-StoredCredential -Target "$credentialTargetPrefix$($secondaryAccount.UserName)" -UserName $secondaryAccount.Qualified -Password $secondaryPassword

$installConfig = [pscustomobject]@{
    PrimaryUser       = $primaryAccount.UserName
    SecondaryUser     = $secondaryAccount.UserName
    PrimaryQualified  = $primaryAccount.Qualified
    SecondaryQualified = $secondaryAccount.Qualified
    InstalledAtUtc    = [DateTime]::UtcNow.ToString('o')
}
Write-InstallConfig -Config $installConfig -Path $configPath

$action = New-ScheduledTaskAction -Execute $ahkExe -Argument ('"{0}"' -f $ahkScriptPath)
$trigger = New-ScheduledTaskTrigger -AtLogOn

try {
    $principal = New-ScheduledTaskPrincipal -GroupId $usersGroupSid -RunLevel Highest
}
catch {
    $principal = New-ScheduledTaskPrincipal -GroupId 'Users' -RunLevel Highest
}

$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Remove-TaskIfExists -TaskName $listenerTaskName
Register-ScheduledTask -TaskName $listenerTaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force -ErrorAction Stop | Out-Null

try {
    Start-ScheduledTask -TaskName $listenerTaskName -ErrorAction SilentlyContinue
}
catch {
    # Listener will start at next logon if manual start is not allowed.
}

Write-Host "Resolved primary account: $($primaryAccount.Qualified)"
Write-Host "Resolved secondary account: $($secondaryAccount.Qualified)"
Write-Host "Scheduled task: $listenerTaskName"
Write-Host 'InstantLoginSwitcher is installed.'
Write-Host 'Hotkey: Numpad4 + Numpad5 + Numpad6'
