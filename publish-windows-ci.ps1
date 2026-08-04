param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("OpenGL", "DirectX")]
    [string]$Backend,

    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$settings = if ($Backend -eq "OpenGL") {
    @{
        Project = Join-Path $repoRoot "MuWinGL/MuWinGL.csproj"
        Framework = "MonoGame.Framework.DesktopGL"
        DefaultOutput = Join-Path $repoRoot "publish-OpenGL"
    }
}
else {
    @{
        Project = Join-Path $repoRoot "MuWinDX/MuWinDX.csproj"
        Framework = "MonoGame.Framework.WindowsDX"
        DefaultOutput = Join-Path $repoRoot "publish-DirectX"
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = $settings.DefaultOutput
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$projectDirectory = Split-Path -Parent $settings.Project
$toolManifest = Join-Path $projectDirectory ".config/dotnet-tools.json"

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Restoring MonoGame tools for $Backend..."
Invoke-DotNet tool restore --tool-manifest $toolManifest

Write-Host "Restoring $Backend project graph..."
Invoke-DotNet restore $settings.Project `
    -r $RuntimeIdentifier `
    -p:MonoGameFramework=$($settings.Framework) `
    -p:RestoreMonoGameTools=false

Write-Host "Publishing $Backend performance release..."
Invoke-DotNet publish $settings.Project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    -o $OutputDirectory `
    --no-restore `
    --disable-build-servers `
    -p:MonoGameFramework=$($settings.Framework) `
    -p:RestoreMonoGameTools=false

Write-Host "Published $Backend to $OutputDirectory"
