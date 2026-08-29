using System;
using System.Collections.Generic;
using Client.Data.ATT;
using Client.Main.Controls;
using Client.Main.Controls.UI.Game.Hud;
using Client.Main.Controls.UI.Game.Skills;
using Client.Main.Core.Utilities;
using Client.Main.Objects;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Objects.Pets;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets;
using MUnique.OpenMU.Network.Packets.ClientToServer;

namespace Client.Main.Scenes
{
    internal sealed class GameSceneSkillController
    {
        private const ushort TeleportSkillId = 6;
        private const ushort TwisterSkillId = 8;
        private const ushort HellFireSkillId = 10;
        private const ushort InfernoSkillId = 14;
        private const ushort EvilSpiritSkillId = 9;
        private const ushort NovaSkillId = 40;
        private const ushort NovaStartSkillId = 58;
        private const ushort DarkRavenCommandFirstSkillId = 120;
        private const ushort DarkRavenCommandLastSkillId = 123;

        private readonly GameScene _scene;
        private readonly ModernBottomHud _hud;
        private readonly ILogger _logger;
        private readonly Func<PlayerObject, bool> _isDuelAttackTarget;

        private Core.Client.SkillEntryState _pendingSkill;
        private ushort _pendingSkillTargetId;
        private Vector2 _pendingSkillTargetLocation;
        private bool _pendingSkillHasLocation;
        private uint _pendingSkillRange;
        private bool _pendingSkillIsArea;
        private bool _pendingSkillTargetIsPlayer;
        private readonly Dictionary<ushort, double> _nextSkillAllowedMs = new();
        private byte _nextAreaSkillAnimationCounter;
        private bool _novaCharging;

        /// <summary>
        /// 最近一次出手失敗的原因，供手機的技能鈕顯示。
        ///
        /// 桌面失敗時只寫 Debug 記錄 —— 玩家看得到滑鼠、看得到目標、看得到資源條，
        /// 大致猜得出來。手機上按鈕就在拇指底下，失敗只閃一下紅色，
        /// 玩家完全無從得知是沒目標、魔力不足、AG 不足還是在冷卻。
        /// </summary>
        public string LastFailureReason { get; private set; }

        public GameSceneSkillController(
            GameScene scene,
            ModernBottomHud hud,
            ILogger logger,
            Func<PlayerObject, bool> isDuelAttackTarget)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _hud = hud ?? throw new ArgumentNullException(nameof(hud));
            _logger = logger;
            _isDuelAttackTarget = isDuelAttackTarget ?? (_ => false);
        }

        public void Update()
        {
            UpdatePendingSkill();
            UpdateNovaState();
        }

        public void ClearPending()
        {
            // 換地圖／換角色／死亡都會走到這裡。SkillCooldownTracker 是 static，
            // 不清掉的話冷卻會跨角色留下來 —— 地裂 62 的 10 秒、龍斬 265 的 3 秒
            // 會讓剛換過去的角色莫名其妙按不動。ResetAll 原本全專案沒有任何呼叫者。
            Core.Client.SkillCooldownTracker.ResetAll();
            _nextSkillAllowedMs.Clear();

            ClearPendingSkill();
            if (_scene.World is WalkableWorldControl world && _scene.Hero != null)
                ForceReleaseNovaCharge(world, _scene.Hero);
            else
                _novaCharging = false;
        }

        public void NotifyLocalSkillAnimation(ushort skillId)
        {
            if (skillId == NovaStartSkillId)
            {
                _novaCharging = true;
                return;
            }

            if (skillId == NovaSkillId)
            {
                _novaCharging = false;
            }
        }

