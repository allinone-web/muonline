using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    public class RedHandOfMaya : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public RedHandOfMaya()
        {
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 20, RightEyeBone = 5, GlowColor = new Color(200, 60, 50) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster120.bmd");
            await base.Load();
        }
    }
}
