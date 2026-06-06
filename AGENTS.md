# Repo Rules

- Apply `karpathy-guidelines` first for all code edits: small, surgical, verified.
- Use context-mode for exploration and analysis: `ctx_batch_execute`, `ctx_search`, `ctx_execute`; avoid raw large output.
- Headroom is configured as the repo MCP in `.codex/config.toml`; provider settings stay in `/root/.codex/config.toml`.
- Caveman is available as project skills under `.agents/skills`; keep responses short when active.
- For any MonoGame/client/rendering/performance work, MUST use `.agents/skills/monogame/SKILL.md` plus relevant MuOnline skills.
- Verify changes with the narrowest useful check:
  `dotnet build ./MuWinGL/MuWinGL.csproj -c Debug -p:MonoGameFramework=MonoGame.Framework.DesktopGL --nologo`

# Game Client Facts

- World coordinates use `Vector3(X, Y, Z)` with `Z` as height.
- Terrain is `256 x 256` tiles; one tile is `Constants.TERRAIN_SCALE == 100f` world units.
- Tile/network coordinates convert to world center as `tile * TERRAIN_SCALE + TERRAIN_SCALE / 2f`; ground-level Z is usually `0f`.
- Camera, visibility, wind, terrain, and world object placement use X/Y for map plane and Z for vertical offsets.
- Default camera constants live in `Client.Main/Constants.cs`; yaw/pitch are radians via `MathHelper.ToRadians`.
- Runtime data loads from `Constants.DataPath`, defaulting to `AppDomain.CurrentDomain.BaseDirectory/Data`.
- Object transform flow: change `Position`, `Angle`, or `Scale`; `WorldObject` recalculates `WorldPosition`.
- Network handlers can run off the render thread; scene/UI/world mutations should go through `MuGame.ScheduleOnMainThread`.
- SpriteBatch state should be managed with `SpriteBatchScope` when nesting or restoring graphics state.
- For visual/effect parity, verify against `SourceMain5.2` evidence before inventing MonoGame approximations.
