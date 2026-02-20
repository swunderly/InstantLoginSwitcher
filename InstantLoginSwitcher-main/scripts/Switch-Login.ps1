param(
    [Parameter(Mandatory)] [string]$PrimaryUser,
    [Parameter(Mandatory)] [string]$SecondaryUser,
    [string]$MachineName = $env:COMPUTERNAME
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$logPath = $null
try {
    $logDir = Join-Path $env:ProgramData 'InstantLoginSwitcher'
    New-Item -Path $logDir -ItemType Directory -Force -ErrorAction Stop | Out-Null
    $logPath = Join-Path $logDir 'switch.log'
}
catch {
    # Logging is optional; failures here should not block the switch flow.
}

function Write-Log {
    param([Parameter(Mandatory)][string]$Message)

    if (-not $logPath) {
        return
    }

    try {
        $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        Add-Content -LiteralPath $logPath -Value "$timestamp $Message" -Encoding UTF8
    }
    catch {
        # no-op
    }
}

function Test-Admin {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Set-AutoLogon {
    param(
        [Parameter(Mandatory)][string]$UserName,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)][string]$Domain
    )

    $path = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
    Set-ItemProperty -Path $path -Name 'AutoAdminLogon' -Value '1' -Type String
    Set-ItemProperty -Path $path -Name 'DefaultUserName' -Value $UserName -Type String
    Set-ItemProperty -Path $path -Name 'DefaultPassword' -Value $Password -Type String
    Set-ItemProperty -Path $path -Name 'DefaultDomainName' -Value $Domain -Type String
}

try {
    Import-Module (Join-Path $PSScriptRoot 'CredentialStore.psm1') -Force

    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name.Split('\\')[-1]
    Write-Log "Switch requested by $currentUser."

    if (-not (Test-Admin)) {
        throw "Current user '$currentUser' is not running with administrative privileges."
    }

    if ($currentUser -eq $PrimaryUser) {
        $targetUser = $SecondaryUser
    }
    elseif ($currentUser -eq $SecondaryUser) {
        $targetUser = $PrimaryUser
    }
    else {
        throw "Current user '$currentUser' is not part of configured switch pair."
    }

    $target = "InstantLoginSwitcher:$targetUser"
    $cred = Read-StoredCredential -Target $target
    if ([string]::IsNullOrEmpty($cred.Password)) {
        throw "No password was found in Credential Manager for target '$target'."
    }

    Set-AutoLogon -UserName $targetUser -Password $cred.Password -Domain $MachineName
    Write-Log "AutoAdminLogon configured for '$targetUser'."

    $sessionId = (Get-Process -Id $PID).SessionId
    $exclude = @(
        'Idle','System','Registry','csrss','wininit','winlogon','services','lsass','smss',
        'svchost','dwm','fontdrvhost','sihost','ctfmon','explorer','taskhostw',
        'ShellExperienceHost','StartMenuExperienceHost','RuntimeBroker','AutoHotkey',
        'AutoHotkey64'
    )

    Get-Process | Where-Object {
        $_.SessionId -eq $sessionId -and $_.Id -ne $PID -and $exclude -notcontains $_.ProcessName
    } | ForEach-Object {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }

    Write-Log "Logging out current user '$currentUser'."
    Start-Sleep -Milliseconds 250
    Start-Process -FilePath shutdown.exe -ArgumentList '/l /f' -WindowStyle Hidden
}
catch {
    Write-Log "ERROR: $($_.Exception.Message)"
    throw
}
