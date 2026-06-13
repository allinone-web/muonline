using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    public class GrandWizard : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public GrandWizard()
        {
            // Eyes: bones 79,33 or 80,34 (two MoveEye calls in original)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 33, RightEyeBone = 79, GlowColor = new Color(60, 140, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster176.bmd");
            await base.Load();
        }
    }
}
