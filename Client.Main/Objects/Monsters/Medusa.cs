using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    public class Medusa : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public Medusa()
        {
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 35, RightEyeBone = 34, GlowColor = new Color(70, 160, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster193.bmd");
            await base.Load();
        }
    }
}
