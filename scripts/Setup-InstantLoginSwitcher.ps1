param(
    [string]$PrimaryUser,
    [string]$SecondaryUser,
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramData 'InstantLoginSwitcher'
$taskNameBase = 'InstantLoginSwitcher-Hotkey'

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

if (-not (Test-Admin)) {
    throw 'Run this setup script in an elevated PowerShell session (Run as Administrator).'
}

if ($Uninstall) {
    $knownTasks = @()
    if ($PrimaryUser) {
        $knownTasks += "$taskNameBase-$($PrimaryUser.Replace(' ','_'))"
    }
    if ($SecondaryUser) {
        $knownTasks += "$taskNameBase-$($SecondaryUser.Replace(' ','_'))"
    }

    if ($knownTasks.Count -eq 0) {
        $knownTasks += (Get-ScheduledTask -ErrorAction SilentlyContinue |
            Where-Object { $_.TaskName -like "$taskNameBase-*" } |
            Select-Object -ExpandProperty TaskName)
    }

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

$ahkExe = Join-Path ${env:ProgramFiles} 'AutoHotkey\v2\AutoHotkey64.exe'
if (-not (Test-Path $ahkExe)) {
    throw "AutoHotkey v2 not found at '$ahkExe'. Install AutoHotkey v2 first."
}

Import-Module (Join-Path $PSScriptRoot 'CredentialStore.psm1') -Force

$primaryPassword = Read-Host "Password for $PrimaryUser" -AsSecureString
$secondaryPassword = Read-Host "Password for $SecondaryUser" -AsSecureString

Write-StoredCredential -Target "InstantLoginSwitcher:$PrimaryUser" -UserName $PrimaryUser -Password $primaryPassword
Write-StoredCredential -Target "InstantLoginSwitcher:$SecondaryUser" -UserName $SecondaryUser -Password $secondaryPassword

New-Item -Path $installDir -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'Switch-Login.ps1') -Destination (Join-Path $installDir 'Switch-Login.ps1') -Force
Copy-Item -Path (Join-Path $PSScriptRoot 'CredentialStore.psm1') -Destination (Join-Path $installDir 'CredentialStore.psm1') -Force

$template = Get-Content (Join-Path $PSScriptRoot 'InstantLoginSwitcher.ahk') -Raw
$template = $template.Replace('__PRIMARY_USER__', $PrimaryUser).Replace('__SECONDARY_USER__', $SecondaryUser)
$ahkScriptPath = Join-Path $installDir 'InstantLoginSwitcher.ahk'
Set-Content -Path $ahkScriptPath -Value $template -Encoding UTF8

$plainPrimaryBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($primaryPassword)
$plainSecondaryBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secondaryPassword)
$plainPrimary = $null
$plainSecondary = $null

try {
    $plainPrimary = [Runtime.InteropServices.Marshal]::PtrToStringUni($plainPrimaryBstr)
    $plainSecondary = [Runtime.InteropServices.Marshal]::PtrToStringUni($plainSecondaryBstr)

    $primaryTaskName = "$taskNameBase-$($PrimaryUser.Replace(' ','_'))"
    $secondaryTaskName = "$taskNameBase-$($SecondaryUser.Replace(' ','_'))"

    $action = New-ScheduledTaskAction -Execute $ahkExe -Argument ('"{0}"' -f $ahkScriptPath)
    $primaryTrigger = New-ScheduledTaskTrigger -AtLogOn -User $PrimaryUser
    $secondaryTrigger = New-ScheduledTaskTrigger -AtLogOn -User $SecondaryUser
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

    Register-ScheduledTask -TaskName $primaryTaskName -Action $action -Trigger $primaryTrigger -Settings $settings -RunLevel Highest -User $PrimaryUser -Password $plainPrimary -Force | Out-Null
    Register-ScheduledTask -TaskName $secondaryTaskName -Action $action -Trigger $secondaryTrigger -Settings $settings -RunLevel Highest -User $SecondaryUser -Password $plainSecondary -Force | Out-Null
}
finally {
    if ($plainPrimaryBstr -and $plainPrimaryBstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($plainPrimaryBstr) }
    if ($plainSecondaryBstr -and $plainSecondaryBstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($plainSecondaryBstr) }
    $plainPrimary = $null
    $plainSecondary = $null
}

Write-Host 'InstantLoginSwitcher is installed.'
Write-Host 'Hotkey: Numpad4 + Numpad5 + Numpad6'
