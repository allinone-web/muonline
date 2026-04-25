# context-mode + RTK routing — mandatory

Use context-mode and RTK together; they solve different parts of the same problem.

- context-mode is the outer routing layer for exploration, analysis, broad search, build/test logs, and any command that may produce more than ~20 lines.
- RTK is the shell-output filter. When a context-mode shell command runs an RTK-supported command, put `rtk ...` inside the context-mode command.
- Direct `rtk ...` is for short exact commands, compact diffs, targeted reads, and final verification where the RTK summary is the desired output.
- Do not replace context-mode with direct RTK for broad repo investigation. Prefer `ctx_batch_execute` with commands like `rtk grep ...`, `rtk diff ...`, or `rtk dotnet build ...`.
- If context-mode tools are not visible in a fresh Codex session, use `tool_search` for `context-mode` once, then continue with `ctx_batch_execute`, `ctx_execute`, `ctx_search`, and `ctx_stats`.
- Do not use `rtk proxy sed`, raw `rg`, raw `grep`, or raw build output as the default exploration path. Use them only for small nearby source context or exact raw output.

Good combined examples:

```text
ctx_batch_execute(commands: [
  { label: "Relevant render symbols", command: "rtk grep \"DrawAfter|IsTransparent\" Client.Main" },
  { label: "Focused diff", command: "rtk diff -- Client.Main/Controls/WorldControl.cs" }
], queries: ["transparent render order", "draw after pass"])
```

```text
ctx_execute(language: "shell", code: "rtk dotnet build ./MuMac/MuMac.csproj -c Debug -p:UsePrebuiltContent=true")
```

@RTK.md

# AGENTS.md

## Project overview
- MuOnline client clone built with .NET 10 and MonoGame 3.8.x.
- Uses Season 6 network protocol.
- Uses Season 20 (1.20.61) game assets for client data loading.
- Primary engineering focus: performance and cross-platform compatibility (Windows DX/GL, Linux, macOS, Android).
- Educational/research project: do not commit proprietary game data or secrets.

## Repository layout
- `Client.Main/`: shared game client core (rendering, scenes, UI, networking, objects).
- `Client.Data/`: readers/parsers for MU formats (BMD, ATT, MAP, OZB/OZG, CWS, OBJS, LANG, CAP).
- `MuWinDX/`, `MuWinGL/`, `MuLinux/`, `MuMac/`, `MuAndroid/`, `MuIos/`: platform heads.
- `Client.Editor/`: tooling.
- `MuOnline.sln`: solution entry point.

## Key paths (one-line map)
- `MuOnline.sln` — solution root for opening/building all heads from one entrypoint.
- `Client.Main/` — shared runtime core: scenes, rendering, controls, networking, and game objects.
- `Client.Data/` — MU file format readers and parsers consumed by the runtime loaders.
- `Client.Main/MuGame.cs` — bootstrap, config loading, scene lifecycle, and main-thread scheduling gateway.
- `Client.Main/Constants.cs` — global runtime flags and `DataPath`; first check for missing assets.
- `Client.Main/appsettings.json` — host/port, protocol/client settings, graphics, and logging levels.
- `Client.Main/MGContent/Content.mgcb` — authoritative content manifest for textures, fonts, and effects.
- `Client.Main/MGContent/PrebuiltContent/DesktopGL/Content/` — fallback prebuilt `.xnb` set used by macOS mode.
- `MuMac/MuMac.csproj` — macOS build rules including `UsePrebuiltContent`, `DetectWine`, and prebuilt validation.
- `MuWinDX/MuWinDX.csproj` — Windows DX head; requires `-p:MonoGameFramework=MonoGame.Framework.WindowsDX`.
- `MuWinGL/MuWinGL.csproj` — Windows GL head; requires `-p:MonoGameFramework=MonoGame.Framework.DesktopGL`.
- `MuLinux/MuLinux.csproj` — Linux DesktopGL head for non-Windows desktop validation.
- `Client.Main/Networking/` — packet routing, handlers, and service-layer protocol flow.
- `Client.Main/Scenes/` — scene implementations (`Load/Login/Select/Game`) and transition orchestration.
- `Client.Main/Objects/` — world entities (player/NPC/monster/items/effects) and rendering behavior.

## Dev environment tips
- Restore local tools first: `rtk err dotnet tool restore`.
- Configure local data path in `Client.Main/Constants.cs` (`DataPath`) before running.
- Configure server and graphics settings in `Client.Main/appsettings.json`.
- Windows heads must always pass `-p:MonoGameFramework=...`:
  - DX11: `MonoGame.Framework.WindowsDX`
  - OpenGL: `MonoGame.Framework.DesktopGL`

## Build commands
- Windows DX11:
  - `rtk dotnet build ./MuWinDX/MuWinDX.csproj -c Debug -p:MonoGameFramework=MonoGame.Framework.WindowsDX`
- Windows OpenGL:
  - `rtk dotnet build ./MuWinGL/MuWinGL.csproj -c Debug -p:MonoGameFramework=MonoGame.Framework.DesktopGL`
- Linux:
  - `rtk dotnet build ./MuLinux/MuLinux.csproj -c Debug`
- macOS:
  - `rtk dotnet build ./MuMac/MuMac.csproj -c Debug`
