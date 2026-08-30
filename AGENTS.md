# Repo Rules

- Apply `karpathy-guidelines` first for all code edits: small, surgical, verified.
- Use context-mode for exploration and analysis: `ctx_batch_execute`, `ctx_search`, `ctx_execute`; avoid raw large output.
- Headroom is configured as the repo MCP in `.codex/config.toml`; provider settings stay in `/root/.codex/config.toml`.
- Caveman mode active via Claude Code plugin; or is available as project skills under `.agents/skills`; keep responses short when active.
- For any MonoGame/client/rendering/performance work, MUST use `.codex/skills/monogame/SKILL.md` or `.agents/skills/monogame/SKILL.md` plus relevant MuOnline skills.
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
- **Before touching any UI drawing code, read [docs/UI繪製陷阱.md](./docs/UI繪製陷阱.md).** A draw exception is swallowed by `MuGame.Draw` and re-presents the last good frame, so the failure looks like "the client froze" with no error at all: screen static, every UI element gone, but buttons still click and play sounds. Never call `spriteBatch.Begin()/End()` directly, never call `gd.SetRenderTarget` directly (use `SpriteBatchScope.BeginRenderTarget`), and verify on device with `tools/mu ios --committed --console` — the run is only clean if no `[DrawEx]` line appears.
- For visual/effect parity, verify against `SourceMain5.2` evidence before inventing MonoGame approximations.

# Architecture Facts (updated 2026-06-06)

- `ModelObject.Position` is Vector3 (world units). `WalkerObject.Location` is Vector2 (tile coords). Convert: `tile * TERRAIN_SCALE + TERRAIN_SCALE / 2f`.
- `GameSceneSkillController` — handles all skill usage: right-click, area skills, teleport, Nova charging. Cooldowns tracked via `SkillCooldownTracker` static class.
- `SkillDatabase` — static utility loading `skill.bmd`. Returns `SkillBMD` with mana cost, damage, range, requirements, delay, mastery type.
- `BuffManager` — central buff state via `ProcessMagicEffectStatus(playerId, effectId, isActive)`. Fires `BuffStateChanged` event consumed by `BuffEffectController` for visual effects.
- `BuffEffectController` — subscribes to BuffManager, applies Swell (scale ×1.25) via `WalkerObject.Scale`, aura colors for Damage/Defense/ManaShield/Poison/Ice.
- `PacketRouter` — manually instantiates handlers with dependencies. BuffManager and PetManager created here; add new managers here and pass to relevant handlers.
- `CharacterState.Class` is `CharacterClassNumber` enum (DarkKnight=16, DarkWizard=0, FairyElf=32, etc.). Knight classes: DarkKnight, BladeKnight, BladeMaster.
- SourceMain energy formula for skills: `20 + (RequiredEnergy * Level * 4 / 100)`. Knight gets `10 + ...`. Summon Explosion/Requiem: `20 + (Energy * Level * 3 / 100)`.
- Pet system: `PetObject` extends `ModelObject`, follows owner using `TilePosition` (Vector2 from WorldPosition). `PetManager` tracks per-owner via `_activePets` dict.
