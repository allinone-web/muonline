#nullable enable
using System.Threading.Tasks;
using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Core fireball model from original MU client.
    ///
    /// 檔名在不同版本之間不一樣：Season 20 的資源包裡是 <c>Skill/Fire01.bmd</c>，
    /// 沒有 <c>Skill/Fire.bmd</c>。原本硬寫後者且沒有退路 ——
    /// <b>火球術（技能 4）的核心模型因此從來沒有載入過</b>，畫面上只剩粒子。
    /// 其他特效（隕石、冰、毒）早就有 ResolveModelPath 這類的候選鏈，這裡補上同樣的作法。
    /// </summary>
    public sealed class FireBallCoreModel : ModelObject
    {
        public FireBallCoreModel()
        {
            ContinuousAnimation = true;
            AnimationSpeed = 7f;
            BlendMesh = 1;
            BlendMeshState = BlendState.Additive;
            BlendMeshLight = 0.9f;
            LightEnabled = true;
            Light = new Vector3(1f, 0.25f, 0.08f);
            IsTransparent = true;
            DepthState = DepthStencilState.DepthRead;
        }

        private static readonly string[] ModelCandidates =
        {
            "Skill/Fire01.bmd",   // Season 20
            "Skill/Fire1.bmd",
            "Skill/Fire.bmd"      // 舊版
        };

        public override async Task Load()
        {
            foreach (var candidate in ModelCandidates)
            {
                if (!await BMDLoader.Instance.AssestExist(candidate))
                    continue;

                Model = await BMDLoader.Instance.Prepare(candidate);
                if (Model != null)
                    break;
            }

            await base.Load();
        }
    }
}
