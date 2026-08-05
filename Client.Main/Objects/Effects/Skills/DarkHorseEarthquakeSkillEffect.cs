#nullable enable
using Client.Main.Core.Utilities;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for Dark Lord's Dark Horse Earthquake/Earth Shake visual sequence.
    /// SourceMain uses the same sequence for the five Earth Shake master variants.
    /// </summary>
    [SkillVisualEffect(62)]
    [SkillVisualEffect(515)]
    [SkillVisualEffect(516)]
    [SkillVisualEffect(517)]
    [SkillVisualEffect(518)]
    [SkillVisualEffect(519)]
    public sealed class DarkHorseEarthquakeSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            return new DarkHorseEarthquakeEffect(context.Caster);
        }
    }
}
