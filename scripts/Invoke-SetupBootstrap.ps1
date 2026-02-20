$ErrorActionPreference = 'Stop'

try {
    $scriptPath = $env:CORE_SCRIPT
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        throw 'CORE_SCRIPT environment variable is missing.'
    }

    if (-not (Test-Path -LiteralPath $scriptPath)) {
        throw "Core script not found: $scriptPath"
    }

    $scriptText = Get-Content -LiteralPath $scriptPath -Raw
    $core = [ScriptBlock]::Create($scriptText)

    $mode = $env:ILS_MODE
    if ([string]::IsNullOrWhiteSpace($mode)) {
        throw 'ILS_MODE environment variable is missing.'
    }

    if ($mode -eq 'Install') {
        & $core -Mode Install -PrimaryUser $env:ILS_PRIMARY_USER -SecondaryUser $env:ILS_SECONDARY_USER
    }
    elseif ($mode -eq 'Uninstall') {
        & $core -Mode Uninstall
    }
    else {
        throw "Unsupported mode: $mode"
    }

    exit 0
}
catch {
    Write-Host $_.Exception.Message
    if ($_.ScriptStackTrace) {
        Write-Host $_.ScriptStackTrace
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOG_FILE)) {
        Add-Content -LiteralPath $env:LOG_FILE -Value $_.Exception.ToString()
        if ($_.ScriptStackTrace) {
            Add-Content -LiteralPath $env:LOG_FILE -Value $_.ScriptStackTrace
        }
    }
    exit 1
}
