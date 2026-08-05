param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("OpenGL", "DirectX", "DirectX11", "DirectX12", "DesktopVK")]
    [string]$Backend,

    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$normalizedBackend = if ($Backend -eq "DirectX") { "DirectX11" } else { $Backend }
$settings = switch ($normalizedBackend) {
    "OpenGL" {
        @{ Project = Join-Path $repoRoot "MuWinGL/MuWinGL.csproj"; Framework = "MonoGame.Framework.DesktopGL"; Platform = "DesktopGL"; DefaultOutput = Join-Path $repoRoot "publish-OpenGL" }
    }
    "DirectX11" {
        @{ Project = Join-Path $repoRoot "MuWinDX/MuWinDX.csproj"; Framework = "MonoGame.Framework.WindowsDX"; Platform = "Windows"; DefaultOutput = Join-Path $repoRoot "publish-DirectX11" }
    }
    "DirectX12" {
        @{ Project = Join-Path $repoRoot "MuWinDX12/MuWinDX12.csproj"; Framework = "MonoGame.Framework.Native"; Platform = "WindowsDX12"; DefaultOutput = Join-Path $repoRoot "publish-DirectX12" }
    }
    "DesktopVK" {
        @{ Project = Join-Path $repoRoot "MuDesktopVK/MuDesktopVK.csproj"; Framework = "MonoGame.Framework.Native"; Platform = "DesktopVK"; DefaultOutput = Join-Path $repoRoot "publish-DesktopVK" }
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

Write-Host "Restoring MonoGame tools for $normalizedBackend..."
Invoke-DotNet tool restore --tool-manifest $toolManifest

Write-Host "Restoring $normalizedBackend project graph..."
Invoke-DotNet restore $settings.Project `
    -r $RuntimeIdentifier `
    -p:MonoGameFramework=$($settings.Framework) `
    -p:MonoGamePlatform=$($settings.Platform) `
    -p:RestoreMonoGameTools=false

Write-Host "Publishing $normalizedBackend performance release..."
Invoke-DotNet publish $settings.Project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    -o $OutputDirectory `
    --no-restore `
    --disable-build-servers `
    -p:MonoGameFramework=$($settings.Framework) `
    -p:MonoGamePlatform=$($settings.Platform) `
    -p:RestoreMonoGameTools=false

Write-Host "Published $normalizedBackend to $OutputDirectory"
