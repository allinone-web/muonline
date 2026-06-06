#nullable enable
using System.Collections.Generic;

namespace Client.Main.Core.Utilities
{
    /// <summary>
    /// Pet type identifiers matching SourceMain PET_TYPE defines.
    /// </summary>
    public enum PetType
    {
        None = -1,
        DarkSpirit = 0,   // PC4_ELF
        DarkHorse = 1,    // PC4_TEST
        Fenrir = 2,       // PC4_SATAN
        Rudolph = 3,      // XMAS_RUDOLPH
        Panda = 4,        // PANDA
        Unicorn = 5,      // UNICORN
        Skeleton = 6,     // SKELETON
    }

    /// <summary>
    /// Static data for each pet type — model paths, scales, actions, speeds.
    /// Equivalent to SourceMain PetInfo + PetProcess configuration.
    /// </summary>
    public class PetDefinition
    {
        public PetType Type { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ModelPath { get; init; } = string.Empty;
        public float Scale { get; init; } = 1.0f;
        public int[] Actions { get; init; } = [];
        public float[] ActionSpeeds { get; init; } = [];
        public int BlendMesh { get; init; } = -1;
    }

    public static class PetDatabase
    {
        private static readonly Dictionary<PetType, PetDefinition> _pets = new();

        static PetDatabase()
        {
            Register(new PetDefinition
            {
                Type = PetType.DarkSpirit,
                Name = "Dark Spirit",
                ModelPath = "Skill\\",
                Scale = 0.9f,
                Actions = [0, 1, 2, 3],
                ActionSpeeds = [1.0f, 1.2f, 1.5f, 1.0f],
                BlendMesh = -1,
            });

            Register(new PetDefinition
            {
                Type = PetType.Unicorn,
                Name = "Unicorn",
                ModelPath = "Skill\\",
                Scale = 1.0f,
                Actions = [0, 1, 2, 3],
                ActionSpeeds = [1.0f, 1.0f, 1.3f, 1.0f],
            });

            Register(new PetDefinition
            {
                Type = PetType.Fenrir,
                Name = "Fenrir",
                ModelPath = "Skill\\",
                Scale = 1.2f,
                Actions = [0, 1, 2, 3],
                ActionSpeeds = [1.0f, 1.4f, 1.6f, 1.0f],
            });

            Register(new PetDefinition
            {
                Type = PetType.Rudolph,
                Name = "Rudolph",
                ModelPath = "Skill\\",
                Scale = 0.8f,
                Actions = [0, 1, 2, 3],
                ActionSpeeds = [1.0f, 0.9f, 1.2f, 1.0f],
            });

            Register(new PetDefinition
            {
                Type = PetType.Skeleton,
                Name = "Skeleton",
                ModelPath = "Skill\\",
                Scale = 1.0f,
                Actions = [0, 1, 2, 3],
                ActionSpeeds = [1.0f, 1.1f, 1.3f, 1.0f],
            });

            Register(new PetDefinition
            {
                Type = PetType.Panda,
                Name = "Panda",
                ModelPath = "Skill\\",
                Scale = 0.85f,
                Actions = [0, 1, 2, 3],
                ActionSpeeds = [1.0f, 0.8f, 1.1f, 1.0f],
            });
        }

        private static void Register(PetDefinition def)
        {
            _pets[def.Type] = def;
        }

        public static PetDefinition? GetDefinition(PetType type)
        {
            return _pets.TryGetValue(type, out var def) ? def : null;
        }

        /// <summary>
        /// Maps item type to pet type. Item types defined in SourceMain w_PetProcess.h.
        /// </summary>
        public static PetType GetPetTypeFromItem(int itemType)
        {
            return itemType switch
            {
                0 => PetType.DarkSpirit,
                1 => PetType.DarkHorse,
                2 => PetType.Fenrir,
                3 => PetType.Rudolph,
                4 => PetType.Panda,
                5 => PetType.Unicorn,
                6 => PetType.Skeleton,
                _ => PetType.None,
            };
        }
    }
}
