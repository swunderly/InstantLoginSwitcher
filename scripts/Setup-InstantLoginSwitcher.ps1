param(
    [string]$PrimaryUser,
    [string]$SecondaryUser,
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramData 'InstantLoginSwitcher'
$listenerTaskName = 'InstantLoginSwitcher-Hotkey-Listener'

function Test-Admin {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Remove-TaskIfExists {
    param([Parameter(Mandatory)][string]$TaskName)

    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    }
}

function Stop-ListenerProcesses {
    param([Parameter(Mandatory)][string]$ScriptPath)

    $normalizedScriptPath = [IO.Path]::GetFullPath($ScriptPath).ToLowerInvariant()
    $ahkProcessNames = @('AutoHotkey64.exe', 'AutoHotkey.exe')

    $running = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $ahkProcessNames -contains $_.Name -and
            $_.CommandLine -and
            $_.CommandLine.ToLowerInvariant().Contains($normalizedScriptPath)
        }

    foreach ($proc in $running) {
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
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

if (-not (Test-Admin)) {
    throw 'Run this setup script in an elevated PowerShell session (Run as Administrator).'
}

if ($Uninstall) {
    $listenerScriptPath = Join-Path $installDir 'InstantLoginSwitcher.ahk'
    Stop-ListenerProcesses -ScriptPath $listenerScriptPath

    $knownTasks = @($listenerTaskName)
    $knownTasks += (Get-ScheduledTask -ErrorAction SilentlyContinue |
        Where-Object { $_.TaskName -like 'InstantLoginSwitcher-Hotkey-*' } |
        Select-Object -ExpandProperty TaskName)

    foreach ($taskName in $knownTasks | Select-Object -Unique) {
        Remove-TaskIfExists -TaskName $taskName
    }

    if (Test-Path $installDir) {
        Remove-Item -Path $installDir -Recurse -Force
    }

    Write-Host 'InstantLoginSwitcher removed.'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($PrimaryUser) -or [string]::IsNullOrWhiteSpace($SecondaryUser)) {
    throw 'Both -PrimaryUser and -SecondaryUser are required unless using -Uninstall.'
}

$primaryAccount = Resolve-LocalAccount -InputName $PrimaryUser
$secondaryAccount = Resolve-LocalAccount -InputName $SecondaryUser

$ahkExe = Join-Path ${env:ProgramFiles} 'AutoHotkey\v2\AutoHotkey64.exe'
if (-not (Test-Path $ahkExe)) {
    throw "AutoHotkey v2 not found at '$ahkExe'. Install AutoHotkey v2 first."
}

Import-Module (Join-Path $PSScriptRoot 'CredentialStore.psm1') -Force

$primaryPassword = Read-Host "Password for $($primaryAccount.Qualified)" -AsSecureString
$secondaryPassword = Read-Host "Password for $($secondaryAccount.Qualified)" -AsSecureString

Write-StoredCredential -Target "InstantLoginSwitcher:$($primaryAccount.UserName)" -UserName $primaryAccount.Qualified -Password $primaryPassword
Write-StoredCredential -Target "InstantLoginSwitcher:$($secondaryAccount.UserName)" -UserName $secondaryAccount.Qualified -Password $secondaryPassword

New-Item -Path $installDir -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'Switch-Login.ps1') -Destination (Join-Path $installDir 'Switch-Login.ps1') -Force
Copy-Item -Path (Join-Path $PSScriptRoot 'CredentialStore.psm1') -Destination (Join-Path $installDir 'CredentialStore.psm1') -Force

$template = Get-Content (Join-Path $PSScriptRoot 'InstantLoginSwitcher.ahk') -Raw
$template = $template.Replace('__PRIMARY_USER__', $primaryAccount.UserName).Replace('__SECONDARY_USER__', $secondaryAccount.UserName)
$ahkScriptPath = Join-Path $installDir 'InstantLoginSwitcher.ahk'

Stop-ListenerProcesses -ScriptPath $ahkScriptPath

Set-Content -Path $ahkScriptPath -Value $template -Encoding UTF8

$action = New-ScheduledTaskAction -Execute $ahkExe -Argument ('"{0}"' -f $ahkScriptPath)
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -GroupId 'Users' -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName $listenerTaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force -ErrorAction Stop | Out-Null

Write-Host "Resolved primary account: $($primaryAccount.Qualified)"
Write-Host "Resolved secondary account: $($secondaryAccount.Qualified)"
Write-Host "Scheduled task: $listenerTaskName"
Write-Host 'InstantLoginSwitcher is installed.'
Write-Host 'Hotkey: Numpad4 + Numpad5 + Numpad6'
