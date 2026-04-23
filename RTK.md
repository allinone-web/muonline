# RTK - Rust Token Killer (Codex CLI)

Use RTK for shell commands in this repository.

## Rule

Always prefix shell commands with `rtk` when RTK supports the command.

Examples:

```bash
rtk git status --short
rtk rg "Pattern" Client.Main
rtk ls Client.Main/Objects
```

For commands that need exact raw output, unsupported commands, or extra native flags that RTK does not handle, use `rtk proxy` instead of running the command directly:

```bash
rtk proxy git -c safe.directory=/mnt/d/nowe/muonline status --short
rtk proxy dotnet msbuild ./MuMac/MuMac.csproj -nologo -t:ValidatePrebuiltContent
```

For .NET builds in this repository, prefer `rtk proxy dotnet` so MSBuild properties are passed through exactly:

```bash
rtk proxy dotnet restore Client.Main/Client.Main.csproj /p:RestoreFallbackFolders= /p:DisableImplicitNuGetFallbackFolder=true
rtk proxy dotnet build Client.Main/Client.Main.csproj --no-restore /p:RestoreFallbackFolders= /p:DisableImplicitNuGetFallbackFolder=true /consoleloggerparameters:NoSummary -v:minimal
```

If using `ctx_execute` or `ctx_batch_execute`, put the `rtk ...` command inside the shell script whenever the command is RTK-supported.

## Verification

```bash
rtk --version
rtk init --show --codex
rtk git status --short
rtk gain
```
