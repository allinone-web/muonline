using Client.Data;
using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public class HouseObject : ModelObject
    {
        // Opaque house meshes may be instanced; animated additive meshes remain per object.
        protected override bool AllowMapObjectInstancing => true;
        private const double LegacyStepSeconds = 1.0 / 25.0;
        private double _legacyAccumulator;

        public HouseObject()
        {
            LightEnabled = true;
        }

        public override async Task Load()
        {
            var idx = (Type - (ushort)ModelType.House01 + 1).ToString().PadLeft(2, '0');

            if (idx == "03")
            {
                BlendMesh = 4;
                BlendMeshState = BlendState.Additive;
            }
            else if (idx == "04")
            {
                BlendMesh = 8;
                BlendMeshState = BlendState.Additive;
            }
            else if (idx == "05")
            {
                BlendMesh = 2;
                BlendMeshState = BlendState.Additive;
            }

            if (idx == "04" || idx == "05")
                TextureCoordinateOffsetMeshIndex = BlendMesh;

            Model = await BMDLoader.Instance.Prepare($"Object1/House{idx}.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            if (Type == (ushort)ModelType.House01 + 3 ||
                Type == (ushort)ModelType.House01 + 4)
            {
                TextureCoordinateOffset = new Vector2(
                    0f,
                    -(float)((long)gameTime.TotalGameTime.TotalMilliseconds % 1000L) * 0.001f);
            }

            base.Update(gameTime);

            _legacyAccumulator += gameTime.ElapsedGameTime.TotalSeconds;
            while (_legacyAccumulator >= LegacyStepSeconds)
            {
                if (Type == (ushort)ModelType.House01 + 2)
                    BlendMeshLight = (MuGame.Random.Next(4) + 4) * 0.1f;

                _legacyAccumulator -= LegacyStepSeconds;
            }
        }

    }
}