        public void HandleRightClickSkillUsage()
        {
            var mouse = MuGame.Instance.Mouse;
            var prevMouse = MuGame.Instance.PrevMouseState;
            bool rightPressed = mouse.RightButton == ButtonState.Pressed;
            bool rightJustPressed = rightPressed && prevMouse.RightButton == ButtonState.Released;
            bool rightJustReleased = !rightPressed && prevMouse.RightButton == ButtonState.Pressed;
            bool leftJustPressed = mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released;

            var skill = _hud.SelectedSkill;
            var hero = _scene.Hero;
            var walkableForSkills = _scene.World as WalkableWorldControl;

            if (_novaCharging && (rightJustReleased || leftJustPressed))
            {
                TryReleaseNovaCharge(hero, walkableForSkills);
                return;
            }

            if (_scene.IsMouseInputConsumedThisFrame)
                return;

            if (IsMouseOverUi())
                return;

            // Allow continuous skill usage while holding right mouse button
            // The cooldown system (TryConsumeSkillDelay) will rate-limit the casting
            if (!rightPressed)
                return;

            if (skill == null)
                return;

            if (hero == null || hero.IsDead || walkableForSkills == null)
                return;

            if (IsDarkRavenCommandSkill(skill.SkillId))
            {
                TryUseDarkRavenCommand(skill.SkillId, hero, rightJustPressed);
                return;
            }

            if (skill.SkillId == NovaSkillId)
            {
                TryStartNovaCharge(skill, hero, walkableForSkills, rightJustPressed);
                return;
            }

            // Check if player is in SafeZone
            var terrainFlags = walkableForSkills.Terrain.RequestTerrainFlag((int)hero.Location.X, (int)hero.Location.Y);
            if (terrainFlags.HasFlag(TWFlags.SafeZone))
            {
                _logger?.LogDebug("Cannot use skill in SafeZone");
                _scene.SetMouseInputConsumed();
                return;
            }

            ClearPendingSkill();
            uint allowedRange = SkillDatabase.GetSkillRange(skill.SkillId);

            if (skill.SkillId == TeleportSkillId)
            {
                var mouseTile = new Vector2(walkableForSkills.MouseTileX, walkableForSkills.MouseTileY);
                if (IsInSkillRange(mouseTile, allowedRange))
                {
                    UseAreaSkill(skill, 0, mouseTile);
                }
                else
                {
                    _logger?.LogDebug("Teleport target out of range. Target=({X},{Y}) Range={Range}",
                        mouseTile.X, mouseTile.Y, allowedRange);
                }

                _scene.SetMouseInputConsumed();
                return;
            }

            var hoveredTarget = GetHoveredSkillTarget();
            if (IsAreaSkill(skill.SkillId))
            {
                if (skill.SkillId == HellFireSkillId || skill.SkillId == InfernoSkillId || skill.SkillId == EvilSpiritSkillId)
                {
                    UseAreaSkill(skill);
                    _scene.SetMouseInputConsumed();
                    return;
                }

                var skillTarget = hoveredTarget;
                var mouseTile = new Vector2(walkableForSkills.MouseTileX, walkableForSkills.MouseTileY);
                if (skillTarget == null)
                {
                    if (IsInSkillRange(mouseTile, allowedRange))
                    {
                        UseAreaSkill(skill, 0, mouseTile);
                    }
                    else
                    {
                        QueueAreaSkillCast(skill, mouseTile, allowedRange);
                    }
                }
                else if (IsInSkillRange(skillTarget.Location, allowedRange))
                {
                    UseAreaSkill(skill, skillTarget.NetworkId);
                }
                else
                {
                    QueueSkillCast(skill, skillTarget, allowedRange, isAreaSkill: true);
                }
            }
            else if (SkillDatabase.IsSelfSkill(skill.SkillId))
            {
                // 自身／隊伍增益不需要滑鼠指到誰。原本沒有這條分支，
                // 於是防禦、生命增幅這類技能必須「指著一隻怪」才放得出來。
                UseSelfSkill(skill);
            }
            else
            {
                if (hoveredTarget is MonsterObject targetMonster)
                {
                    if (IsInSkillRange(targetMonster.Location, allowedRange))
                        UseSkillOnTarget(skill, targetMonster);
                    else
                        QueueSkillCast(skill, targetMonster, allowedRange, isAreaSkill: false);
                }
                else if (hoveredTarget is PlayerObject targetPlayer)
                {
                    if (IsInSkillRange(targetPlayer.Location, allowedRange))
                        UseSkillOnPlayerTarget(skill, targetPlayer);
                    else
                        QueueSkillCast(skill, targetPlayer, allowedRange, isAreaSkill: false);
                }
            }

            _scene.SetMouseInputConsumed();
        }

        private void TryUseDarkRavenCommand(ushort skillId, PlayerObject hero, bool rightJustPressed)
        {
            if (!rightJustPressed)
                return;

            if (hero.EquippedHelper?.Kind != FlyingHelperKind.DarkRaven)
            {
                _logger?.LogDebug("Dark Raven command ignored because Dark Raven is not equipped.");
                _scene.SetMouseInputConsumed();
                return;
            }

            PetCommandMode commandMode = (PetCommandMode)(skillId - DarkRavenCommandFirstSkillId);
            ushort targetId = 0xFFFF;
            if (commandMode == PetCommandMode.AttackTarget)
            {
                WalkerObject target = GetHoveredSkillTarget();
                if (target == null)
                {
                    _logger?.LogDebug("Dark Raven target command ignored because no valid target is hovered.");
                    _scene.SetMouseInputConsumed();
                    return;
                }

                targetId = target.NetworkId;
            }

            _ = MuGame.Network.GetCharacterService().SendDarkRavenCommandAsync(commandMode, targetId);
            _scene.SetMouseInputConsumed();
        }

        private void UpdateNovaState()
        {
            if (!_novaCharging)
                return;

            var hero = _scene.Hero;
            if (hero == null || hero.IsDead)
            {
                if (_scene.World is WalkableWorldControl deadWorld && hero != null)
                    ScrollOfNovaChargeEffect.StopForCaster(deadWorld, hero.NetworkId);

                _novaCharging = false;
                return;
            }

            var selectedSkill = _hud.SelectedSkill;
            if (selectedSkill?.SkillId == NovaSkillId)
                return;

            // If player switched skill while charging, stop local charging state and visuals.
            if (_scene.World is WalkableWorldControl world)
                ForceReleaseNovaCharge(world, hero);
            else
                _novaCharging = false;
        }

