using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Original-client character entrance effect: a short magic circle and ground glow
    /// around the character.
    /// </summary>
    public sealed class CharacterSpawnEffect : EffectObject
    {
        private const string CircleTexturePath = "Effect/Magic_Circle1.jpg";
        private const string GroundTexturePath = "Effect/Magic_Ground2.jpg";
        private const float SourceFrameRate = 25f;
        private const float LifetimeFrames = 30f;
        private const float GroundGrowthFrames = 20f;
        private const float FadeOutFrames = 8f;
        private const float Lifetime = LifetimeFrames / SourceFrameRate;

        private static readonly ConditionalWeakTable<PlayerObject, CharacterSpawnEffect> ActiveEffects =
            new ConditionalWeakTable<PlayerObject, CharacterSpawnEffect>();

        private readonly PlayerObject _player;
        private readonly VertexPositionColorTexture[] _circleVertices =
            new VertexPositionColorTexture[12 * 4];
        private Texture2D _circleTexture;
        private Texture2D _groundTexture;
        private float _elapsed;

        private CharacterSpawnEffect(PlayerObject player)
        {
            _player = player;
            Position = player.Position;
            IsTransparent = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            LightEnabled = false;
        }

        public static void Start(PlayerObject player)
        {
            if (player?.World == null)
                return;

            if (ActiveEffects.TryGetValue(player, out _))
                return;

            var effect = new CharacterSpawnEffect(player);
            ActiveEffects.Add(player, effect);
            player.World.Objects.Add(effect);
        }

        public static async Task PreloadAsync()
        {
            await Task.WhenAll(
                TextureLoader.Instance.PrepareAndGetTexture(CircleTexturePath),
                TextureLoader.Instance.PrepareAndGetTexture(GroundTexturePath));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _circleTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                CircleTexturePath);
            _groundTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                GroundTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_player.Status == Models.GameControlStatus.Disposed ||
                !ReferenceEquals(_player.World, World))
            {
                Finish();
                return;
            }

            Position = _player.Position;
            _elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (_elapsed >= Lifetime)
                Finish();
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || _circleTexture == null || _groundTexture == null)
                return;

            var graphicsDevice = GraphicsManager.Instance.GraphicsDevice;
            var effect = GraphicsManager.Instance.AlphaTestEffect3D;
            BlendState originalBlend = graphicsDevice.BlendState;
            DepthStencilState originalDepth = graphicsDevice.DepthStencilState;
            RasterizerState originalRasterizer = graphicsDevice.RasterizerState;
            SamplerState originalSampler = graphicsDevice.SamplerStates[0];
            var originalWorld = effect.World;
            var originalView = effect.View;
            var originalProjection = effect.Projection;
            var originalTexture = effect.Texture;
            var originalDiffuse = effect.DiffuseColor;
            var originalAlpha = effect.Alpha;
            bool originalVertexColorEnabled = effect.VertexColorEnabled;

            try
            {
                graphicsDevice.BlendState = Blendings.OneOneAdditive;
                graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                graphicsDevice.RasterizerState = RasterizerState.CullNone;
                graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                float life = MathHelper.Clamp(
                    LifetimeFrames - _elapsed * SourceFrameRate,
                    0f,
                    LifetimeFrames);
                float fadeAlpha = life < FadeOutFrames ? life / FadeOutFrames : 1f;
                float rotationDegrees =
                    (float)(gameTime.TotalGameTime.TotalMilliseconds % 3600.0) / 10f;

                DrawCircle(
                    graphicsDevice,
                    effect,
                    Position,
                    90f,
                    130f,
                    200f,
                    rotationDegrees,
                    fadeAlpha);
                DrawCircle(
                    graphicsDevice,
                    effect,
                    Position,
                    90f,
                    130f,
                    200f,
                    -rotationDegrees,
                    fadeAlpha);

                float groundScale = MathF.Min(LifetimeFrames - life, GroundGrowthFrames) * 0.15f;
                DrawGround(
                    graphicsDevice,
                    effect,
                    Position + new Vector3(0f, 0f, 5f),
                    groundScale,
                    -_player.Angle.Z,
                    new Vector3(1f, 0.4f, 0.2f),
                    fadeAlpha);
            }
            finally
            {
                graphicsDevice.BlendState = originalBlend;
                graphicsDevice.DepthStencilState = originalDepth;
                graphicsDevice.RasterizerState = originalRasterizer;
                graphicsDevice.SamplerStates[0] = originalSampler;
                effect.World = originalWorld;
                effect.View = originalView;
                effect.Projection = originalProjection;
                effect.Texture = originalTexture;
                effect.DiffuseColor = originalDiffuse;
                effect.Alpha = originalAlpha;
                effect.VertexColorEnabled = originalVertexColorEnabled;
            }
        }

        private void DrawCircle(
            GraphicsDevice graphicsDevice,
            AlphaTestEffect effect,
            Vector3 position,
            float scaleBottom,
            float scaleTop,
            float height,
            float rotationDegrees,
            float alpha)
        {
            const int segments = 12;
            float rotation = MathHelper.ToRadians(rotationDegrees);

            for (int segment = 0; segment < segments; segment++)
            {
                float angle0 = rotation + MathHelper.ToRadians(segment * 30f);
                float angle1 = rotation + MathHelper.ToRadians((segment + 1) * 30f);
                int vertex = segment * 4;

                _circleVertices[vertex] = new VertexPositionColorTexture(
                    position + new Vector3(
                        MathF.Cos(angle0) * scaleBottom,
                        MathF.Sin(angle0) * scaleBottom,
                        0f),
                    Color.White,
                    new Vector2(segment / (float)segments, 1f));
                _circleVertices[vertex + 1] = new VertexPositionColorTexture(
                    position + new Vector3(
                        MathF.Cos(angle1) * scaleBottom,
                        MathF.Sin(angle1) * scaleBottom,
                        0f),
                    Color.White,
                    new Vector2((segment + 1) / (float)segments, 1f));
                _circleVertices[vertex + 2] = new VertexPositionColorTexture(
                    position + new Vector3(
                        MathF.Cos(angle1) * scaleTop,
                        MathF.Sin(angle1) * scaleTop,
                        height),
                    Color.Black,
                    new Vector2((segment + 1) / (float)segments, 0f));
                _circleVertices[vertex + 3] = new VertexPositionColorTexture(
                    position + new Vector3(
                        MathF.Cos(angle0) * scaleTop,
                        MathF.Sin(angle0) * scaleTop,
                        height),
                    Color.Black,
                    new Vector2(segment / (float)segments, 0f));
            }

            effect.World = Matrix.Identity;
            effect.View = Camera.Instance.View;
            effect.Projection = Camera.Instance.Projection;
            effect.Texture = _circleTexture;
            effect.VertexColorEnabled = true;
            effect.DiffuseColor = Vector3.One;
            effect.Alpha = alpha;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _circleVertices,
                    0,
                    _circleVertices.Length,
                    CircleIndices,
                    0,
                    segments * 2);
            }
        }

        private void DrawGround(
            GraphicsDevice graphicsDevice,
            AlphaTestEffect effect,
            Vector3 position,
            float scale,
            float rotation,
            Vector3 tint,
            float alpha)
        {
            float size = scale * Constants.TERRAIN_SCALE;
            effect.World = Matrix.CreateScale(size)
                           * Matrix.CreateRotationX(-MathHelper.PiOver2)
                           * Matrix.CreateRotationZ(rotation)
                           * Matrix.CreateTranslation(position);
            effect.View = Camera.Instance.View;
            effect.Projection = Camera.Instance.Projection;
            effect.Texture = _groundTexture;
            effect.VertexColorEnabled = false;
            effect.DiffuseColor = tint;
            effect.Alpha = alpha;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    GroundVertices,
                    0,
                    GroundVertices.Length,
                    GroundIndices,
                    0,
                    2);
            }
        }

        private static readonly VertexPositionTexture[] GroundVertices =
        {
            new VertexPositionTexture(new Vector3(-1f, 0f, -1f), new Vector2(0f, 0f)),
            new VertexPositionTexture(new Vector3(1f, 0f, -1f), new Vector2(1f, 0f)),
            new VertexPositionTexture(new Vector3(-1f, 0f, 1f), new Vector2(0f, 1f)),
            new VertexPositionTexture(new Vector3(1f, 0f, 1f), new Vector2(1f, 1f))
        };

        private static readonly short[] GroundIndices = { 0, 1, 2, 2, 1, 3 };

        private static readonly short[] CircleIndices = CreateCircleIndices();

        private static short[] CreateCircleIndices()
        {
            var indices = new short[12 * 6];
            for (int segment = 0; segment < 12; segment++)
            {
                int vertex = segment * 4;
                int index = segment * 6;
                indices[index] = (short)vertex;
                indices[index + 1] = (short)(vertex + 1);
                indices[index + 2] = (short)(vertex + 2);
                indices[index + 3] = (short)vertex;
                indices[index + 4] = (short)(vertex + 2);
                indices[index + 5] = (short)(vertex + 3);
            }

            return indices;
        }

        private void Finish()
        {
            ActiveEffects.Remove(_player);
            World?.RemoveObject(this);
            Dispose();
        }

        public override void Dispose()
        {
            ActiveEffects.Remove(_player);
            base.Dispose();
        }
    }
}
