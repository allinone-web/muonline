using Client.Data.BMD;
using System;
using System.Collections.Generic;
using System.IO;

namespace Client.Main.Objects.Player
{
    internal static class HelmModelRules
    {
        // These are the same item types for which SourceMain5.2 assigns a separate
        // MODEL_BODY_HELM head mesh in SetCharacterScale.
        private static readonly HashSet<string> BaseHeadHelmNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "HelmMale01",
            "HelmMale03",
            "HelmElf01",
            "HelmElf02",
            "HelmElf03",
            "HelmElf04"
        };

        // Lucky helm types that SourceMain5.2 treats as a separate base-head item.
        private static readonly HashSet<string> LuckyBaseHeadHelmNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "new_Helm02",
            "new_Helm04",
            "new_Helm07",
            "new_Helm09"
        };

        // Explicit overrides for the mesh that represents the actual item shell.
        // These follow the per-model RenderMesh selections in SourceMain5.2.
        private static readonly Dictionary<string, int> ShellMeshIndexOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            // Classic helms keep the base head separately; mesh 0 is the item.
            ["HelmMale01"] = 0,
            ["HelmMale03"] = 0,
            ["HelmElf01"] = 0,
            ["HelmElf02"] = 0,
            ["HelmElf03"] = 0,
            ["HelmElf04"] = 0,

            // Brass helm contains the face/skin before the item shell.
            ["HelmMale09"] = 1,

            // Helm with face on mesh 0, shell on mesh 1
            ["HelmMale25"] = 1,

            // Newer models with face/hair meshes before the shell.
            ["HelmMale26"] = 2,
            ["HelmMale27"] = 2,
            ["HelmMale28"] = 2,
            ["HelmMale29"] = 2,

            // Mystery, Red Wing, Ancient, Black Rose, Aura and Lilium.
            // SourceMain5.2 uses { 2, 1, 0, 2, 1, 2 } for these models.
            ["HelmMale40"] = 2,
            ["HelmMale41"] = 1,
            ["HelmMale42"] = 0,
            ["HelmMale43"] = 2,
            ["HelmMale44"] = 1,
            ["HelmMale45"] = 2,

            // Faith helm and the +53 helm use explicit SourceMain mesh selections.
            ["HelmMale51"] = 1,
            ["HelmMale54"] = 2,

            // Sacred, Storm Hard, Piercing and Phoenix Soul helms.
            ["HelmMale60"] = 1,
            ["HelmMale61"] = 1,
            ["HelmMale62"] = 0,
            ["HelmMale74"] = 0,

            // Dark Lord mask variants: the face is before the shell.
            ["MaskHelmMale01"] = 1,
            ["MaskHelmMale06"] = 2,
            ["MaskHelmMale07"] = 1,
            ["MaskHelmMale09"] = 1,
            ["MaskHelmMale10"] = 1,

            // Lucky items with explicit SourceMain RenderMesh selections.
            ["new_Helm04"] = 2,
            ["new_Helm09"] = 1,
        };

        private static readonly Dictionary<string, string> DarkLordMaskModelNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["HelmMale01"] = "MaskHelmMale01",
            ["HelmMale06"] = "MaskHelmMale06",
            ["HelmMale07"] = "MaskHelmMale07",
            ["HelmMale09"] = "MaskHelmMale09",
            ["HelmMale10"] = "MaskHelmMale10"
        };

        /// <summary>
        /// Returns true if this helm should keep the base head visible (face lives outside the helm).
        /// </summary>
        public static bool RequiresBaseHead(string helmPath, BMD model)
        {
            var candidate = GetModelNameCandidate(helmPath, model);
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            return IsBaseHeadHelm(candidate);
        }

        /// <summary>
        /// Returns the mesh index where the helmet shell lives for item material application.
        /// </summary>
        public static int GetHelmetShellMeshIndex(string modelPath, BMD model)
        {
            var candidate = GetModelNameCandidate(modelPath, model);

            if (ShellMeshIndexOverrides.TryGetValue(candidate, out var index))
                return index;

            // Default shell on mesh 0.
            return 0;
        }

        private static bool IsBaseHeadHelm(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            if (BaseHeadHelmNames.Contains(candidate))
                return true;

            // MaskHelmMale01 replaces HelmMale01 for Dark Lord and keeps the
            // separate class head, just like the original item type.
            if (candidate.Equals("MaskHelmMale01", StringComparison.OrdinalIgnoreCase))
                return true;

            return LuckyBaseHeadHelmNames.Contains(candidate);
        }

        public static string GetDarkLordMaskModelPath(string modelPath)
        {
            var candidate = GetFileNameWithoutExtension(modelPath);
            if (!DarkLordMaskModelNames.TryGetValue(candidate, out var maskName))
                return null;

            return $"Player/{maskName}.bmd";
        }

        private static string GetModelNameCandidate(string helmPath, BMD model)
        {
            var candidate = GetFileNameWithoutExtension(helmPath);
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;

            if (model == null)
                return string.Empty;

            var name = model.Name ?? string.Empty;
            candidate = GetFileNameWithoutExtension(name);
            return string.IsNullOrWhiteSpace(candidate) ? name : candidate;
        }

        private static string GetFileNameWithoutExtension(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
                return string.Empty;

            // BMD names from the original client use Windows separators even when
            // the MonoGame backend runs on another platform.
            var normalized = pathOrName.Replace('\\', '/');
            var fileName = Path.GetFileNameWithoutExtension(normalized);
            return string.IsNullOrWhiteSpace(fileName) ? pathOrName : fileName;
        }
    }
}
