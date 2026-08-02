using Client.Data;
using Client.Main.Content;
using Client.Main.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Devias
{
    public class HouseWallObject : ModelObject
    {
        // Constants
        private const float TARGET_ALPHA = 0.3f;
        private const float FADE_SPEED = 0.3f;
        private const float Y_PROXIMITY_THRESHOLD = 100f;
        private const float ScaleLocation = 100f; // Conversion factor for player position

        // State fields
        private float _alpha = 1f;
        private bool _isTransparent = false;

        public override bool IsTransparent => _isTransparent || (Alpha < 0.99f);

        // Cannot be cached due to flicker animation and player-proximity fading
        public override bool IsStaticForCaching => false;

        public override async Task Load()
        {
            BlendState = BlendState.AlphaBlend;
            LightEnabled = true;

            if (Type == 78)
            {
                // SourceMain5.2 changes only BlendMeshLight every frame;
                // it does not animate the mesh alpha.
                BlendMesh = 3;
                BlendMeshState = BlendState.Additive;
            }

            Model = await BMDLoader.Instance.Prepare($"Object3/Object{Type + 1}.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            _isTransparent = false;
            base.Update(gameTime);

            if (World is not WalkableWorldControl walkableWorld)
                return;

            if (Type == 78)
                BlendMeshLight = (MuGame.Random.Next(4) + 4) * 0.1f;

            Vector2 playerPos = walkableWorld.Walker.Location * ScaleLocation;

            if (Type == 75 || Type == 76 || Type == 77 || Type == 78)
            {
                // Check if player is behind the wall
                bool isBehind = (playerPos.X < Position.X) && (Math.Abs(playerPos.X - Position.X) < 300f);
                bool isWithinY = Math.Abs(playerPos.Y - Position.Y) <= Y_PROXIMITY_THRESHOLD;
                float targetAlpha = (isBehind && isWithinY) ? TARGET_ALPHA : 1f;

                if (isBehind && isWithinY)
                    _isTransparent = true;

                _alpha = MathHelper.Lerp(_alpha, targetAlpha, FADE_SPEED);
                Alpha = _alpha;
            }
            else if (Type == (ushort)ModelType.Fence01 || Type == (ushort)ModelType.Fence02 || Type == (ushort)ModelType.Carriage01 || Type == (ushort)ModelType.Carriage02)
            {
                // Fade fence objects when player is under the roof
                bool playerInside = walkableWorld.HeroTile == 3 || walkableWorld.HeroTile >= 10;
                float targetAlpha = playerInside ? 0f : 1f;
                Alpha = MathHelper.Lerp(Alpha, targetAlpha, FADE_SPEED);
            }

            IsTransparent = (Alpha < 0.99f) || _isTransparent;
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
        }

    }
}