        private void TryStartNovaCharge(Core.Client.SkillEntryState skill, PlayerObject hero, WalkableWorldControl world, bool rightJustPressed)
        {
            if (_novaCharging || !rightJustPressed)
                return;

            var terrainFlags = world.Terrain.RequestTerrainFlag((int)hero.Location.X, (int)hero.Location.Y);
            if (terrainFlags.HasFlag(TWFlags.SafeZone))
            {
                _logger?.LogDebug("Cannot use Nova in SafeZone");
                _scene.SetMouseInputConsumed();
                return;
            }

            if (hero.IsAttackOrSkillAnimationPlaying())
                return;

            if (!TryConsumeSkillDelay(NovaSkillId))
                return;

            if (!HasResourcesForNovaStart())
                return;

            _novaCharging = true;
            ClearPendingSkill();

            var startAction = hero.GetSkillAction(NovaStartSkillId, isInSafeZone: false);
            hero.PlayAction((ushort)startAction);
            hero.TriggerVehicleSkillAnimation();

            ushort targetId = hero.NetworkId != 0 ? hero.NetworkId : (ushort)_scene.Hero.NetworkId;
            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(NovaStartSkillId, targetId);

            // Start local charging visuals immediately; server packets will refine stage.
            ScrollOfNovaChargeEffect.GetOrCreate(world, hero);

            _logger?.LogInformation("Started Nova charge (skill {SkillId} -> start {StartSkillId})", skill.SkillId, NovaStartSkillId);
            _scene.SetMouseInputConsumed();
        }

        private void TryReleaseNovaCharge(PlayerObject hero, WalkableWorldControl world)
        {
            if (!_novaCharging)
                return;

            _novaCharging = false;

            if (hero == null || hero.IsDead || world == null)
                return;

            var releaseAction = hero.GetSkillAction(NovaSkillId, isInSafeZone: false);
            hero.PlayAction((ushort)releaseAction);
            hero.TriggerVehicleSkillAnimation();

            ushort targetId = ResolveNovaReleaseTargetId(hero);
            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(NovaSkillId, targetId);

            ScrollOfNovaChargeEffect.StopForCaster(world, hero.NetworkId);

            _logger?.LogInformation("Released Nova charge with target {TargetId}", targetId);
            _scene.SetMouseInputConsumed();
        }

        private void ForceReleaseNovaCharge(WalkableWorldControl world, PlayerObject hero)
        {
            if (!_novaCharging || hero == null || hero.IsDead || world == null)
            {
                _novaCharging = false;
                return;
            }

            _novaCharging = false;

            ushort targetId = hero.NetworkId != 0 ? hero.NetworkId : ResolveNovaReleaseTargetId(hero);
            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(NovaSkillId, targetId);
            ScrollOfNovaChargeEffect.StopForCaster(world, hero.NetworkId);
        }

        private ushort ResolveNovaReleaseTargetId(PlayerObject hero)
        {
            var hoveredTarget = GetHoveredSkillTarget();
            if (hoveredTarget is MonsterObject monster && !monster.IsDead)
            {
                hero.FaceTowards(monster.Location, immediate: true);
                return monster.NetworkId;
            }

            if (hoveredTarget is PlayerObject player && !player.IsDead && _isDuelAttackTarget(player))
            {
                hero.FaceTowards(player.Location, immediate: true);
                return player.NetworkId;
            }

            return hero.NetworkId != 0 ? hero.NetworkId : _characterStateSafeIdFallback();

            ushort _characterStateSafeIdFallback()
            {
                var state = MuGame.Network?.GetCharacterState();
                return state?.Id ?? (ushort)0;
            }
        }

        private bool HasResourcesForNovaStart()
        {
            var characterState = MuGame.Network?.GetCharacterState();
            if (characterState == null)
                return true;

            ushort manaCost = SkillDatabase.GetSkillManaCost(NovaSkillId);
            ushort agCost = SkillDatabase.GetSkillAGCost(NovaStartSkillId);
            if (agCost == 0)
                agCost = SkillDatabase.GetSkillAGCost(NovaSkillId);

            if (characterState.CurrentMana < manaCost)
            {
                _logger?.LogDebug("Not enough mana for Nova charge start. Required: {Required}, Current: {Current}",
                    manaCost, characterState.CurrentMana);
                return false;
            }

            if (characterState.CurrentAbility < agCost)
            {
                _logger?.LogDebug("Not enough AG for Nova charge start. Required: {Required}, Current: {Current}",
                    agCost, characterState.CurrentAbility);
                return false;
            }

            return true;
        }

        private WalkerObject GetHoveredSkillTarget()
        {
            if (_scene.World != null)
            {
                MonsterObject targetedMonster = WorldHoverSystem.FindBestLiveMonster(
                    _scene.World.VisibleObjects,
                    MuGame.Instance.MouseRay,
                    _scene.World);
                if (targetedMonster != null)
                    return targetedMonster;
            }

            if (_scene.MouseHoverObject is MonsterObject monster)
            {
                if (!monster.IsDead && monster.World == _scene.World)
                    return monster;
                return null;
            }

            if (_scene.MouseHoverObject is PlayerObject player)
            {
                if (player != _scene.Hero &&
                    !player.IsDead &&
                    player.World == _scene.World &&
                    _isDuelAttackTarget(player))
                {
                    return player;
                }
            }

            return null;
        }

