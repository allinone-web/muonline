using System.Collections.Generic;

namespace Client.Data.BMD
{
    /// <summary>
    /// Static definitions for skills based on original MU client source code.
    /// Provides skill type and animation mappings that are not stored in BMD files.
    /// </summary>
    public static class SkillDefinitions
    {
        /// <summary>
        /// Mapping of skill IDs to their types (AREA/TARGET/SELF).
        /// Based on SendRequestMagic vs SendRequestMagicContinue in original client.
        /// </summary>
        private static readonly Dictionary<int, SkillType> SkillTypes = new()
        {
            // ID 1-10: Wizard Skills
            { 1, SkillType.Target },   // Poison
            { 2, SkillType.Target },   // Meteorite
            { 3, SkillType.Target },   // Lightning
            { 4, SkillType.Target },   // Fire Ball
            { 5, SkillType.Area },     // Flame
            { 6, SkillType.Area },     // Teleport
            { 7, SkillType.Target },   // Ice
            { 8, SkillType.Area },     // Twister
            { 9, SkillType.Area },   // Evil Spirit
            { 10, SkillType.Area },    // Hellfire

            // ID 11-25: Mixed Skills
            { 11, SkillType.Target },  // Power Wave
            { 12, SkillType.Area },  // Aqua Beam
            { 13, SkillType.Area },    // Cometfall
            { 14, SkillType.Area },    // Inferno
            { 15, SkillType.Self },    // Teleport Ally
            { 16, SkillType.Target },    // Soul Barrier
            { 17, SkillType.Target },  // Energy Ball
            { 18, SkillType.Self },    // Defense
            { 19, SkillType.Target },    // Falling Slash
            { 20, SkillType.Target },  // Lunge
            { 21, SkillType.Target },  // Uppercut
            { 22, SkillType.Target },    // Cyclone
            { 23, SkillType.Target },  // Slash
            { 24, SkillType.Area },  // Triple Shot
            { 26, SkillType.Target },  // Heal

            // ID 27-52: Elf/Summoner Skills
            { 27, SkillType.Self },    // Greater Defense
            { 28, SkillType.Self },    // Greater Damage
            { 30, SkillType.Self },    // Summon Goblin
            { 31, SkillType.Self },    // Summon Stone Golem
            { 32, SkillType.Self },    // Summon Assassin
            { 33, SkillType.Self },    // Summon Elite Yeti
            { 34, SkillType.Self },    // Summon Dark Knight
            { 35, SkillType.Self },    // Summon Bali
            { 36, SkillType.Self },    // Summon Soldier
            { 38, SkillType.Area },    // Decay
            { 39, SkillType.Area },    // Ice Storm
            { 40, SkillType.Target },  // Nova
            { 41, SkillType.Area },    // Twisting Slash
            { 42, SkillType.Area },    // Rageful Blow
            { 43, SkillType.Target },  // Death Stab
            { 44, SkillType.Target },  // Crescent Moon Slash
            { 45, SkillType.Target },  // Lance
            { 46, SkillType.Target },  // Deep Impact
            { 47, SkillType.Target },  // Impale
            { 48, SkillType.Self },    // Swell Life
            { 49, SkillType.Target },  // Fire Breath
            { 51, SkillType.Target },  // Ice Arrow
            { 52, SkillType.Area },  // Penetration

            // ID 55-79: Dark Lord/Mixed Skills
            { 55, SkillType.Area },    // Fire Slash
            { 56, SkillType.Area },  // Power Slash
            { 57, SkillType.Target },    // Spiral Slash
            { 60, SkillType.Self },    // Force
            { 61, SkillType.Target },  // Fire Burst
            { 508, SkillType.Target }, // Fire Burst Strength
            { 514, SkillType.Target }, // Fire Burst Mastery
            { 62, SkillType.Area },    // Earthshake
            { 63, SkillType.Self },    // Summon
            { 64, SkillType.Self },    // Increase Critical Damage
            { 65, SkillType.Area },  // Electric Spike
            { 66, SkillType.Target },  // Force Wave
            { 67, SkillType.Area },    // Stun
            { 68, SkillType.Self },    // Cancel Stun
            { 69, SkillType.Self },    // Swell Mana
            { 70, SkillType.Self },    // Invisibility
            { 71, SkillType.Self },    // Cancel Invisibility
            { 72, SkillType.Self },    // Abolish Magic
            { 73, SkillType.Target },  // Mana Rays
            { 74, SkillType.Target },  // Fire Blast
            { 76, SkillType.Area },    // Plasma Storm
            { 77, SkillType.Self },    // Infinity Arrow
            { 78, SkillType.Area },    // Fire Scream
            { 79, SkillType.Target },    // Explosion

            // ID 200-225: Summoner Skills
            { 200, SkillType.Self },   // Summon Monster
            { 201, SkillType.Self },   // Magic Attack Immunity
            { 202, SkillType.Self },   // Physical Attack Immunity
            { 203, SkillType.Self },   // Potion of Bless
            { 204, SkillType.Self },   // Potion of Soul
            { 210, SkillType.Self },   // Spell of Protection
            { 211, SkillType.Self },   // Spell of Restriction
            { 212, SkillType.Self },   // Spell of Pursuit
            { 213, SkillType.Target }, // Shield-Burn
            { 214, SkillType.Area }, // Drain Life
            { 215, SkillType.Area },   // Chain Lightning
            { 217, SkillType.Self },   // Damage Reflection
            { 218, SkillType.Self },   // Berserker
            { 219, SkillType.Target }, // Sleep
            { 221, SkillType.Target }, // Weakness
            { 222, SkillType.Target }, // Innovation
            { 223, SkillType.Area },   // Explosion
            { 224, SkillType.Area }, // Requiem
            { 225, SkillType.Area },   // Pollution

            // ID 230-270: Dark Lord/Rage Fighter Skills
            { 230, SkillType.Area },   // Lightning Shock
            { 232, SkillType.Area },   // Strike of Destruction
            { 233, SkillType.Self },   // Expansion of Wizardry
            { 234, SkillType.Self },   // Recovery
            { 235, SkillType.Area },   // Multi-Shot
            { 236, SkillType.Area }, // Flame Strike
            { 237, SkillType.Area },   // Gigantic Storm
            { 238, SkillType.Area },   // Chaotic Diseier
            { 260, SkillType.Target }, // Killing Blow
            { 261, SkillType.Target }, // Beast Uppercut
            { 262, SkillType.Target }, // Chain Drive
            { 263, SkillType.Target }, // Dark Side
            { 264, SkillType.Area }, // Dragon Roar
            { 265, SkillType.Target },   // Dragon Slasher
            { 266, SkillType.Self },   // Ignore Defense
            { 267, SkillType.Self },   // Increase Health
            { 268, SkillType.Self },   // Increase Block
            { 269, SkillType.Target }, // Charge
            { 270, SkillType.Area }, // Phoenix Shot

            // Ice Storm master variants (Ice Up I-V)
            { 302, SkillType.Area },   // Ice Up
            { 303, SkillType.Area },   // Ice Up II
            { 304, SkillType.Area },   // Ice Up III
            { 305, SkillType.Area },   // Ice Up IV
            { 306, SkillType.Area },   // Ice Up V

            // Earthquake master variants (Earth Shake I-V)
            { 515, SkillType.Self },
            { 516, SkillType.Area },
            { 517, SkillType.Self },
            { 518, SkillType.Area },
            { 519, SkillType.Area },

            // ID 495: Earth Prison
            { 495, SkillType.Area },   // Earth Prison

            // ID 565: Blood Howling
            { 565, SkillType.Self },   // Blood Howling

            // ── 對照 OpenMU Season 6 設定補齊（2026-08-29）──
            // Area / 非 Area 決定的是<b>封包格式</b>，弄錯就是靜默失敗：
            // 送錯型別 → 伺服器回的是另一種動畫封包 → 特效註冊表不會被觸發。
            // 大師級的強化技（330+）原本整批不在表裡，一律被當成單體技送出。
            // 對照來源：config."Skill"."SkillType"（3/4/5 = 各種 AreaSkill）。
            { 50, SkillType.Target },       // Flame of Evil (Monster)
            { 239, SkillType.Target },      // Doppelganger Self Explosion
            { 326, SkillType.Target },      // Cyclone Strengthener
            { 327, SkillType.Target },      // Slash Strengthener
            { 328, SkillType.Target },      // Falling Slash Streng
            { 329, SkillType.Target },      // Lunge Strengthener
            { 330, SkillType.Area },        // Twisting Slash Streng
            { 331, SkillType.Area },        // Rageful Blow Streng
            { 332, SkillType.Area },        // Twisting Slash Mastery
            { 333, SkillType.Area },        // Rageful Blow Mastery
            { 336, SkillType.Target },      // Death Stab Strengthener
            { 337, SkillType.Area },        // Strike of Destr Str
            { 356, SkillType.Self },        // Swell Life Strengt
            { 360, SkillType.Self },        // Swell Life Proficiency
            { 378, SkillType.Area },        // Flame Strengthener
            { 379, SkillType.Target },      // Lightning Strengthener
            { 380, SkillType.Self },        // Expansion of Wiz Streng
            { 381, SkillType.Area },        // Inferno Strengthener
            { 382, SkillType.Area },        // Blast Strengthener
            { 383, SkillType.Self },        // Expansion of Wiz Mas
            { 384, SkillType.Target },      // Poison Strengthener
            { 385, SkillType.Area },        // Evil Spirit Streng
            { 387, SkillType.Area },        // Decay Strengthener
            { 388, SkillType.Area },        // Hellfire Strengthener
            { 389, SkillType.Target },      // Ice Strengthener
            { 403, SkillType.Self },        // Soul Barrier Strength
            { 404, SkillType.Self },        // Soul Barrier Proficie
            { 413, SkillType.Self },        // Heal Strengthener
            { 414, SkillType.Area },        // Triple Shot Strengthener
            { 416, SkillType.Area },        // Penetration Strengthener
            { 417, SkillType.Self },        // Defense Increase Str
            { 418, SkillType.Area },        // Triple Shot Mastery
            { 420, SkillType.Self },        // Attack Increase Str
            { 422, SkillType.Self },        // Attack Increase Mastery
            { 423, SkillType.Self },        // Defense Increase Mastery
            { 424, SkillType.Target },      // Ice Arrow Strengthener
            { 441, SkillType.Self },        // Infinity Arrow Str
            { 454, SkillType.Self },        // Sleep Strengthener
            { 455, SkillType.Area },        // Chain Lightning Str
            { 456, SkillType.Area },        // Lightning Shock Str
            { 458, SkillType.Area },        // Drain Life Strengthener
            { 469, SkillType.Self },        // Berserker Strengthener
            { 470, SkillType.Self },        // Berserker Proficiency
            { 479, SkillType.Target },      // Cyclone Strengthener
            { 480, SkillType.Target },      // Lightning Strengthener
            { 481, SkillType.Area },        // Twisting Slash Stren
            { 482, SkillType.Area },        // Power Slash Streng
            { 483, SkillType.Area },        // Flame Strengthener
            { 484, SkillType.Area },        // Blast Strengthener
            { 486, SkillType.Area },        // Inferno Strengthener
            { 487, SkillType.Area },        // Evil Spirit Strengthen
            { 489, SkillType.Target },      // Ice Strengthener
            { 490, SkillType.Area },        // Blood Attack Strengthen
            { 509, SkillType.Target },      // Force Wave Streng
            { 511, SkillType.Self },        // Critical DMG Inc PowUp
            { 512, SkillType.Area },        // Earthshake Streng
            { 551, SkillType.Target },      // Killing Blow Strengthener
            { 552, SkillType.Target },      // Beast Uppercut Strengthener
            { 554, SkillType.Target },      // Killing Blow Mastery
            { 555, SkillType.Target },      // Beast Uppercut Mastery
            { 558, SkillType.Target },      // Chain Drive Strengthener
            { 559, SkillType.Target },      // Dark Side Strengthener
            { 560, SkillType.Area },        // Dragon Roar Strengthener
            { 569, SkillType.Self },        // Def SuccessRate IncPowUp
            { 572, SkillType.Self },        // DefSuccessRate IncMastery
            { 573, SkillType.Self },        // Stamina Increase Strengthener
        };

