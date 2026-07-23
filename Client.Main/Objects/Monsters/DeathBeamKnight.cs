using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(63, "Death Beam Knight")]
    public class DeathBeamKnight : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public DeathBeamKnight()
        {
            Scale = 1.9f;
            BlendMesh = -2; // Makes the entire monster semi-transparent like in original
            BlendMeshLight = 1.0f;

            // Eyes: bones 8 (Right), 9 (Left) — same model as BeamKnight
            _eyeGlow = new GlowingEyesEffect
            {
                LeftEyeBone = 9,
                RightEyeBone = 8,
                GlowColor = new Color(60, 140, 255),
                GlowScale = 1.0f,
                GlowAlpha = 0.95f,
                TrailWidth = 5f,
                TrailDuration = 0.7f
            };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster45.bmd");
            await base.Load();
        }
    }
}
