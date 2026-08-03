using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    /// <summary>
    /// Shared Blood Castle saint-statue rendering. SourceMain5.2 renders the
    /// normal body and then two chrome/metal passes before the statue dies.
    /// </summary>
    public abstract class BloodCastleStatueObject : MonsterObject
    {
        private Texture2D _chromeTexture;

        protected BloodCastleStatueObject(float scale)
        {
            Scale = scale;
            RenderShadow = false;
            Children.Add(new Effects.BloodCastleDeathFragmentEffect(
                this,
                "Object12/StoneCoffin01.bmd",
                "Object12/StoneCoffin02.bmd"));
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Monster/Monster61.bmd");
            await base.Load();
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _chromeTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Chrome01.jpg");
        }

        public override void Draw(GameTime gameTime)
        {
            if (CurrentAction != (int)MonsterActionType.Die)
                DrawChromePass();

            base.Draw(gameTime);
        }

        protected override void RecalculateWorldPosition()
        {
            base.RecalculateWorldPosition();
            if (Parent != null)
                return;

            Matrix worldPosition = WorldPosition;
            worldPosition.Translation += new Vector3(0f, 120f, 0f);
            WorldPosition = worldPosition;
        }

        private void DrawChromePass()
        {
            if (_chromeTexture == null || Model?.Meshes == null)
                return;

            BlendState previousBlendState = BlendState;
            BlendState = Blendings.OneOneAdditive;
            try
            {
                for (int meshIndex = 0; meshIndex < Model.Meshes.Length; meshIndex++)
                {
                    Texture2D originalTexture = GetMeshTexture(meshIndex);
                    try
                    {
                        SetMeshTextureOverride(meshIndex, _chromeTexture);
                        DrawMesh(meshIndex);
                    }
                    finally
                    {
                        if (originalTexture != null)
                            SetMeshTextureOverride(meshIndex, originalTexture);
                        else
                            ClearMeshTextureOverride(meshIndex);
                    }
                }
            }
            finally
            {
                BlendState = previousBlendState;
            }
        }
    }
}
