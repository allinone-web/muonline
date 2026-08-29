using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 SetMonsterDie for MODEL_STONE_GOLEM:
    /// eight MODEL_BIG_STONE1 and eight MODEL_BIG_STONE2 pieces.
    /// </summary>
    public sealed class StoneGolemDeathRockEffect : EffectObject
    {
        private const float Lifetime = 2f;
        private readonly ModelObject[] _pieces = new ModelObject[16];
        private float _life = Lifetime;

        public StoneGolemDeathRockEffect(Vector3 position, Vector3 angle)
        {
            Position = position;
            Angle = angle;
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            // Season 20 的檔名是 BigStone01/02，沒有 BigStone1/2 ——
            // 原本硬寫後者且沒有退路，石人死亡的碎石特效整個不會出現。
            var stone1Model = await PrepareFirstAsync("Skill/BigStone01.bmd", "Skill/BigStone1.bmd");
            var stone2Model = await PrepareFirstAsync("Skill/BigStone02.bmd", "Skill/BigStone2.bmd");
            if (stone1Model == null || stone2Model == null)
                return;

            for (int i = 0; i < 8; i++)
            {
                await AddPieceAsync(stone1Model, i);
                await AddPieceAsync(stone2Model, i + 8);
            }
        }

        private static async Task<Client.Data.BMD.BMD> PrepareFirstAsync(params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!await BMDLoader.Instance.AssestExist(candidates[i]))
                    continue;

                var model = await BMDLoader.Instance.Prepare(candidates[i]);
                if (model != null)
                    return model;
            }

            return null;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            _life -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_life <= 0f)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private async Task AddPieceAsync(Client.Data.BMD.BMD model, int index)
        {
            var piece = new StonePiece(model);
            _pieces[index] = piece;
            Children.Add(piece);
            await piece.Load();
        }

        private sealed class StonePiece : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;
            private Vector3 _velocity;
            private float _lifeFrames;

            public StonePiece(Client.Data.BMD.BMD model)
            {
                _model = model;
                RenderShadow = false;
                ContinuousAnimation = true;
                AnimationSpeed = 6f;
                Position = new Vector3(
                    RandomRange(-64f, 63f),
                    RandomRange(-64f, 63f),
                    RandomRange(0f, 179f));
                Scale = RandomRange(0.8f, 1.1f);
                Angle = new Vector3(0f, 0f, RandomRange(0f, MathHelper.TwoPi));

                float speed = RandomRange(6.4f, 32f);
                float yaw = RandomRange(0f, MathHelper.TwoPi);
                _velocity = new Vector3(
                    MathF.Cos(yaw) * speed,
                    MathF.Sin(yaw) * speed,
                    RandomRange(8f, 23f));
                _lifeFrames = RandomRange(32f, 47f);
            }

            public override async Task Load()
            {
                Model = _model;
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready)
                    return;

                float frameFactor = (float)gameTime.ElapsedGameTime.TotalSeconds * 25f;
                Position += _velocity * frameFactor;
                _velocity *= MathF.Pow(0.9f, frameFactor);
                _velocity.Z -= 3f * frameFactor;
                Angle = new Vector3(Angle.X - Scale * 32f * frameFactor, Angle.Y, Angle.Z + 20f * frameFactor);
                _lifeFrames -= frameFactor;

                if (_lifeFrames <= 0f && Parent != null)
                    Parent.Children.Remove(this);
            }

            private static float RandomRange(float min, float max) =>
                min + (float)MuGame.Random.NextDouble() * (max - min);
        }
    }
}
