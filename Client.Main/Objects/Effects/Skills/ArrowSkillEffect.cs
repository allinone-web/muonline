#nullable enable
using Client.Main.Core.Utilities;
using Client.Main.Objects.Player;

namespace Client.Main.Objects.Effects.Skills
{
    [SkillVisualEffect(24)]  // Triple Shot
    [SkillVisualEffect(25)]  // Basic bow skill
    [SkillVisualEffect(46)]  // Deep Impact
    [SkillVisualEffect(51)]  // Ice Arrow
    [SkillVisualEffect(52)]  // Penetration
    [SkillVisualEffect(235)] // Multi-Shot
    public sealed class ArrowSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster is not PlayerObject shooter || context.World == null)
                return null;

            return new ArrowProjectileEffect(
                shooter,
                context.World,
                context.TargetId,
                context.TargetPosition,
                ArrowProjectileSpawner.GetVolleyKind(context.SkillId));
        }
    }
}