        private bool IsMouseOverUi()
        {
            return _scene.MouseHoverControl != null && _scene.MouseHoverControl != _scene.World;
        }

        private static bool IsAreaSkill(ushort skillId)
        {
            return SkillDatabase.IsAreaSkill(skillId);
        }

        private static bool IsDarkRavenCommandSkill(ushort skillId)
        {
            return skillId >= DarkRavenCommandFirstSkillId &&
                   skillId <= DarkRavenCommandLastSkillId;
        }

        private bool IsInSkillRange(Vector2 targetLocation, uint allowedRange)
        {
            var hero = _scene.Hero;
            if (hero == null)
                return false;

            return allowedRange == 0 || Vector2.Distance(hero.Location, targetLocation) <= allowedRange;
        }

        private void QueueSkillCast(Core.Client.SkillEntryState skill, WalkerObject target, uint allowedRange, bool isAreaSkill)
        {
            var hero = _scene.Hero;
            if (skill == null || target == null || hero == null)
                return;

            _pendingSkill = skill;
            _pendingSkillTargetId = target.NetworkId;
            _pendingSkillRange = allowedRange;
            _pendingSkillIsArea = isAreaSkill;
            _pendingSkillTargetIsPlayer = target is PlayerObject;

            MoveHeroTowardsTarget(target.Location, force: true);
        }

        private void UpdatePendingSkill()
        {
            var hero = _scene.Hero;
            if (_pendingSkill == null || hero == null || hero.IsDead)
            {
                ClearPendingSkill();
                return;
            }

            if (_pendingSkill.SkillId == HellFireSkillId || _pendingSkill.SkillId == InfernoSkillId || _pendingSkill.SkillId == EvilSpiritSkillId)
            {
                ClearPendingSkill();
                return;
            }

            if (_pendingSkill.SkillId == TeleportSkillId)
            {
                // Teleport is an instant skill; it shouldn't path towards the target.
                ClearPendingSkill();
                return;
            }

            if (_pendingSkillTargetId == 0 && !_pendingSkillHasLocation)
            {
                ClearPendingSkill();
                return;
            }

            if (_hud.SelectedSkill == null || _hud.SelectedSkill.SkillId != _pendingSkill.SkillId)
            {
                ClearPendingSkill();
                return;
            }

            if (_scene.World is not WalkableWorldControl walkableWorld)
            {
                ClearPendingSkill();
                return;
            }

            var terrainFlags = walkableWorld.Terrain.RequestTerrainFlag((int)hero.Location.X, (int)hero.Location.Y);
            if (terrainFlags.HasFlag(TWFlags.SafeZone))
            {
                ClearPendingSkill();
                return;
            }

            if (_pendingSkillHasLocation)
            {
                if (IsInSkillRange(_pendingSkillTargetLocation, _pendingSkillRange))
                {
                    bool sent = UseAreaSkill(_pendingSkill, 0, _pendingSkillTargetLocation);
                    if (sent)
                        ClearPendingSkill();
                }
                else
                {
                    MoveHeroTowardsTarget(_pendingSkillTargetLocation, force: false);
                }
                return;
            }

            if (!walkableWorld.WalkerObjectsById.TryGetValue(_pendingSkillTargetId, out var walker))
            {
                ClearPendingSkill();
                return;
            }

            if (_pendingSkillTargetIsPlayer)
            {
                if (walker is not PlayerObject targetPlayer || targetPlayer.IsDead || !_isDuelAttackTarget(targetPlayer))
                {
                    ClearPendingSkill();
                    return;
                }

                if (IsInSkillRange(targetPlayer.Location, _pendingSkillRange))
                {
                    bool sent = _pendingSkillIsArea
                        ? UseAreaSkill(_pendingSkill, targetPlayer.NetworkId)
                        : UseSkillOnPlayerTarget(_pendingSkill, targetPlayer);
                    if (sent)
                        ClearPendingSkill();
                }
                else
                {
                    MoveHeroTowardsTarget(targetPlayer.Location, force: false);
                }
                return;
            }

            if (walker is not MonsterObject targetMonster || targetMonster.IsDead || targetMonster.World != _scene.World)
            {
                ClearPendingSkill();
                return;
            }

            if (IsInSkillRange(targetMonster.Location, _pendingSkillRange))
            {
                bool sent = _pendingSkillIsArea
                    ? UseAreaSkill(_pendingSkill, targetMonster.NetworkId)
                    : UseSkillOnTarget(_pendingSkill, targetMonster);
                if (sent)
                    ClearPendingSkill();
            }
            else
            {
                MoveHeroTowardsTarget(targetMonster.Location, force: false);
            }
        }

        private void ClearPendingSkill()
        {
            _pendingSkill = null;
            _pendingSkillTargetId = 0;
            _pendingSkillTargetLocation = Vector2.Zero;
            _pendingSkillHasLocation = false;
            _pendingSkillRange = 0;
            _pendingSkillIsArea = false;
            _pendingSkillTargetIsPlayer = false;
        }

