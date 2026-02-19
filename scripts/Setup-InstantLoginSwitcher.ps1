param(
    [string]$PrimaryUser,
    [string]$SecondaryUser,
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$installDir = Join-Path $env:ProgramData 'InstantLoginSwitcher'
$taskNameBase = 'InstantLoginSwitcher-Hotkey'

function Test-Admin {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
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
        $knownTasks += (schtasks.exe /Query /FO LIST /V 2>$null |
            Where-Object { $_ -like 'TaskName:*' -and $_ -like "*$taskNameBase-*" } |
            ForEach-Object { ($_ -split ':', 2)[1].Trim() })
    }

    foreach ($taskName in $knownTasks | Select-Object -Unique) {
        schtasks.exe /Delete /TN $taskName /F *> $null
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

$runCommand = '"{0}" "{1}"' -f $ahkExe, $ahkScriptPath

$plainPrimary = [Runtime.InteropServices.Marshal]::PtrToStringUni([Runtime.InteropServices.Marshal]::SecureStringToBSTR($primaryPassword))
$plainSecondary = [Runtime.InteropServices.Marshal]::PtrToStringUni([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secondaryPassword))

try {
    $primaryTaskName = "$taskNameBase-$($PrimaryUser.Replace(' ','_'))"
    $secondaryTaskName = "$taskNameBase-$($SecondaryUser.Replace(' ','_'))"
    schtasks.exe /Create /TN $primaryTaskName /SC ONLOGON /RL HIGHEST /RU $PrimaryUser /RP $plainPrimary /TR $runCommand /F | Out-Null
    schtasks.exe /Create /TN $secondaryTaskName /SC ONLOGON /RL HIGHEST /RU $SecondaryUser /RP $plainSecondary /TR $runCommand /F | Out-Null
}
finally {
    if ($plainPrimary) { $plainPrimary = $null }
    if ($plainSecondary) { $plainSecondary = $null }
}

Write-Host 'InstantLoginSwitcher is installed.'
Write-Host "Hotkey: Numpad4 + Numpad5 + Numpad6"