        /// <summary>
        /// Mapping of skill IDs to their animation IDs.
        /// Based on SetAction calls in original client ZzzInterface.cpp.
        /// Returns -1 for skills using generic magic/attack animations.
        /// </summary>
        private static readonly Dictionary<int, int> SkillAnimations = BuildSkillAnimations();

        /// <summary>
        /// Mapping of skill IDs to their sound file paths.
        /// Based on original client sound effects for each skill.
        /// </summary>
        private static readonly Dictionary<int, string> SkillSounds = BuildSkillSounds();

        /// <summary>
        /// Gets the skill type for a given skill ID.
        /// Returns TARGET by default if not found.
        /// </summary>
        public static SkillType GetSkillType(int skillId)
        {
            if (SkillTypes.TryGetValue(skillId, out var type))
                return type;

            // 大師技與基礎技的型別一定相同（伺服器的 ReplacedSkill 就是這個意思），
            // 表裡漏了也還有這條退路。
            int baseSkill = ResolveBaseSkill(skillId);
            if (baseSkill != skillId && SkillTypes.TryGetValue(baseSkill, out type))
                return type;

            return SkillType.Target;
        }

        /// <summary>
        /// Gets the animation ID for a given skill ID.
        /// Returns -1 if the skill uses generic magic/attack animation.
        /// </summary>

