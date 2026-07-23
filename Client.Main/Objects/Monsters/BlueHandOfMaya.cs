using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    public class BlueHandOfMaya : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public BlueHandOfMaya()
        {
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 11, RightEyeBone = 5, GlowColor = new Color(30, 90, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster119.bmd");
            await base.Load();
        }
    }
}