        private void MoveHeroTowardsTarget(Vector2 targetLocation, bool force)
        {
            var hero = _scene.Hero;
            if (hero == null)
                return;

            // 手機改用虛擬搖桿操控移動。若同時保留點擊移動，
            // 每次點技能按鈕或 UI 空白處都會讓角色跑掉，兩套操作互相打架。
            // 攻擊時為了接近目標而移動（force）仍然保留。
            if (GameScene.UseVirtualJoystick && !force)
                return;

            if (!force && (hero.IsMoving || hero.MovementIntent))
                return;

            bool usePathfinding = !hero.IsAttackOrSkillAnimationPlaying();
            hero.MoveTo(targetLocation, sendToServer: true, usePathfinding: usePathfinding);
        }

        /// <summary>
        /// 手機用：自動鎖定最近的敵人並施放指定技能（技能為 null 則普通攻擊）。
        ///
        /// 桌面的流程是「先選技能、再點目標」，手機上要點兩次而且得點準怪物，
        /// 體驗很差。手遊 MMO 的標準做法是按鈕直接對最近的敵人出手，這裡照做。
        ///
        /// <b>必須依技能型別分派</b>。原本一律走 <see cref="UseSkillOnTarget"/>，
        /// 也就是一律送「指定目標」的技能封包，於是：
        /// <list type="bullet">
        ///   <item>範圍技（戰士的旋風斬 41、憤怒之錘 42…）被當成單體技送出，
        ///         伺服器只打得到一個目標，而且客戶端等不到 AreaSkillAnimation（0x1E），
        ///         技能特效永遠不會生成 —— 這正是「戰士的魔法沒有播放」。</item>
        ///   <item>自身增益（防禦 18、生命增幅 48…）需要一個怪物在附近才送得出去，
        ///         而且會把自己指定成「攻擊那隻怪物」。安全區裡完全無法施放。</item>
        /// </list>
        /// 桌面沒有這個問題，因為 <see cref="HandleRightClickSkillUsage"/> 本來就有分派。
        /// </summary>
        /// <returns>是否成功出手。false 代表這一次什麼都沒送出，
        /// 原因記在 <see cref="LastFailureReason"/>。</returns>
        public bool AttackNearestEnemy(Core.Client.SkillEntryState skill)
        {
            LastFailureReason = null;

            var hero = _scene.Hero;
            if (hero == null || hero.IsDead)
            {
                LastFailureReason = hero == null ? null : "You are dead";
                return false;
            }

            if (skill == null)
            {
                // 沒有指定技能就普通攻擊
                var meleeTarget = FindNearestTarget(hero);
                if (meleeTarget == null)
                {
                    LastFailureReason = "No target nearby";
                    return false;
                }

                // 走 PlayerObject.Attack 而不是自己組封包 —— 桌面就是走這條。
                // 它會處理射程（不夠就走過去）、出手動作、弓箭的實體投射物、
                // 武器揮擊音效，以及客戶端方向到伺服器方向的轉換。
                // 原本手機自己送 SendHitRequestAsync 並寫死 attackAnimation 0x78，
                // 所以手機的普通攻擊<b>沒有箭、沒有音效</b>，動作代號也是錯的。
                if (meleeTarget is MonsterObject meleeMonster)
                    hero.Attack(meleeMonster);
                else if (meleeTarget is PlayerObject meleePlayer)
                    hero.Attack(meleePlayer);
                else
                    return false;

                return true;
            }

            // 傳送需要玩家自己指定落點，不能用「最近的敵人」代替。
            if (skill.SkillId == TeleportSkillId)
            {
                LastFailureReason = "Tap the map to teleport";
                return false;
            }

            // 安全區內伺服器會直接丟掉技能封包（TargetedSkillDefaultPlugin 的
            // IsAtSafezone 判斷）。桌面的右鍵路徑本來就有擋，手機這條沒有 ——
            // 玩家在城裡按技能會完全沒有反應，也沒有任何訊息。
            if (_scene.World is WalkableWorldControl safeZoneWorld)
            {
                var flags = safeZoneWorld.Terrain.RequestTerrainFlag((int)hero.Location.X, (int)hero.Location.Y);
                if (flags.HasFlag(TWFlags.SafeZone))
                {
                    LastFailureReason = "Not in a safe zone";
                    return false;
                }
            }

            var skillType = SkillDatabase.GetSkillType(skill.SkillId);

            if (skillType == Client.Data.BMD.SkillType.Self)
                return UseSelfSkill(skill);

            var target = FindNearestTarget(hero);
            uint allowedRange = SkillDatabase.GetSkillRange(skill.SkillId);

            if (skillType == Client.Data.BMD.SkillType.Area)
            {
                // 沒有目標時仍然可以放 —— 範圍技本來就不必指定敵人。
                if (target == null)
                    return UseAreaSkill(skill, 0, TileInFrontOfHero(hero, allowedRange));

                if (!IsInSkillRange(target.Location, allowedRange))
                {
                    // 走過去再放，而不是靜默失敗（原本連走都不會走）。
                    QueueAreaSkillCast(skill, target.Location, allowedRange);
                    return true;
                }

                return UseAreaSkill(skill, target.NetworkId, target.Location);
            }

            if (target == null)
            {
                LastFailureReason = "No target nearby";
                return false;
            }

            if (!IsInSkillRange(target.Location, allowedRange))
            {
                QueueSkillCast(skill, target, allowedRange, isAreaSkill: false);
                return true;
            }

            return target switch
            {
                MonsterObject monster => UseSkillOnTarget(skill, monster),
                PlayerObject player => UseSkillOnPlayerTarget(skill, player),
                _ => false
            };
        }