        /// <summary>
        /// 大師級技能 → 對應的基礎技能。
        ///
        /// 原版客戶端是 <c>CSkillManager::MasterSkillToBaseSkillIndex</c>：大師技沒有自己的
        /// 動作、音效與特效，一律沿用基礎技的（見 <c>SkillCast.cpp:343</c>）。
        /// 這裡的內容取自伺服器設定 <c>MasterSkillDefinition.ReplacedSkill</c>，
        /// 並且已經把「強化 → 精通 → 基礎」的鏈路展開成一步到位。
        ///
        /// 少了這張表，所有 300 以上的大師技都查不到動作、音效與特效，
        /// 而查不到動作就會退回施法手勢 —— 對戰士就是「技能沒放出去」的樣子。
        /// </summary>
        private static readonly Dictionary<int, int> MasterSkillBase = new()
        {
            { 326, 22 },      // Cyclone Strengthener → Cyclone
            { 327, 23 },      // Slash Strengthener → Slash
            { 328, 19 },      // Falling Slash Streng → Falling Slash
            { 329, 20 },      // Lunge Strengthener → Lunge
            { 330, 41 },      // Twisting Slash Streng → Twisting Slash
            { 331, 42 },      // Rageful Blow Streng → Rageful Blow
            { 332, 41 },      // Twisting Slash Mastery → Twisting Slash
            { 333, 42 },      // Rageful Blow Mastery → Rageful Blow
            { 336, 43 },      // Death Stab Strengthener → Death Stab
            { 337, 232 },     // Strike of Destr Str → Strike of Destruction
            { 356, 48 },      // Swell Life Strengt → Swell Life
            { 360, 48 },      // Swell Life Proficiency → Swell Life
            { 378, 5 },       // Flame Strengthener → Flame
            { 379, 3 },       // Lightning Strengthener → Lightning
            { 380, 233 },     // Expansion of Wiz Streng → Expansion of Wizardry
            { 381, 14 },      // Inferno Strengthener → Inferno
            { 382, 13 },      // Blast Strengthener → Cometfall
            { 383, 233 },     // Expansion of Wiz Mas → Expansion of Wizardry
            { 384, 1 },       // Poison Strengthener → Poison
            { 385, 9 },       // Evil Spirit Streng → Evil Spirit
            { 387, 38 },      // Decay Strengthener → Decay
            { 388, 10 },      // Hellfire Strengthener → Hellfire
            { 389, 7 },       // Ice Strengthener → Ice
            { 403, 16 },      // Soul Barrier Strength → Soul Barrier
            { 404, 16 },      // Soul Barrier Proficie → Soul Barrier
            { 413, 26 },      // Heal Strengthener → Heal
            { 414, 24 },      // Triple Shot Strengthener → Triple Shot
            { 416, 52 },      // Penetration Strengthener → Penetration
            { 417, 27 },      // Defense Increase Str → Greater Defense
            { 418, 24 },      // Triple Shot Mastery → Triple Shot
            { 420, 28 },      // Attack Increase Str → Greater Damage
            { 422, 28 },      // Attack Increase Mastery → Greater Damage
            { 423, 27 },      // Defense Increase Mastery → Greater Defense
            { 424, 51 },      // Ice Arrow Strengthener → Ice Arrow
            { 441, 77 },      // Infinity Arrow Str → Infinity Arrow
            { 454, 219 },     // Sleep Strengthener → Sleep
            { 455, 215 },     // Chain Lightning Str → Chain Lightning
            { 456, 230 },     // Lightning Shock Str → Lightning Shock
            { 458, 214 },     // Drain Life Strengthener → Drain Life
            { 469, 218 },     // Berserker Strengthener → Berserker
            { 470, 218 },     // Berserker Proficiency → Berserker
            { 479, 22 },      // Cyclone Strengthener → Cyclone
            { 480, 3 },       // Lightning Strengthener → Lightning
            { 481, 41 },      // Twisting Slash Stren → Twisting Slash
            { 482, 56 },      // Power Slash Streng → Power Slash
            { 483, 5 },       // Flame Strengthener → Flame
            { 484, 13 },      // Blast Strengthener → Cometfall
            { 486, 14 },      // Inferno Strengthener → Inferno
            { 487, 9 },       // Evil Spirit Strengthen → Evil Spirit
            { 489, 7 },       // Ice Strengthener → Ice
            { 490, 55 },      // Blood Attack Strengthen → Fire Slash
            { 508, 61 },      // Fire Burst Streng → Fire Burst
            { 509, 66 },      // Force Wave Streng → Force Wave
            { 511, 64 },      // Critical DMG Inc PowUp → Increase Critical Damage
            { 512, 62 },      // Earthshake Streng → Earthshake
            { 514, 61 },      // Fire Burst Mastery → Fire Burst
            { 515, 64 },      // Crit DMG Inc PowUp (2) → Increase Critical Damage
            { 516, 62 },      // Earthshake Mastery → Earthshake
            { 517, 64 },      // Crit DMG Inc PowUp (3) → Increase Critical Damage
            { 518, 78 },      // Fire Scream Stren → Fire Scream
            { 551, 260 },     // Killing Blow Strengthener → Killing Blow
            { 552, 261 },     // Beast Uppercut Strengthener → Beast Uppercut
            { 554, 260 },     // Killing Blow Mastery → Killing Blow
            { 555, 261 },     // Beast Uppercut Mastery → Beast Uppercut
            { 558, 262 },     // Chain Drive Strengthener → Chain Drive
            { 559, 263 },     // Dark Side Strengthener → Dark Side
            { 560, 264 },     // Dragon Roar Strengthener → Dragon Roar
            { 569, 268 },     // Def SuccessRate IncPowUp → Increase Block
            { 572, 268 },     // DefSuccessRate IncMastery → Increase Block
            { 573, 267 },     // Stamina Increase Strengthener → Increase Health
        };

