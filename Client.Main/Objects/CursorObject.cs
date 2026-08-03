using Client.Main.Objects.Effects;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects
{
    public class CursorObject : WorldObject
    {
        public float _visibleTime = 0f;
        private bool _effectHierarchyValidated;

        public override async Task Load()
        {
            Scale = 0.7f;
            EnsureSingleMoveTargetEffect();
            _effectHierarchyValidated = true;
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            if (!_effectHierarchyValidated)
            {
                EnsureSingleMoveTargetEffect();
                _effectHierarchyValidated = true;
            }

            if (_visibleTime > 0)
            {
                _visibleTime -= gameTime.ElapsedGameTime.Milliseconds;
                Alpha = _visibleTime / 1500f;
            }
            else if (!Hidden)
            {
                Hidden = true;
            }

            base.Update(gameTime);
        }

        protected override void OnPositionChanged()
        {
            base.OnPositionChanged();
            Hidden = false;
            _visibleTime = 1500f;
            Alpha = 1f;
        }

        private void EnsureSingleMoveTargetEffect()
        {
            MoveTargetPostEffectObject primaryEffect = null;
            var children = Children.GetSnapshotArray();

            for (int i = children.Length - 1; i >= 0; i--)
            {
                if (children[i] is not MoveTargetPostEffectObject effect)
                    continue;

                if (primaryEffect == null)
                {
                    primaryEffect = effect;
                    continue;
                }

                Children.Remove(effect);
                if (effect.Status != GameControlStatus.Disposed)
                    effect.Dispose();
            }

            if (primaryEffect == null)
                Children.Add(new MoveTargetPostEffectObject());
        }
    }
}
