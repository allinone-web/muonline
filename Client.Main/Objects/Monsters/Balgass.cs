using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(349, "Balgass")]
    public class Balgass : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public Balgass()
        {
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 10, RightEyeBone = 9, GlowColor = new Color(50, 130, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster90.bmd");
            await base.Load();
        }
    }
}