        /// <summary>
        /// 把大師技換算成基礎技；不是大師技就原樣回傳。
        /// 等同原版的 <c>MasterSkillToBaseSkillIndex</c>。
        /// </summary>
        public static int ResolveBaseSkill(int skillId)
            => MasterSkillBase.TryGetValue(skillId, out var baseSkill) ? baseSkill : skillId;

        /// <summary>這個技能編號是否為大師級的強化／精通技。</summary>
        public static bool IsMasterSkill(int skillId) => MasterSkillBase.ContainsKey(skillId);

        public static int GetSkillAnimation(int skillId)
        {
            if (SkillAnimations.TryGetValue(skillId, out var animId))
                return animId;

            // 大師技沿用基礎技的動作（原版 SkillCast.cpp:343 就是這樣做的）
            int baseSkill = ResolveBaseSkill(skillId);
            if (baseSkill != skillId && SkillAnimations.TryGetValue(baseSkill, out animId))
                return animId;

            return -1;
        }

        /// <summary>
        /// Gets the sound file path for a given skill ID.
        /// Returns null if the skill has no specific sound mapping.
        /// </summary>
        public static string? GetSkillSound(int skillId)
        {
            if (SkillSounds.TryGetValue(skillId, out var soundPath))
                return soundPath;

            int baseSkill = ResolveBaseSkill(skillId);
            if (baseSkill != skillId && SkillSounds.TryGetValue(baseSkill, out soundPath))
                return soundPath;

            return null;
        }

