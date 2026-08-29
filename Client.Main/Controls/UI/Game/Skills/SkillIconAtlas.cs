#nullable enable
using Client.Data.BMD;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Game.Skills
{
    internal readonly struct SkillIconFrame
    {
        public SkillIconFrame(string texturePath, Rectangle sourceRectangle)
        {
            TexturePath = texturePath;
            SourceRectangle = sourceRectangle;
        }

        public string TexturePath { get; }

        public Rectangle SourceRectangle { get; }
    }

    /// <summary>
    /// Skill icon atlas mapping based on Main 5.2 (CNewUISkillList::RenderSkillIcon).
    /// </summary>
    internal static class SkillIconAtlas
    {
        public const int IconWidth = 20;
        public const int IconHeight = 28;

        public const string Skill1TexturePath = "Interface/newui_skill.jpg";
        public const string Skill2TexturePath = "Interface/newui_skill2.jpg";
        public const string Skill3TexturePath = "Interface/newui_skill3.jpg";
        public const string CommandTexturePath = "Interface/newui_command.jpg";

        /// <summary>
        /// 大師級技能（編號 300 以上）的圖示表。
        ///
        /// 這是一張<b>獨立的 512x512 圖集，一列 25 格</b>，索引用 <c>SkillBMD.MagicIcon</c>，
        /// 與前面四張 256x256 的圖集規則完全不同：
        /// <code>
        /// RenderImage(new_Master_Icon, x, y, w, h,
        ///             (20/512) * (Skill_Icon % 25),
        ///             (28/512) * (Skill_Icon / 25), 20/512, 28/512);
        /// </code>
        /// （mumain <c>NewUIMuHelper.cpp:1806</c>；載入處 <c>NewUIMasterLevel.cpp:490</c>）
        ///
        /// 少了這條分支，<c>MagicIcon</c> 會被套進 256 的算式，算出來的列數最大到 26，
        /// 遠超出圖集高度而被邊界檢查擋掉 —— 結果是 60 個大師技全部沒有圖示，
        /// 按鈕上只剩一個技能編號。
        /// </summary>
        public const string MasterTexturePath = "Interface/new_Master_Icon.jpg";

        public static readonly string[] TexturePaths =
        [
            Skill1TexturePath,
            Skill2TexturePath,
            Skill3TexturePath,
            CommandTexturePath,
            MasterTexturePath
        ];

        private const int AtlasSize = 256;

        private const int MasterAtlasSize = 512;
        private const int MasterAtlasColumns = 25;

        /// <summary>大師技能的編號起點（原版的 <c>AT_SKILL_MASTER_BEGIN</c>）。</summary>
        private const int MasterSkillFirstId = 300;

        private const int PetCommandDefaultSkillId = 120;
        private const int PetCommandLastSkillId = 123;

        private const int PlasmaStormFenrirSkillId = 76;
        private const int AliceDrainLifeSkillId = 214;
        private const int AliceThornsSkillId = 217;
        private const int AliceSleepSkillId = 219;
        private const int AliceBlindSkillId = 220;
        private const int AliceBerserkerSkillId = 218;
        private const int AliceWeaknessSkillId = 221;
        private const int AliceEnervationSkillId = 222;
        private const int SummonExplosionSkillId = 223;
        private const int SummonRequiemSkillId = 224;
        private const int SummonPollutionSkillId = 225;
        private const int BlowOfDestructionSkillId = 232;
        private const int GaoticSkillId = 238;
        private const int RecoverSkillId = 234;
        private const int MultiShotSkillId = 235;
        private const int FlameStrikeSkillId = 236;
        private const int GiganticStormSkillId = 237;
        private const int LightningShockSkillId = 230;
        private const int LightningShockUpSkillId = 545;
        private const int SwofMagicPowerSkillId = 233;

        private const int RageFighterFirstSkillId = 260;
        private const int Skill2DefaultStartSkillId = 57;

        public static bool TryResolve(ushort skillId, SkillBMD? definition, out SkillIconFrame frame)
        {
            frame = default;
            if (skillId == 0)
            {
                return false;
            }

            int skill = skillId;
            int column;
            int row;
            string texturePath;

            if (skill >= PetCommandDefaultSkillId && skill <= PetCommandLastSkillId)
            {
                column = (skill - PetCommandDefaultSkillId) % 8;
                row = (skill - PetCommandDefaultSkillId) / 8;
                texturePath = CommandTexturePath;
            }
            else if (skill == PlasmaStormFenrirSkillId)
            {
                column = 4;
                row = 0;
                texturePath = CommandTexturePath;
            }
            else if (skill >= AliceDrainLifeSkillId && skill <= AliceThornsSkillId)
            {
                column = (skill - AliceDrainLifeSkillId) % 8;
                row = 3;
                texturePath = Skill2TexturePath;
            }
            else if (skill >= AliceSleepSkillId && skill <= AliceBlindSkillId)
            {
                column = (skill - AliceSleepSkillId + 4) % 8;
                row = 3;
                texturePath = Skill2TexturePath;
            }
            else if (skill == AliceBerserkerSkillId)
            {
                column = 10;
                row = 3;
                texturePath = Skill2TexturePath;
            }
            else if (skill >= AliceWeaknessSkillId && skill <= AliceEnervationSkillId)
            {
                column = skill - AliceWeaknessSkillId + 8;
                row = 3;
                texturePath = Skill2TexturePath;
            }
            else if (skill >= SummonExplosionSkillId && skill <= SummonRequiemSkillId)
            {
                column = (skill - SummonExplosionSkillId + 6) % 8;
                row = 3;
                texturePath = Skill2TexturePath;
            }
            else if (skill == SummonPollutionSkillId)
            {
                column = 11;
                row = 3;
                texturePath = Skill2TexturePath;
            }
            else if (skill == BlowOfDestructionSkillId)
            {
                column = 7;
                row = 2;
                texturePath = Skill2TexturePath;
            }
            else if (skill == GaoticSkillId)
            {
                column = 3;
                row = 8;
                texturePath = Skill2TexturePath;
            }
            else if (skill == RecoverSkillId)
            {
                column = 9;
                row = 2;
                texturePath = Skill2TexturePath;
            }
            else if (skill == MultiShotSkillId)
            {
                column = 0;
                row = 8;
                texturePath = Skill2TexturePath;
            }
            else if (skill == FlameStrikeSkillId)
            {
                column = 1;
                row = 8;
                texturePath = Skill2TexturePath;
            }
            else if (skill == GiganticStormSkillId)
            {
                column = 2;
                row = 8;
                texturePath = Skill2TexturePath;
            }
            else if (skill == LightningShockSkillId)
            {
                column = 2;
                row = 3;
                texturePath = Skill2TexturePath;
            }
            else if (skill >= LightningShockUpSkillId && skill <= LightningShockUpSkillId + 4)
            {
                column = 6;
                row = 8;
                texturePath = Skill2TexturePath;
            }
            else if (skill == SwofMagicPowerSkillId)
            {
                column = 8;
                row = 2;
                texturePath = Skill2TexturePath;
            }
            else if (skill >= MasterSkillFirstId && definition != null)
            {
                // 大師技用自己的 512x512 圖集，一列 25 格，索引是 MagicIcon
                int masterIcon = definition.MagicIcon;
                int mx = (masterIcon % MasterAtlasColumns) * IconWidth;
                int my = (masterIcon / MasterAtlasColumns) * IconHeight;

                if (mx < 0 || my < 0 || mx + IconWidth > MasterAtlasSize || my + IconHeight > MasterAtlasSize)
                {
                    return false;
                }

                frame = new SkillIconFrame(MasterTexturePath, new Rectangle(mx, my, IconWidth, IconHeight));
                return true;
            }
            else if (definition?.SkillUseType == (byte)SkillUseType.MasterActive)
            {
                int magicIcon = definition.MagicIcon;
                column = magicIcon % 12;
                row = (magicIcon / 12) + 4;
                texturePath = Skill2TexturePath;
            }
            else if (skill >= RageFighterFirstSkillId)
            {
                column = (skill - RageFighterFirstSkillId) % 12;
                row = (skill - RageFighterFirstSkillId) / 12;
                texturePath = Skill3TexturePath;
            }
            else if (skill >= Skill2DefaultStartSkillId)
            {
                column = (skill - Skill2DefaultStartSkillId) % 8;
                row = (skill - Skill2DefaultStartSkillId) / 8;
                texturePath = Skill2TexturePath;
            }
            else
            {
                column = (skill - 1) % 8;
                row = (skill - 1) / 8;
                texturePath = Skill1TexturePath;
            }

            int x = column * IconWidth;
            int y = row * IconHeight;

            if (x < 0 || y < 0 || x + IconWidth > AtlasSize || y + IconHeight > AtlasSize)
            {
                return false;
            }

            frame = new SkillIconFrame(texturePath, new Rectangle(x, y, IconWidth, IconHeight));
            return true;
        }
    }
}
