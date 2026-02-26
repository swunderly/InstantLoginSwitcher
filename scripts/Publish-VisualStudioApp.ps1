param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [string]$OutputDir = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\InstantLoginSwitcher.App\InstantLoginSwitcher.App.csproj'

if (-not (Test-Path -LiteralPath $appProject)) {
    throw "App project not found: $appProject"
}

$publishRoot = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $repoRoot 'artifacts\publish\InstantLoginSwitcher.App'
}
else {
    $OutputDir
}

$publishFlavor = if ($SelfContained) { 'selfcontained' } else { 'framework' }
$publishDir = Join-Path $publishRoot ("{0}-{1}-{2}" -f $Configuration, $Runtime, $publishFlavor)

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

$arguments = @(
    'publish',
    $appProject,
    '-c', $Configuration,
    '-r', $Runtime,
    '-o', $publishDir,
    '/p:PublishSingleFile=true',
    '/p:IncludeNativeLibrariesForSelfExtract=true',
    '/p:PublishTrimmed=false',
    '/p:DebugType=None',
    '/p:DebugSymbols=false'
)

if ($SelfContained) {
    $arguments += '/p:SelfContained=true'
}
else {
    $arguments += '/p:SelfContained=false'
}

Write-Host "Running: dotnet $($arguments -join ' ')" -ForegroundColor Cyan
dotnet @arguments

$readmeSource = Join-Path $repoRoot 'README.md'
if (Test-Path -LiteralPath $readmeSource) {
    Copy-Item -LiteralPath $readmeSource -Destination (Join-Path $publishDir 'README.md') -Force
}

Write-Host ''
Write-Host 'Publish completed.' -ForegroundColor Green
Write-Host "Output: $publishDir"