        private static Dictionary<int, int> BuildSkillAnimations()
        {
            var map = new Dictionary<int, int>();

            void Add(int skillId, int animationId)
            {
                if (!map.TryAdd(skillId, animationId))
                {
                    // Prefer the first mapping we encountered (matches Main 5.2 behaviour).
                    return;
                }
            }

            // WARRIOR/KNIGHT SKILLS
            //
            // The five combo skills are a contiguous block in the original client:
            //   SetAction(PLAYER_ATTACK_SKILL_SWORD1 + skillId - AT_SKILL_FALLING_SLASH)
            // (MuMain WSclient.cpp, AT_SKILL_FALLING_SLASH..AT_SKILL_SLASH = 19..23).
            // Any skill missing from this table falls through to GetDefaultSkillAction,
            // which plays PlayerSkillHand1/2 — a two-handed *spellcasting* gesture.
            // On a knight that reads as "the skill did not fire", which is why the four
            // unmapped combo skills looked broken while every wizard skill looked fine.
            Add(19, 60);    // Falling Slash → PlayerAttackSkillSword1
            Add(20, 61);    // Lunge         → PlayerAttackSkillSword2
            Add(21, 62);    // Uppercut      → PlayerAttackSkillSword3
            Add(22, 63);    // Cyclone       → PlayerAttackSkillSword4
            Add(23, 64);    // Slash         → PlayerAttackSkillSword5
            Add(43, 71);    // Death Stab → PlayerAttackDeathstab
            Add(41, 65);    // Twisting Slash → PlayerAttackSkillWheel
            Add(42, 66);    // Rageful Blow → PlayerAttackSkillFuryStrike
            Add(44, 137);   // Crescent Moon Slash → PlayerAttackRush (AT_SKILL_RUSH)
            Add(47, 70);    // Impale → PlayerAttackSkillSpear
            Add(55, 65);    // Fire Slash → PlayerAttackSkillWheel

            // RAGE FIGHTER SKILLS
            // 來源：mumain 的 CMonkSystem::SetRageSkillAni（MonkSystem.cpp:389）。
            // 這幾個原本整批不在表裡 —— 拳王的每一個攻擊技都在演法師的施法手勢。
            Add(260, 247);  // Killing Blow    → PlayerSkillThrust
            Add(261, 248);  // Beast Uppercut  → PlayerSkillStamp
            Add(262, 249);  // Chain Drive     → PlayerSkillGiantswing
            Add(263, 250);  // Dark Side       → PlayerSkillDarksideReady
            Add(264, 253);  // Dragon Roar     → PlayerSkillDragonlore
            Add(265, 252);  // Dragon Slasher  → PlayerSkillDragonkick（原版的 AT_SKILL_DRAGON_KICK）
            Add(269, 137);  // Charge          → PlayerAttackRush（原版的 AT_SKILL_OCCUPY，WSclient.cpp:5041）
            Add(266, 255);  // Ignore Defense  → PlayerSkillAttUpOurforces
            Add(267, 256);  // Increase Health → PlayerSkillHpUpOurforces
            Add(268, 256);  // Increase Block  → PlayerSkillHpUpOurforces
            // 原版這三個增益是在兩個動作之間隨機挑（SkillCast.cpp:920），
            // 這裡固定下來 —— 一張靜態表存不了隨機，而且固定的動作比較好認。
            // 鳳凰擊 270 在原版裡沒有指定玩家動作（SetRageSkillAni 對它回傳 false），
            // 只生成特效，因此這裡也不猜。

            // WIZARD/SUMMONER SKILLS
            Add(6, 151);    // Teleport → PlayerSkillTeleport
            Add(10, 154);   // HellFire → PlayerSkillHell
            Add(13, 154);   // Cometfall → PlayerSkillHell
            Add(14, 153);   // Inferno → PlayerSkillInferno
            Add(40, 73);    // Nova release → PlayerSkillHellStart
            Add(58, 72);    // Nova start/charge → PlayerSkillHellBegin
            Add(63, 172);   // Summon → PlayerSkillSummon
            Add(214, 168);  // Drain Life → PlayerSkillDrainLife
            Add(215, 160);  // Chain Lightning → PlayerSkillChainLightning
            Add(219, 156);  // Sleep → PlayerSkillSleep
            Add(230, 185);  // Lightning Shock → PlayerSkillLightningShock
            Add(221, 156);  // Weakness → PlayerSkillSleep
            Add(222, 156);  // Innovation → PlayerSkillSleep
            Add(218, 166);  // Berserker → PlayerSkillSleep

            // DARK LORD SKILLS
            Add(78, 184);   // Fire Scream → PlayerSkillFlamestrike
            Add(232, 176);  // Strike of Destruction → PlayerSkillBlowOfDestruction
            Add(64, 71);    // Increase Critical Damage → PlayerSkillVitality
            Add(65, 71);    // Electric Spike → PlayerSkillVitality
            Add(56, 146);   // Power Slash → PlayerAttackTwoHandSwordTwo
            Add(57, 65);    // Spiral Slash → PlayerAttackSkillWheel
            Add(62, 87);    // Earthquake → PlayerAttackDarkhorse
            Add(515, 87);   // Earth Shake I → PlayerAttackDarkhorse
            Add(516, 87);   // Earth Shake II → PlayerAttackDarkhorse
            Add(517, 87);   // Earth Shake III → PlayerAttackDarkhorse
            Add(518, 87);   // Earth Shake IV → PlayerAttackDarkhorse
            Add(519, 87);   // Earth Shake V → PlayerAttackDarkhorse

            // ELF SKILLS
            Add(234, 246);  // Recovery → PlayerRecoverSkill
            Add(46, 178);   // Deep Impact → PlayerSkillMultishotBowStand
            Add(235, 178);  // Multi-Shot → PlayerSkillMultishotBowStand

            // RAGE FIGHTER SKILLS
            Add(260, 247);  // Killing Blow → PlayerSkillThrust
            Add(261, 248);  // Beast Uppercut → PlayerSkillStamp
            Add(262, 249);  // Chain Drive → PlayerSkillGiantswing
            Add(263, 250);  // Dark Side → PlayerSkillDarksideReady
            Add(264, 252);  // Dragon Roar → PlayerSkillDragonkick
            Add(265, 253);  // Dragon Slasher → PlayerSkillDragonlore
            Add(270, 254);  // Phoenix Shot → PlayerSkillPhoenixShot
            Add(48, 255);   // Swell Life → PlayerSkillAttUpOurforces

            // SPECIAL SKILLS
            Add(67, 140);   // Stun → PlayerAttackStun
            Add(76, 102);   // Plasma Storm → PlayerFenrirSkill
            Add(565, 257);  // Blood Howling → PlayerSkillBloodHowling

            return map;
        }