        /// <summary>
        /// 自身／隊伍增益。OpenMU 走的是同一個「指定目標」的處理器，
        /// 目標填自己即可（<c>TargetedSkillHandlerPlugIn</c>：「target 也可以是玩家本人」）。
        /// </summary>
        private bool UseSelfSkill(Core.Client.SkillEntryState skill)
        {
            var hero = _scene.Hero;
            if (hero == null || hero.IsDead)
                return false;

            if (!TryBeginSkillCast(skill, hero))
                return false;

            _logger?.LogInformation("Using self skill {SkillId} (Level {Level})",
                skill.SkillId, skill.SkillLevel);

            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(
                skill.SkillId,
                hero.NetworkId);

            return true;
        }

        /// <summary>
        /// 角色面前的格子，供「附近沒有敵人時仍要放範圍技」使用。
        /// 距離取射程的一半，讓特效落在畫面裡而不是踩在自己腳下。
        /// </summary>
        private static Vector2 TileInFrontOfHero(PlayerObject hero, uint allowedRange)
        {
            float distance = allowedRange > 0 ? MathF.Max(1f, allowedRange * 0.5f) : 2f;

            // Angle.Z 是「畫面朝向」，與地圖格子的對應見 DirectionExtensions.ToAngle：
            // 0 度 = (0,-1)、90 度 = (+1,0)、180 度 = (0,+1)、270 度 = (-1,0)。
            // 也就是 dx = sin(angle)、dy = -cos(angle)，不是一般的 (cos, sin)。
            float angle = hero.Angle.Z;
            var ahead = hero.Location + new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * distance;

            return new Vector2(
                Math.Clamp(ahead.X, 0, Constants.TERRAIN_SIZE - 1),
                Math.Clamp(ahead.Y, 0, Constants.TERRAIN_SIZE - 1));
        }

        /// <summary>
        /// 找出最近且可以攻擊的對象。只看格子距離，夠用且成本低。
        ///
        /// <b>玩家優先於怪物。</b>客戶端唯一允許攻擊玩家的情況是決鬥中的對手
        /// （<c>GameSceneDuelController.IsDuelAttackTarget</c>），那是一個明確的
        /// 一對一狀態 —— 決鬥中按攻擊鍵想打的一定是對手，不是旁邊的怪。
        ///
        /// 原本這個方法只掃 <c>World.Monsters</c>，所以<b>手機上完全無法對玩家出手</b>，
        /// 決鬥等於沒有。桌面因為是滑鼠點目標，沒有這個問題。
        /// </summary>
        /// <summary>
        /// 自動選敵的最大距離（格）。
        ///
        /// <b>沒有這個上限會出事</b>：超出射程時會走過去再放（QueueSkillCast），
        /// 所以「最近的敵人在四十格外」時，按一下技能鈕角色就會自己橫越整張地圖。
        /// 桌面沒有這個問題 —— 目標是玩家用滑鼠點的。
        /// 10 格略小於視野範圍，畫面上看得到的敵人都在內。
        /// </summary>
        private const float AutoTargetRangeTiles = 10f;

        private WalkerObject FindNearestTarget(Objects.Player.PlayerObject hero)
        {
            var duelTarget = FindNearestDuelPlayer(hero);
            if (duelTarget != null)
                return duelTarget;

            return FindNearestMonster(hero);
        }

