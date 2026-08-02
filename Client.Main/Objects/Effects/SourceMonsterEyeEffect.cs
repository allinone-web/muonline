using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 RenderEye equivalent: two eye01.jpg additive sprites attached
    /// to animated bones, with the original white luminosity pulse.
    /// </summary>
    public sealed class SourceMonsterEyeEffect : EffectObject
    {
        private Texture2D _eyeTexture;

        public int LeftEyeBone { get; set; } = -1;
        public int RightEyeBone { get; set; } = -1;
        public Vector3 LeftEyeOffset { get; set; } = new Vector3(5f, 0f, 0f);
        public Vector3 RightEyeOffset { get; set; } = new Vector3(-5f, 0f, 0f);

        public SourceMonsterEyeEffect()
        {
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        public override async Task Load()
        {
            await base.Load();
            _eyeTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/eye01.jpg");
        }

        public override void Update(GameTime gameTime)
        {
            if (Parent is ModelObject parentModel)
                Hidden = parentModel.Hidden || parentModel.Model == null;

            base.Update(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (Hidden || _eyeTexture == null || Parent is not ModelObject parentModel)
                return;

            Matrix[] bones = parentModel.GetBoneTransforms();
            if (bones == null || LeftEyeBone < 0 || RightEyeBone < 0 ||
                LeftEyeBone >= bones.Length || RightEyeBone >= bones.Length)
                return;

            using (new SpriteBatchScope(
                GraphicsManager.Instance.Sprite,
                SpriteSortMode.Deferred,
                BlendState.Additive,
                SamplerState.LinearClamp,
                DepthStencilState.DepthRead,
                RasterizerState.CullNone))
            {
                float luminosity = MathF.Sin((float)gameTime.TotalGameTime.TotalMilliseconds * 0.002f) * 0.3f + 0.8f;
                Color light = Color.White * luminosity;

                DrawEye(parentModel, bones[LeftEyeBone], LeftEyeOffset, light);
                DrawEye(parentModel, bones[RightEyeBone], RightEyeOffset, light);
            }

            base.DrawAfter(gameTime);
        }

        private void DrawEye(ModelObject parentModel, Matrix bone, Vector3 localOffset, Color light)
        {
            Vector3 worldPosition = Vector3.Transform(localOffset, bone * parentModel.WorldPosition);
            Vector3 projected = GraphicsDevice.Viewport.Project(
                worldPosition,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            if (projected.Z < 0f || projected.Z > 1f)
                return;

            float distance = Vector3.Distance(Camera.Instance.Position, worldPosition);
            float ownerScale = parentModel.WorldPosition.Right.Length();
            if (ownerScale <= 0.001f)
                ownerScale = 1f;

            float screenScale = ownerScale /
                (MathF.Max(distance, 0.1f) / Constants.TERRAIN_SIZE) *
                Constants.RENDER_SCALE;

            GraphicsManager.Instance.Sprite.Draw(
                _eyeTexture,
                new Vector2(projected.X, projected.Y),
                null,
                light,
                0f,
                new Vector2(_eyeTexture.Width * 0.5f, _eyeTexture.Height * 0.5f),
                screenScale,
                SpriteEffects.None,
                MathHelper.Clamp(projected.Z, 0f, 1f));
        }

        public override void Dispose()
        {
            base.Dispose();
            _eyeTexture = null;
        }
    }
}