        /// <summary>
        /// Checks if a skill is an area/directional skill.
        /// </summary>
        public static bool IsAreaSkill(int skillId)
        {
            return GetSkillType(skillId) == SkillType.Area;
        }

        /// <summary>
        /// Checks if a skill is a target-required skill.
        /// </summary>
        public static bool IsTargetSkill(int skillId)
        {
            return GetSkillType(skillId) == SkillType.Target;
        }

        /// <summary>
        /// Checks if a skill is a self-cast skill.
        /// </summary>
        public static bool IsSelfSkill(int skillId)
        {
            return GetSkillType(skillId) == SkillType.Self;
        }

        private static Dictionary<int, string> BuildSkillSounds()
        {
            var map = new Dictionary<int, string>();

            // WARRIOR/KNIGHT SKILLS
            map[18] = "Sound/sKnightDefense.wav";        // Defense (ID 18)
            map[19] = "Sound/sKnightSkill1.wav";         // Falling Slash (ID 19)
            map[20] = "Sound/sKnightSkill2.wav";         // Lunge (ID 20)
            map[21] = "Sound/sKnightSkill3.wav";         // Uppercut (ID 21)
            map[22] = "Sound/sKnightSkill4.wav";         // Cyclone (ID 22)
            map[23] = "Sound/sKnightSkill4.wav";         // Slash (ID 23)

            // DARK LORD —— Force / Force Wave 與 Fire Burst 同一組（WSclient.cpp:4259）
            map[60] = "Sound/sKnightSkill1.wav";         // Force (ID 60)
            map[66] = "Sound/sKnightSkill1.wav";         // Force Wave (ID 66)
            map[41] = "Sound/sKnightSkill4.wav";         // Twisting Slash (ID 41)

            // ELF SKILLS
            map[26] = "Sound/sKnightDefense.wav";        // Healing (ID 26)
            map[24] = "Sound/eRaidShoot.wav";            // Triple Shot (ID 24)

            // WIZARD SKILLS
            map[4] = "Sound/sMagic.wav";                 // Fireball (ID 4)
            map[11] = "Sound/sMagic.wav";                // Power Wave (ID 11)
            map[3] = "Sound/eThunder.wav";               // Lightning (ID 3)
            map[6] = "Sound/eTelekinesis.wav";           // Teleport (ID 6)
            map[2] = "Sound/eMeteorite.wav";             // Meteorite (ID 2)
            map[7] = "Sound/sIce.wav";                   // Ice (ID 7)
            map[1] = "Sound/sEvil.wav";                  // Poison (ID 1)
            map[5] = "Sound/sFlame.wav";                 // Flame (ID 5)
            map[8] = "Sound/sTornado.wav";               // Twister (ID 8)
            map[9] = "Sound/sEvil.wav";                  // Evil Spirits (ID 9)
            map[10] = "Sound/sHellFire.wav";             // Hellfire (ID 10)
            map[12] = "Sound/sAquaFlash.wav";            // Aqua Beam (ID 12)
            map[13] = "Sound/eExplosion.wav";            // Cometfall (ID 13)
            map[14] = "Sound/sFlame.wav";                // Inferno (ID 14)
            map[15] = "Sound/eTelekinesis.wav";          // Teleport Ally (ID 15)
            map[16] = "Sound/eSoulBarrier.wav";          // Soul Barrier (ID 16)
            map[17] = "Sound/sMagic.wav";                // Energy Ball (ID 17)
            map[58] = "Sound/eHellFire2_1.wav";          // Nova charge start (ID 58)

            // MIXED SKILLS
            map[52] = "Sound/ePiercing.wav";             // Penetration (ID 52)
            map[51] = "Sound/eIceArrow.wav";             // Ice Arrow (ID 51)
            map[49] = "Sound/sKnightSkill1.wav";         // Fire Breath (ID 49)
            map[47] = "Sound/eRidingSpear.wav";          // Impale (ID 47)
            map[48] = "Sound/eSwellLife.wav";            // Greater Fortitude (ID 48)
            map[56] = "Sound/eRaidShoot.wav";            // Raid (ID 56)

            // DARK LORD SKILLS
            map[62] = "Sound/sDarkEarthQuake.wav";       // Earthshake (ID 62)
            map[515] = map[62];                            // Earth Shake I
            map[516] = map[62];                            // Earth Shake II
            map[517] = map[62];                            // Earth Shake III
            map[518] = map[62];                            // Earth Shake IV
            map[519] = map[62];                            // Earth Shake V
            map[61] = "Sound/eFirebust.wav";             // Fire Burst (ID 61)
            map[508] = "Sound/eFirebust.wav";            // Fire Burst Strength
            map[514] = "Sound/eFirebust.wav";            // Fire Burst Mastery
            map[78] = "Sound/Darklord_firescream.wav";   // Fire Scream (ID 78)

            // SUMMONER SKILLS
            map[214] = "Sound/SE_Ch_summoner_skill07_lifedrain.wav";      // Drain Life (ID 214)
            map[219] = "Sound/SE_Ch_summoner_skill03_sleep.wav";          // Sleep (ID 219)
            map[215] = "Sound/SE_Ch_summoner_skill08_chainlightning.wav"; // Chain Lightning (ID 215)
            map[65] = "Sound/sDarkElecSpike.wav";        // Electric Surge (ID 65)
            map[221] = "Sound/SE_Ch_summoner_weakness.wav";               // Weakness (ID 221)
            map[222] = "Sound/SE_Ch_summoner_innovation.wav";             // Innovation (ID 222)

            // MASTER SKILLS
            map[218] = "Sound/Berserker.wav";            // Berserker (ID 218)
            map[230] = "Sound/lightning_shock.wav";      // Lightning Shock (ID 230)
            map[237] = "Sound/gigantic_storm.wav";       // Gigantic Storm (ID 237)
            map[236] = "Sound/flame_strike.wav";         // Flame Strike (ID 236)
            map[238] = "Sound/caotic.wav";               // Chaotic Diseier (ID 238)
            map[233] = "Sound/SwellofMagicPower.wav";    // Swell of Magicpower (ID 233)
            map[232] = "Sound/BLOW_OF_DESTRUCTION.wav";  // Destruction (ID 232)
            map[235] = "Sound/multi_shot.wav";           // Multi-Shot (ID 235)
            map[234] = "Sound/recover.wav";              // Recovery (ID 234)

            // RAGE FIGHTER SKILLS
            map[263] = "Sound/Ragefighter/Rage_Darkside.wav";    // Darkside (ID 263)
            map[265] = "Sound/Ragefighter/Rage_Dragonlower.wav"; // Dragon Lore (ID 265)
            map[264] = "Sound/Ragefighter/Rage_Dragonkick.wav";  // Dragon Slayer (ID 264)
            map[270] = "Sound/Ragefighter/Rage_Giantswing.wav";  // Phoenix Shot (ID 270)
            map[269] = "Sound/Ragefighter/Rage_Stamp.wav";       // Charge (ID 269)

            return map;
        }
    }
}
