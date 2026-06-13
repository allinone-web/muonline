using Client.Main.Content;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(74, "Alpha Crust")]
    public class AlphaCrust : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private WeaponObject _leftHandWeapon;
        private GlowingEyesEffect _eyeGlow;

        public AlphaCrust()
        {
            Scale = 1.3f;
            BlendMesh = 1;
            BlendMeshLight = 1.0f;

            EnableCustomShader = true;
            SimpleColorMode = true;
            GlowColor = new Vector3(1.0f, 1.4f, 1.6f);
            GlowIntensity = 8.0f;

            _rightHandWeapon = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 36, ItemLevel = 9 };
            _leftHandWeapon = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 45, ItemLevel = 9 };
            Children.Add(_rightHandWeapon);
            Children.Add(_leftHandWeapon);

            // Eyes: bones 26 (L), 27 (R), size 2.0 — original RenderEye(o, 26, 27, 2.0f)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 26, RightEyeBone = 27, GlowColor = new Color(60, 180, 255), GlowScale = 3.0f };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster53.bmd"); // TODO
            var item = ItemDatabase.GetItemDefinition(0, 18); // Thunder Blade
            if (item != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            var shield = ItemDatabase.GetItemDefinition(6, 14); // Legendary Shield
            if (shield != null)
                _leftHandWeapon.Model = await BMDLoader.Instance.Prepare(shield.TexturePath);

            await base.Load();
        }
    }
}
