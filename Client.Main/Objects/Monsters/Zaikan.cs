using Client.Main.Content;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(59, "Zaikan")]
    public class Zaikan : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private GlowingEyesEffect _eyeGlow;

        public Zaikan()
        {
            Scale = 2.1f;
            BlendMesh = -2;
            BlendMeshLight = 1.0f;
            Type = 1;

            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 43
            };
            Children.Add(_rightHandWeapon);

            // Same model as Tantalos — eyes: 24 (R), 25 (L)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 25, RightEyeBone = 24, GlowColor = new Color(80, 170, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster43.bmd");
            var weapon = ItemDatabase.GetItemDefinition(5, 8); // Staff of Destruction
            if (weapon != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(weapon.TexturePath);
            await base.Load();
            //TODO Zaikan uses tantalos model with some different blending options
        }
    }
}
