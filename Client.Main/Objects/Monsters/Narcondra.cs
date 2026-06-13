using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(576, "Narcondra")]
    public class NarCondra : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public NarCondra()
        {
            // Both eyes use bone 9 in original (single central eye / cyclops type)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 9, RightEyeBone = 9, GlowColor = new Color(60, 150, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster217.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            BlendMesh = 4;
        }
    }
}
