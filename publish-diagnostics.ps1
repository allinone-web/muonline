param(
    [string]$Output = '.\artifacts\MuDiagnostics.Web',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'MuDiagnostics.Web\MuDiagnostics.Web.csproj'
$outputPath = Join-Path $root $Output

$arguments = @('publish', $project, '-c', 'Release', '-r', $Runtime, '-o', $outputPath)
$arguments += '--self-contained'
$arguments += $SelfContained.IsPresent.ToString().ToLowerInvariant()

dotnet @arguments
Write-Host "Published diagnostics service to: $outputPath"
