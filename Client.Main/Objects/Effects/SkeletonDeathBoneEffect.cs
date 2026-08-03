using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 SetPlayerDie for MODEL_PLAYER skeleton subtypes:
    /// one MODEL_BONE1 and ten MODEL_BONE2 effects at the skeleton position.
    /// </summary>
    public sealed class SkeletonDeathBoneEffect : EffectObject
    {
        private const float Lifetime = 2f;
        private readonly ModelObject[] _pieces = new ModelObject[11];
        private float _life = Lifetime;

        public SkeletonDeathBoneEffect(Vector3 position, Vector3 angle)
        {
            Position = position;
            Angle = angle;
            IsTransparent = false;
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            var bone1Model = await BMDLoader.Instance.Prepare("Skill/Bone01.bmd");
            var bone2Model = await BMDLoader.Instance.Prepare("Skill/Bone02.bmd");
            if (bone1Model == null || bone2Model == null)
                return;

            await AddPieceAsync(bone1Model, 0);
            for (int i = 1; i < _pieces.Length; i++)
                await AddPieceAsync(bone2Model, i);
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
            var piece = new SkeletonBonePiece(model);
            _pieces[index] = piece;
            Children.Add(piece);
            await piece.Load();
        }

        private sealed class SkeletonBonePiece : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;

            public SkeletonBonePiece(Client.Data.BMD.BMD model)
            {
                _model = model;
                RenderShadow = false;
                ContinuousAnimation = true;
                AnimationSpeed = 6f;
            }

            public override async Task Load()
            {
                Model = _model;
                await base.Load();
            }
        }
    }
}