- macOS fallback (prebuilt content):
  - `rtk dotnet build ./MuMac/MuMac.csproj -c Debug -p:UsePrebuiltContent=true`

## Run commands
- Windows DX11:
  - `dotnet run --project ./MuWinDX/MuWinDX.csproj -f net10.0-windows -c Debug -p:MonoGameFramework=MonoGame.Framework.WindowsDX`
- Windows OpenGL:
  - `dotnet run --project ./MuWinGL/MuWinGL.csproj -f net10.0-windows -c Debug -p:MonoGameFramework=MonoGame.Framework.DesktopGL`
- Linux:
  - `dotnet run --project ./MuLinux/MuLinux.csproj -f net10.0 -c Debug`
- macOS:
  - `dotnet run --project ./MuMac/MuMac.csproj -f net10.0 -c Debug`

## Definition of done
- Change in `Client.Main/*.cs`:
  - Build at least one affected desktop head.
  - If rendering/UI/gameplay-facing: also smoke-check login/scene/render path.
- Change in shaders/effects (`Client.Main/MGContent/*.fx`):
  - Validate Windows DX and Windows GL builds.
  - Confirm no effect parameter mismatches at runtime.
- Change in packet handling/network flow:
  - Validate build for touched head.
  - Verify scene/UI mutations are marshaled via `MuGame.ScheduleOnMainThread`.
- Change in macOS content/build flow:
  - Validate both modes: default and `-p:UsePrebuiltContent=true`.
  - Ensure prebuilt content set is complete and synchronized with `Content.mgcb`.

## Change impact map
- If you touch `Client.Main/MGContent/Content.mgcb`, also review:
  - `Client.Main/MGContent/PrebuiltContent/DesktopGL/Content/*.xnb` completeness.
  - `MuMac/MuMac.csproj` `ValidatePrebuiltContent` required file list.
- If you touch `Client.Main/Constants.cs`, also review:
  - `DataPath` expectations.
  - quality/performance toggles used by rendering path.
- If you touch `Client.Main/appsettings.json`, also review:
  - config binding expectations in `MuGame.cs`.
- If you touch platform head `.csproj`, also review:
  - content pipeline references and copy/publish behavior.

## Fast checks
- Verify mgcb/prebuilt parity count:
  - Use `ctx_execute` with a small script for counts, or `rtk proxy` only if exact shell output is required.
- Validate prebuilt mode contract on macOS project:
  - `rtk err dotnet msbuild ./MuMac/MuMac.csproj -nologo -t:ValidatePrebuiltContent -p:UsePrebuiltContent=true`
- Verify wine detection path on non-Windows:
  - `rtk err dotnet msbuild ./MuMac/MuMac.csproj -nologo -t:DetectWine -p:UsePrebuiltContent=false`

## Content and shaders
- Content is built from `Client.Main/MGContent/Content.mgcb`.
- `MuMac` supports prebuilt fallback content in:
  - `Client.Main/MGContent/PrebuiltContent/DesktopGL/Content`
- Keep prebuilt `.xnb` files synchronized with `Content.mgcb` entries.
- Expected DesktopGL prebuilt files:
  - `AlphaRGB.xnb`, `Arial.xnb`, `Background.xnb`, `Bubbles.xnb`, `DynamicLighting.xnb`, `FXAA.xnb`, `GammaCorrection.xnb`, `Grass.xnb`, `ItemMaterial.xnb`, `MonsterMaterial.xnb`, `NotoKR.xnb`, `Shadow.xnb`, `WaterSplashParticle.xnb`.
- When touching shaders/effects, validate DX and GL heads.

## Code style
- C# 10, 4-space indentation, Allman braces.
- Naming: `PascalCase` for types/methods, `camelCase` for locals/fields, `Async` suffix for async methods.
- Prefer async/await for networking and I/O paths.
- Avoid hardcoded gameplay/config IDs when databases/config already exist.

## Threading and safety
- Rendering and scene/UI updates are main-thread only.
- Network handlers may run off main thread.
- Marshal scene/UI mutations with `MuGame.ScheduleOnMainThread`.

## Testing instructions
- There is no full automated test suite yet.
- If you modify shader/rendering code, test both Windows DX and Windows GL when possible.
- If you modify macOS content flow, validate both default mode and `UsePrebuiltContent=true` fallback.

## PR instructions
- Keep commits focused and concise.
- In PR description include:
  - intent and affected platforms,
  - exact build/run commands executed,
  - config expectations (`DataPath`, `appsettings.json`) when relevant.

## Do not commit
- Proprietary MU client data.
- Credentials, private endpoints, tokens, or secrets.
- Developer-specific absolute paths unless intentionally required and documented.
- Partial prebuilt content updates that break `Content.mgcb` parity.

## Common pitfalls and fixes
- Missing `-p:MonoGameFramework=...` on Windows builds causes wrong package/shader behavior.
- Incorrect `DataPath` causes missing assets or black screens.
- Updating UI/state directly from network thread can crash; marshal to main thread.
- Mixing DX/GL package expectations between heads leads to restore/build conflicts.
- On environments with restricted internet access, NuGet restore can fail (`NU1301`). Verify network/proxy before troubleshooting code changes.

## Security and data handling
- Never commit credentials, private endpoints, or local absolute paths that are environment-specific.
