# AGENTS.md

## Scope
- This file applies to `Client.Main/Objects/Effects/` and its `Skills/` subfolder.
- Use it together with the repository root `AGENTS.md`; the closest file wins when instructions conflict.

## Folder purpose
- This folder contains runtime visual effects used by the client world: billboards, particles, composite spell effects, floating text, and skill-to-effect factories.
- Effects here are gameplay-facing and render-path sensitive. Favor stable frame time and predictable cleanup over visual complexity.

## Existing patterns to copy
- Use `SpriteObject` for simple billboard effects backed by one texture and light pulse logic.
  Example: `FlareEffect.cs`, `Spark03Effect.cs`, `LightEffect.cs`.
- Use `EffectObject` for composite effects that orchestrate sprites, particles, child objects, and dynamic lights.
  Example: `ScrollOfFireBallEffect.cs`, `ScrollOfInfernoEffect.cs`, `TwistingSlashEffect.cs`, `LevelUpEffect.cs`.
- Use `ModelObject` for 3D sub-parts attached to an effect when a billboard is not enough.
  Example: `FireBallCoreModel.cs`, nested model classes inside `DeathStabEffect.cs` and `ScrollOfIceEffect.cs`.
- Use `Skills/*.cs` only as lightweight factories that translate skill context into spawned world effects.
  Example: `Skills/FireBallSkillEffect.cs`.

## How to choose the base class
- Pick `SpriteObject` when the effect is a single texture projected in screen space and can rely on the built-in sprite draw path.
- Pick `EffectObject` when the effect needs custom `LoadContent`, custom `Draw`, fixed-size particle storage, child objects, or lifetime orchestration.
- Pick `ModelObject` only for reusable 3D pieces or nested sub-effects that need mesh animation, blend meshes, or parent-bone attachment.
- Do not put skill dispatch logic into the main effect class; keep that in `Skills/`.

## Creation rules
- Follow existing naming: `<EffectName>Effect.cs` for runtime visuals and `<EffectName>SkillEffect.cs` for skill factories.
- Keep new effects self-contained. A reader should understand spawn, update, draw, and cleanup from one file unless a reusable helper is clearly justified.
- Prefer `sealed` for concrete effect implementations unless inheritance is intentional.
- Use `#nullable enable` in newer files when the file already follows the newer nullable-aware style.
- Set render-related flags explicitly in the constructor when relevant:
  `BlendState`, `DepthState`, `IsTransparent`, `AffectedByTransparency`, `LightEnabled`, `RenderShadow`.
- Define `BoundingBoxLocal` for composite effects so culling and visibility behave predictably.
- Use constants for tuning values such as durations, speeds, particle counts, radii, and texture paths.

## Content loading
- Preload textures in `LoadContent()` with `TextureLoader.Instance.Prepare(...)` and cache `Texture2D` references once.
- Never perform texture lookup, content preparation, or other expensive asset discovery in `Draw()`.
- When a texture is optional, fall back to `GraphicsManager.Instance.Pixel` instead of crashing.
- If a new effect requires a new content file, also update the relevant content manifest and any platform-specific prebuilt content expectations described in the root `AGENTS.md`.

## Update and draw guidelines
- Keep `Update()` and `Draw()` allocation-free. Do not introduce per-frame LINQ, temporary lists, or per-frame `new Random()`.
- Use fixed-size arrays plus small structs for particles and transient state.
  Example: `ScrollOfFireBallEffect.cs`.
- Use `MuGame.Random` for deterministic shared randomness instead of creating local random generators.
- Guard on object readiness before doing expensive work.
- When a custom sprite pass is needed, use `SpriteBatchScope` and respect whether the batch is already begun.
- If you project world positions manually, clamp or reject invalid depth values before drawing.
- Prefer simple math helpers and cached constants over opaque magic numbers spread across methods.

## Lifetime and cleanup
- Effects must clean up after themselves.
- If the effect adds `DynamicLight` instances, remove them in `Dispose()`.
- If the effect spawns temporary children or world objects, define clearly who owns removal.
- Use the existing removal pattern:
  remove from `Parent.Children` when attached, otherwise call `World?.RemoveObject(this)`, then `Dispose()`.
- Do not leave lights, child objects, or references alive after the effect lifetime ends.

## Skill integration
- New skill-driven visuals usually require two files: the runtime effect in `Client.Main/Objects/Effects/` and the skill factory in `Client.Main/Objects/Effects/Skills/`.
- Each factory must implement `ISkillVisualEffect` and be decorated with `SkillVisualEffectAttribute`.
- Keep skill factories thin: resolve caster hand position, target position, or fallback positions, then return the effect instance.
- Do not manually edit a central registration table; `SkillVisualEffectRegistry` auto-discovers decorated factories.

## Performance rules
- This folder is hot-path code. Small regressions in `Update()` or `Draw()` multiply quickly when many effects are active.
- Prefer fixed particle caps over unbounded growth.
- Avoid per-frame reflection, string building, logging spam, or repeated asset queries.
- Reuse child objects and helper state where practical instead of rebuilding them each frame.
- Favor straightforward math and predictable branches over generalized abstractions.

## Validation
- For documentation-only changes in this folder, no build is required.
- For code changes in this folder, build at least one affected desktop head.
- If the change affects rendering behavior, validate both:
  `dotnet build ./MuWinDX/MuWinDX.csproj -c Debug -p:MonoGameFramework=MonoGame.Framework.WindowsDX`
  `dotnet build ./MuWinGL/MuWinGL.csproj -c Debug -p:MonoGameFramework=MonoGame.Framework.DesktopGL`
- Smoke-check the actual spawn path in game when possible:
  casting the skill, seeing the effect appear at the expected origin, and verifying it cleans itself up.
- If new textures or effect assets are added, verify content pipeline coverage as described in the root `AGENTS.md`.

## Common mistakes to avoid
- Do not put expensive setup in `Draw()`.
- Do not leave `DynamicLight` objects registered after the effect ends.
- Do not update unrelated UI or scene state from effect code.
- Do not introduce hidden ownership; parented child effects should have obvious cleanup rules.
- Do not create a new visual effect factory without the matching `SkillVisualEffectAttribute`, or it will never auto-register.
