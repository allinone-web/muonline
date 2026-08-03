using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 CreateJoint(BITMAP_JOINT_THUNDER) between animated bones.
    /// The source rebuilds these short lightning joints every frame; drawing the
    /// current bone pairs directly keeps the same animated network without a
    /// growing list of temporary world objects.
    /// </summary>
    public sealed class MonsterBoneLightningEffect : EffectObject
    {
        private Texture2D _texture;

        public int[] BonePairs { get; set; } = Array.Empty<int>();
        public ModelObject SourceModel { get; set; }
        public int[] SourceBoneIndices { get; set; } = Array.Empty<int>();
        public Vector3 SourceOffset { get; set; }
        public Func<Vector3> TargetProvider { get; set; }
        public Color LightColor { get; set; } = Color.White;
        public float LineScale { get; set; } = 1f;
        public int RequiredAction { get; set; } = -1;

        public MonsterBoneLightningEffect()
        {
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-5000f, -5000f, -5000f),
                new Vector3(5000f, 5000f, 5000f));
        }

        public override async Task Load()
        {
            await base.Load();
            _texture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/JointThunder01.jpg");
        }

        public override void Update(GameTime gameTime)
        {
            ModelObject parentModel = SourceModel ?? Parent as ModelObject;
            if (parentModel != null)
                Hidden = parentModel.Hidden || parentModel.Model == null;

            base.Update(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            ModelObject parentModel = SourceModel ?? Parent as ModelObject;
            if (Hidden || _texture == null || parentModel == null ||
                RequiredAction >= 0 && parentModel.CurrentAction != RequiredAction)
                return;

            Matrix[] bones = parentModel.GetBoneTransforms();
            if (bones == null || (BonePairs.Length < 2 && SourceBoneIndices.Length == 0))
                return;

            using (new SpriteBatchScope(
                GraphicsManager.Instance.Sprite,
                SpriteSortMode.Deferred,
                BlendState.Additive,
                SamplerState.LinearClamp,
                DepthStencilState.DepthRead,
                RasterizerState.CullNone))
            {
                if (TargetProvider != null && SourceBoneIndices.Length > 0)
                {
                    Vector3 targetWorld = TargetProvider();
                    for (int i = 0; i < SourceBoneIndices.Length; i++)
                    {
                        int sourceBone = SourceBoneIndices[i];
                        if (sourceBone < 0 || sourceBone >= bones.Length)
                            continue;

                        Vector3 localPosition = Vector3.Transform(SourceOffset, bones[sourceBone]);
                        Vector3 sourceWorld = Vector3.Transform(localPosition, parentModel.WorldPosition);
                        DrawLightning(parentModel, sourceWorld, targetWorld);
                    }
                }
                else
                {
                    for (int i = 0; i + 1 < BonePairs.Length; i += 2)
                    {
                        int first = BonePairs[i];
                        int second = BonePairs[i + 1];
                        if (first < 0 || second < 0 || first >= bones.Length || second >= bones.Length)
                            continue;

                        Vector3 firstWorld = Vector3.Transform(bones[first].Translation, parentModel.WorldPosition);
                        Vector3 secondWorld = Vector3.Transform(bones[second].Translation, parentModel.WorldPosition);
                        DrawLightning(parentModel, firstWorld, secondWorld);
                    }
                }
            }

            base.DrawAfter(gameTime);
        }

        private void DrawLightning(ModelObject parentModel, Vector3 firstWorld, Vector3 secondWorld)
        {
            Vector3 midpoint = (firstWorld + secondWorld) * 0.5f;
            Vector3 projected = GraphicsDevice.Viewport.Project(
                midpoint,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;

            float distance = Vector3.Distance(firstWorld, secondWorld);
            float ownerScale = parentModel.WorldPosition.Right.Length();
            if (ownerScale <= 0.001f)
                ownerScale = 1f;

            float cameraDistance = Vector3.Distance(Camera.Instance.Position, midpoint);
            float screenScale = LineScale * MathF.Max(distance / 50f, 0.5f) * ownerScale /
                (MathF.Max(cameraDistance, 0.1f) / Constants.TERRAIN_SIZE) * Constants.RENDER_SCALE;
            float angle = MathF.Atan2(secondWorld.Y - firstWorld.Y, secondWorld.X - firstWorld.X);

            GraphicsManager.Instance.Sprite.Draw(
                _texture,
                new Vector2(projected.X, projected.Y),
                null,
                LightColor,
                angle,
                new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f),
                screenScale,
                SpriteEffects.None,
                MathHelper.Clamp(projected.Z, 0f, 1f));
        }

        public override void Dispose()
        {
            _texture = null;
            base.Dispose();
        }
    }
}
