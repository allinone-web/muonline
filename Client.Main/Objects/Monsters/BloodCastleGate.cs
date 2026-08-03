using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(131, "Castle Gate")]
    public class BloodCastleGate : MonsterObject
    {
        public BloodCastleGate()
        {
            Scale = 0.8f;
            RenderShadow = false;
            Children.Add(new BloodCastleDeathFragmentEffect(
                this,
                "Object12/Gate01.bmd",
                "Object12/Gate02.bmd"));
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster62.bmd");
            await base.Load();
        }

        protected override void RecalculateWorldPosition()
        {
            base.RecalculateWorldPosition();
            if (Parent != null)
                return;

            Matrix worldPosition = WorldPosition;
            worldPosition.Translation += new Vector3(0f, 60f, 0f);
            WorldPosition = worldPosition;
        }
    }
}