        private MonsterObject FindNearestMonster(Objects.Player.PlayerObject hero)
        {
            var monsters = _scene.World?.Monsters;
            if (monsters == null || monsters.Count == 0)
                return null;

            MonsterObject best = null;
            float bestDistanceSquared = AutoTargetRangeTiles * AutoTargetRangeTiles;

            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (monster == null || monster.IsDead || !monster.Visible)
                    continue;

                float distanceSquared = Vector2.DistanceSquared(hero.Location, monster.Location);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    best = monster;
                }
            }

            return best;
        }

        private PlayerObject FindNearestDuelPlayer(Objects.Player.PlayerObject hero)
        {
            var players = _scene.World?.Players;
            if (players == null || players.Count == 0)
                return null;

            PlayerObject best = null;
            float bestDistanceSquared = AutoTargetRangeTiles * AutoTargetRangeTiles;

            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || ReferenceEquals(player, hero))
                    continue;

                if (player.IsDead || !player.Visible || player.World != _scene.World)
                    continue;

                // 與桌面同一條規則，不另外發明 PvP 政策
                if (!_isDuelAttackTarget(player))
                    continue;

                float distanceSquared = Vector2.DistanceSquared(hero.Location, player.Location);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    best = player;
                }
            }

            return best;
        }

        private bool UseSkillOnTarget(Core.Client.SkillEntryState skill, MonsterObject target)
        {
            var hero = _scene.Hero;
            if (skill == null || target == null || hero == null)
                return false;

            if (hero.IsDead)
                return false;

            if (!TryBeginSkillCast(skill, hero))
                return false;

            hero.FaceTowards(target.Location, immediate: true);

            _logger?.LogInformation("Using targeted skill {SkillId} (Level {Level}) on target {TargetId}",
                skill.SkillId, skill.SkillLevel, target.NetworkId);

            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(
                skill.SkillId,
                target.NetworkId);

            return true;
        }

        private bool UseSkillOnPlayerTarget(Core.Client.SkillEntryState skill, PlayerObject target)
        {
            var hero = _scene.Hero;
            if (skill == null || target == null || hero == null)
                return false;

            if (hero.IsDead || target.IsDead)
                return false;

            if (!_isDuelAttackTarget(target))
                return false;

            if (!TryBeginSkillCast(skill, hero))
                return false;

            hero.FaceTowards(target.Location, immediate: true);

            _logger?.LogInformation("Using targeted skill {SkillId} (Level {Level}) on duel target player {TargetId}",
                skill.SkillId, skill.SkillLevel, target.NetworkId);

            _ = MuGame.Network.GetCharacterService().SendSkillRequestAsync(
                skill.SkillId,
                target.NetworkId);

            return true;
        }

        private bool UseAreaSkill(Core.Client.SkillEntryState skill, ushort extraTargetId = 0, Vector2? targetLocationOverride = null)
        {
            var hero = _scene.Hero;
            if (skill == null || hero == null)
                return false;

            if (hero.IsDead)
                return false;

            Vector2 targetTile = hero.Location;
            if (skill.SkillId != HellFireSkillId && skill.SkillId != InfernoSkillId && skill.SkillId != EvilSpiritSkillId)
            {
                if (targetLocationOverride.HasValue)
                {
                    targetTile = targetLocationOverride.Value;
                }
                else if (_scene.World is WalkableWorldControl world)
                {
                    if (extraTargetId != 0 && world.TryGetWalkerById(extraTargetId, out var target))
                        targetTile = target.Location;
                    else
                        targetTile = new Vector2(world.MouseTileX, world.MouseTileY);
                }
            }

            byte targetX = (byte)Math.Clamp((int)targetTile.X, 0, Constants.TERRAIN_SIZE - 1);
            byte targetY = (byte)Math.Clamp((int)targetTile.Y, 0, Constants.TERRAIN_SIZE - 1);
            byte requestTargetX = targetX;
            byte requestTargetY = targetY;

            if (skill.SkillId == TeleportSkillId)
            {
                if (_scene.World is WorldControl worldForTeleport &&
                    !worldForTeleport.IsWalkable(new Vector2(targetX, targetY)))
                {
                    _logger?.LogDebug("Teleport target ({X},{Y}) is not walkable.", targetX, targetY);
                    return false;
                }
            }

            if (!TryBeginSkillCast(skill, hero))
                return false;

            hero.FaceTowards(new Vector2(targetX, targetY), immediate: true);

            var characterState = MuGame.Network?.GetCharacterState();

            if (skill.SkillId == TwisterSkillId)
            {
                requestTargetX = (byte)Math.Clamp((int)hero.Location.X, 0, Constants.TERRAIN_SIZE - 1);
                requestTargetY = (byte)Math.Clamp((int)hero.Location.Y, 0, Constants.TERRAIN_SIZE - 1);
            }

            if (skill.SkillId == TeleportSkillId)
            {
                _logger?.LogInformation("Using teleport skill {SkillId} (Level {Level}) to position ({X},{Y})",
                    skill.SkillId, skill.SkillLevel, targetX, targetY);

                characterState?.BeginTeleport();

                hero.StopMovement();
                hero.Hidden = true; // Hide hero until server responds

                _ = MuGame.Network.GetCharacterService().SendEnterGateRequestAsync(0, targetX, targetY);
                return true;
            }

            byte animationCounter = NextAreaSkillAnimationCounter();
            if (characterState != null)
            {
                characterState.LastAreaSkillId = skill.SkillId;
                characterState.LastAreaSkillTargetX = requestTargetX;
                characterState.LastAreaSkillTargetY = requestTargetY;
                characterState.LastAreaSkillAnimationCounter = animationCounter;
                characterState.LastAreaSkillSentAtMs = GetNowMs();
            }

            if (extraTargetId != 0)
            {
                _logger?.LogInformation("Using skill {SkillId} (Level {Level}) at position ({X},{Y}) with target {TargetId}",
                    skill.SkillId, skill.SkillLevel, requestTargetX, requestTargetY, extraTargetId);
            }
            else
            {
                _logger?.LogInformation("Using area skill {SkillId} (Level {Level}) at position ({X},{Y})",
                    skill.SkillId, skill.SkillLevel, requestTargetX, requestTargetY);
            }

            float angleZ = MathHelper.WrapAngle(hero.Angle.Z);
            if (angleZ < 0f)
            {
                angleZ += MathHelper.TwoPi;
            }
            byte rotation = (byte)(angleZ / MathHelper.TwoPi * 256f);

            _ = MuGame.Network.GetCharacterService().SendAreaSkillRequestAsync(
                skill.SkillId,
                requestTargetX,
                requestTargetY,
                rotation,
                extraTargetId: extraTargetId,
                animationCounter: animationCounter);

            return true;
        }

        private void QueueAreaSkillCast(Core.Client.SkillEntryState skill, Vector2 targetLocation, uint allowedRange)
        {
            var hero = _scene.Hero;
            if (skill == null || hero == null)
                return;

            _pendingSkill = skill;
            _pendingSkillTargetId = 0;
            _pendingSkillTargetLocation = targetLocation;
            _pendingSkillHasLocation = true;
            _pendingSkillRange = allowedRange;
            _pendingSkillIsArea = true;
            _pendingSkillTargetIsPlayer = false;

            MoveHeroTowardsTarget(targetLocation, force: true);
        }

        private bool TryBeginSkillCast(Core.Client.SkillEntryState skill, PlayerObject hero)
        {
            if (hero.IsAttackOrSkillAnimationPlaying())
            {
                LastFailureReason = null;   // 動作還沒演完，不是錯誤，不必回報
                return false;
            }

            if (!TryConsumeSkillDelay(skill.SkillId))
            {
                LastFailureReason = "Cooling down";
                return false;
            }

            // Check player resources and stat requirements (mirrors SourceMain CSkillManager checks)
            var characterState = MuGame.Network?.GetCharacterState();
            if (characterState != null)
            {
                ushort manaCost = SkillDatabase.GetSkillManaCost(skill.SkillId);
                ushort agCost = SkillDatabase.GetSkillAGCost(skill.SkillId);

                if (characterState.CurrentMana < manaCost)
                {
                    _logger?.LogDebug("Not enough mana to use skill {SkillId}. Required: {Required}, Current: {Current}",
                        skill.SkillId, manaCost, characterState.CurrentMana);
                    LastFailureReason = $"Need {manaCost} MP";
                    return false;
                }

                // 戰士技能幾乎都要 AG，法師技能幾乎都不要 —— 這是「法師正常、戰士不正常」
                // 的另一個來源。AG 回得慢，空了就整排技能全部按不動。
                if (characterState.CurrentAbility < agCost)
                {
                    _logger?.LogDebug("Not enough AG to use skill {SkillId}. Required: {Required}, Current: {Current}",
                        skill.SkillId, agCost, characterState.CurrentAbility);
                    LastFailureReason = $"Need {agCost} AG";
                    return false;
                }

                // 這裡原本還檢查等級、力量、敏捷、能量、統率，全部依 skill_eng.bmd 的欄位。
                // 已移除，因為那組數字對不上伺服器，而且對不上的方向是「客戶端比較嚴格」——
                // 結果是玩家永遠放不出來，而且沒有任何訊息。實測（284 筆技能對照）：
                //
                //   * 能量：64 個技能對不上。CalculateRequiredEnergy 是
                //     `20 + 能量 x 等級 x 4 / 100`，但大師技的「等級」欄位存的不是角色等級 ——
                //     380 智慧擴張強化算出來要 138245 點能量，伺服器只要 118。
                //   * 等級：40 個技能伺服器根本沒有等級需求（OpenMU 把等級門檻放在「學習」而不是「施放」）。
                //     337、380 兩個的客戶端值是 29285，明顯是垃圾資料。
                //
                // 這個檢查本來也沒有保護作用：伺服器每次施放都會重新驗，
                // 而且技能是伺服器發的，學得到就代表條件已經滿足。
                // 魔力與 AG 保留 —— 284 筆裡只有新星 40 對不上，而且那兩條資源
                // 玩家在畫面上看得到，擋下來是有意義的回饋。
            }

            bool isInSafeZone = false;
            if (_scene.World is WalkableWorldControl walkableWorld)
            {
                var flags = walkableWorld.Terrain.RequestTerrainFlag((int)hero.Location.X, (int)hero.Location.Y);
                isInSafeZone = flags.HasFlag(TWFlags.SafeZone);
            }

            var action = hero.GetSkillAction(skill.SkillId, isInSafeZone);
            hero.PlayAction((ushort)action);
            hero.TriggerVehicleSkillAnimation();
            return true;
        }

        private bool TryConsumeSkillDelay(ushort skillId)
        {
            double now = GetNowMs();
            if (!Core.Client.SkillCooldownTracker.TryConsume(skillId, now))
                return false;

            // Mirror to local tracking for backward compat
            int delayMs = SkillDatabase.GetSkillCooldown(skillId);
            if (delayMs <= 0)
                return true;

            _nextSkillAllowedMs[skillId] = now + delayMs;
            return true;
        }

        private static double GetNowMs()
        {
            var gameTime = MuGame.Instance?.GameTime;
            if (gameTime != null)
                return gameTime.TotalGameTime.TotalMilliseconds;

            return Environment.TickCount64;
        }

        private byte NextAreaSkillAnimationCounter()
        {
            // Mirrors original client behavior: a small rolling serial number is used
            // to tie AreaSkillHit packets to the AreaSkill animation.
            _nextAreaSkillAnimationCounter++;
            if (_nextAreaSkillAnimationCounter > 50)
                _nextAreaSkillAnimationCounter = 1;

            return _nextAreaSkillAnimationCounter;
        }
    }
}
