# RTK - Rust Token Killer (Codex CLI)

Use RTK for shell commands in this repository, but do not let RTK replace context-mode routing.

RTK filters command output. context-mode keeps raw data out of the conversation, indexes large outputs, and lets you search summaries. Use them together: context-mode outside, RTK inside shell commands.

## Rule

For broad exploration, analysis, noisy builds/tests, or anything that may exceed ~20 lines, use context-mode first:

```text
ctx_batch_execute(commands: [
  { label: "Search render path", command: "rtk grep \"DrawAfter|IsTransparent\" Client.Main" },
  { label: "Current diff", command: "rtk diff -- Client.Main/Controls/WorldControl.cs" }
], queries: ["transparent render order", "draw pass"])
```

For short exact shell commands, prefix with `rtk` when RTK supports the command. Prefer high-compression RTK commands over `rtk proxy`.

Good defaults:

```bash
rtk --ultra-compact git status --short
rtk diff -- Client.Main/Objects/ModelObject.cs
rtk grep "CanUseMonsterCrowdInstancing" Client.Main/Objects
rtk read Client.Main/Objects/ModelObject.Instancing.cs
rtk dotnet build ./MuMac/MuMac.csproj -c Debug -p:UsePrebuiltContent=true
rtk err dotnet build ./MuMac/MuMac.csproj -c Debug -p:UsePrebuiltContent=true
```

## Routing

- context-mode has priority for multi-command gathering, searching, counting, summarizing, parsing, build/test logs, and any output that could be large.
- Inside `ctx_execute` or `ctx_batch_execute`, still use `rtk ...` whenever the shell command is RTK-supported.
- Direct `rtk ...` is appropriate for compact final checks such as `rtk diff`, `rtk --ultra-compact git status --short`, `rtk git diff --check`, `rtk read` for nearby edit context, or `rtk dotnet build` when the RTK summary is enough.
- For file search, prefer `rtk grep` for simple patterns. Avoid very large alternation regexes that make RTK fall back to raw `rg`; split them into smaller `rtk grep` calls or use `ctx_batch_execute`.
- For file reading, prefer `rtk read <file>` for compact context. Use `ctx_execute_file` when analyzing or summarizing a file. Use `sed` only when editing requires exact nearby source lines; wrap it as `rtk proxy sed`.
- For diffs, prefer `rtk diff` for review and `rtk git diff --check` for whitespace validation. Use `rtk git diff -- <path>` only when exact patch context is needed.
- For .NET builds, prefer `rtk dotnet build` or `rtk err dotnet build`. Use `rtk proxy dotnet` only if RTK mishandles MSBuild properties or exact raw output is required.
- For unsupported commands or exact raw output, use `rtk proxy <command>` instead of running the command directly.
- If using `ctx_execute` or `ctx_batch_execute`, put the `rtk ...` command inside the shell script whenever the command is RTK-supported.

## Avoid

These patterns usually produce low RTK savings:

```bash
rtk proxy dotnet build ...
rtk proxy sed -n ...
rtk rg "large|alternation|regex|..." ...
rtk git diff --stat
rtk git status --short
```

They are allowed when appropriate, but do not use them as the default exploration path.

## Diagnostics

Run these periodically when token savings look low:

```bash
rtk gain --project --history
rtk gain --project --failures
rtk discover
rtk session
```

## Verification

```bash
rtk --version
rtk init --show --codex
rtk --ultra-compact git status --short
rtk gain --project
```
