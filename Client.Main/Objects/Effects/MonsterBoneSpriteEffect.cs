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
    /// SourceMain5.2 RenderLight equivalent for one or more animated bones.
    /// Up to three sprite layers can be drawn at every configured bone.
    /// </summary>
    public sealed class MonsterBoneSpriteEffect : EffectObject
    {
        private Texture2D _primaryTexture;
        private Texture2D _secondaryTexture;
        private Texture2D _tertiaryTexture;

        public ModelObject SourceModel { get; set; }
        public int[] BoneIndices { get; set; } = System.Array.Empty<int>();
        public Vector3[] BoneOffsets { get; set; } = System.Array.Empty<Vector3>();
        public string PrimaryTexturePath { get; set; }
        public string SecondaryTexturePath { get; set; }
        public string TertiaryTexturePath { get; set; }
        public float PrimaryScale { get; set; } = 1f;
        public float SecondaryScale { get; set; } = 1f;
        public float TertiaryScale { get; set; } = 1f;
        public Color LightColor { get; set; } = Color.White;
        public float PulseSpeed { get; set; }
        public float PulseBase { get; set; } = 1f;
        public float PulseAmplitude { get; set; }
        public float PulseScaleBase { get; set; } = 1f;
        public float PulseScaleAmplitude { get; set; }
        public int RequiredAction { get; set; } = -1;
        public bool HideDuringDeath { get; set; }

        private float _pulse = 1f;
        private float _scalePulse = 1f;

        public MonsterBoneSpriteEffect()
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
            if (!string.IsNullOrEmpty(PrimaryTexturePath))
                _primaryTexture = await TextureLoader.Instance.PrepareAndGetTexture(PrimaryTexturePath);
            if (!string.IsNullOrEmpty(SecondaryTexturePath))
                _secondaryTexture = await TextureLoader.Instance.PrepareAndGetTexture(SecondaryTexturePath);
            if (!string.IsNullOrEmpty(TertiaryTexturePath))
                _tertiaryTexture = await TextureLoader.Instance.PrepareAndGetTexture(TertiaryTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            ModelObject parentModel = SourceModel ?? Parent as ModelObject;
            if (parentModel != null)
                Hidden = parentModel.Hidden || parentModel.Model == null;

            _pulse = PulseSpeed == 0f
                ? 1f
                : PulseBase + MathF.Sin((float)gameTime.TotalGameTime.TotalMilliseconds * PulseSpeed) * PulseAmplitude;
            _scalePulse = PulseSpeed == 0f
                ? 1f
                : PulseScaleBase + MathF.Sin((float)gameTime.TotalGameTime.TotalMilliseconds * PulseSpeed) * PulseScaleAmplitude;

            base.Update(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            ModelObject parentModel = SourceModel ?? Parent as ModelObject;
            if (Hidden || parentModel == null ||
                RequiredAction >= 0 && parentModel.CurrentAction != RequiredAction ||
                HideDuringDeath && parentModel.CurrentAction == (int)MonsterActionType.Die ||
                (_primaryTexture == null && _secondaryTexture == null && _tertiaryTexture == null))
                return;

            Matrix[] bones = parentModel.GetBoneTransforms();
            if (bones == null)
                return;

            using (new SpriteBatchScope(
                GraphicsManager.Instance.Sprite,
                SpriteSortMode.Deferred,
                BlendState.Additive,
                SamplerState.LinearClamp,
                DepthStencilState.DepthRead,
                RasterizerState.CullNone))
            {
                for (int i = 0; i < BoneIndices.Length; i++)
                {
                    int boneIndex = BoneIndices[i];
                    if (boneIndex < 0 || boneIndex >= bones.Length)
                        continue;

                    Vector3 localPosition = BoneOffsets.Length > i
                        ? Vector3.Transform(BoneOffsets[i], bones[boneIndex])
                        : bones[boneIndex].Translation;
                    Vector3 worldPosition = Vector3.Transform(localPosition, parentModel.WorldPosition);
                    DrawSprite(parentModel, worldPosition, _primaryTexture, PrimaryScale * _scalePulse);
                    DrawSprite(parentModel, worldPosition, _secondaryTexture, SecondaryScale * _scalePulse);
                    DrawSprite(parentModel, worldPosition, _tertiaryTexture, TertiaryScale * _scalePulse);
                }
            }

            base.DrawAfter(gameTime);
        }

        private void DrawSprite(ModelObject parentModel, Vector3 worldPosition, Texture2D texture, float baseScale)
        {
            if (texture == null)
                return;

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

            float screenScale = baseScale * ownerScale /
                (System.MathF.Max(distance, 0.1f) / Constants.TERRAIN_SIZE) *
                Constants.RENDER_SCALE;

            GraphicsManager.Instance.Sprite.Draw(
                texture,
                new Vector2(projected.X, projected.Y),
                null,
                new Color(LightColor.ToVector4() * _pulse),
                0f,
                new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                screenScale,
                SpriteEffects.None,
                MathHelper.Clamp(projected.Z, 0f, 1f));
        }

        public override void Dispose()
        {
            _primaryTexture = null;
            _secondaryTexture = null;
            _tertiaryTexture = null;
            base.Dispose();
        }
    }
}
