using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(351, "Splinter Wolf")]
    public class SplinterWolf : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public SplinterWolf()
        {
            Scale = 0.8f;

            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 17, RightEyeBone = 16, GlowColor = new Color(45, 120, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster108.bmd");
            await base.Load();
        }
    }
}
