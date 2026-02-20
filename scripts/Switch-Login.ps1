param(
    [Parameter(Mandatory)] [string]$PrimaryUser,
    [Parameter(Mandatory)] [string]$SecondaryUser,
    [string]$MachineName = $env:COMPUTERNAME
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'CredentialStore.psm1') -Force

function Set-AutoLogon {
    param(
        [string]$UserName,
        [string]$Password,
        [string]$Domain
    )

    $path = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
    Set-ItemProperty -Path $path -Name 'AutoAdminLogon' -Value '1' -Type String
    Set-ItemProperty -Path $path -Name 'DefaultUserName' -Value $UserName -Type String
    Set-ItemProperty -Path $path -Name 'DefaultPassword' -Value $Password -Type String
    Set-ItemProperty -Path $path -Name 'DefaultDomainName' -Value $Domain -Type String
}

$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name.Split('\\')[-1]
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
Set-AutoLogon -UserName $targetUser -Password $cred.Password -Domain $MachineName

$sessionId = (Get-Process -Id $PID).SessionId
$exclude = @('Idle','System','Registry','csrss','wininit','winlogon','services','lsass','smss','svchost','dwm','fontdrvhost','sihost','ctfmon','explorer','taskhostw','ShellExperienceHost','StartMenuExperienceHost','RuntimeBroker')
Get-Process | Where-Object {
    $_.SessionId -eq $sessionId -and $_.Id -ne $PID -and $exclude -notcontains $_.ProcessName
} | ForEach-Object {
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Milliseconds 250
Start-Process -FilePath shutdown.exe -ArgumentList '/l /f' -WindowStyle Hidden
