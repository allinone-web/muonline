param(
    [switch]$NoBrowser,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'MuDiagnostics.Web\MuDiagnostics.Web.csproj'

if (-not $NoBuild) {
    dotnet build $project -c Release
}

$arguments = @('run', '--project', $project, '-c', 'Release', '--no-build')
if ($NoBrowser) {
    $arguments += '--'
    $arguments += '--no-browser'
}

dotnet @arguments
