using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    public class Nightmare2 : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;
        private GlowingEyesEffect _eyeGlow2;

        public Nightmare2()
        {
            // Primary eyes: bones 9 (R), 10 (L). Secondary: bones 39, 40
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 10, RightEyeBone = 9, GlowColor = new Color(50, 140, 255) };
            _eyeGlow2 = new GlowingEyesEffect { LeftEyeBone = 40, RightEyeBone = 39, GlowColor = new Color(40, 110, 255) };
            Children.Add(_eyeGlow);
            Children.Add(_eyeGlow2);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster122.bmd");
            await base.Load();
        }
    }
}
