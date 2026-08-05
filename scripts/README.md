# PowerShell scripts

## Publishing

- `publish/android-performance.ps1` - Android ARM64 performance packages.
- `publish/windows-ci.ps1` - Windows backend builds used by CI.
- `publish/windows-performance.ps1` - validated DirectX performance release.

## Diagnostics

- `diagnostics/publish.ps1` - publish the diagnostics web service.
- `diagnostics/start.ps1` - build and run the diagnostics web service.

Run scripts from the repository root, for example:

```powershell
./scripts/publish/windows-performance.ps1
./scripts/diagnostics/start.ps1
```
