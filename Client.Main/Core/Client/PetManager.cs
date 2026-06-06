#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Pets;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Manages pet lifecycle per player: creation, deletion, command, rendering.
    /// Equivalent to SourceMain GIPetManager.
    /// </summary>
    public class PetManager
    {
        private readonly ILogger<PetManager> _logger;

        /// <summary>Owner character key → pet object.</summary>
        private readonly Dictionary<ushort, PetObject> _activePets = new();

        /// <summary>Owner character key → pet info (item, level, type).</summary>
        private readonly Dictionary<ushort, PetInfo> _petInfos = new();

        public event Action<ushort>? PetChanged;

        public PetManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<PetManager>();
        }

        /// <summary>
        /// Creates a pet for the given owner.
        /// </summary>
        public PetObject? CreatePet(PlayerObject owner, int petItemType, PetType petType)
        {
            if (owner == null) return null;
            ushort ownerId = owner.NetworkId;

            // Remove existing pet first
            DeletePet(ownerId);

            var def = PetDatabase.GetDefinition(petType);
            if (def == null)
            {
                _logger?.LogWarning("Unknown pet type: {PetType}", petType);
                return null;
            }

            var pet = new PetObject();
            pet.SetOwner(owner);
            pet.SetScale(def.Scale);

            if (owner.World != null)
                owner.World.Objects.Add(pet);

            _activePets[ownerId] = pet;
            _petInfos[ownerId] = new PetInfo
            {
                PetType = petType,
                ItemType = petItemType,
            };

            _logger?.LogInformation("Pet {PetType} created for player {PlayerId}", def.Name, ownerId);

            PetChanged?.Invoke(ownerId);
            return pet;
        }

        /// <summary>
        /// Deletes the pet for the given owner.
        /// </summary>
        public void DeletePet(ushort ownerId)
        {
            if (_activePets.TryGetValue(ownerId, out var pet))
            {
                if (pet.World != null)
                    pet.World.Objects.Remove(pet);

                pet.Dispose();
                _activePets.Remove(ownerId);
                _petInfos.Remove(ownerId);
                PetChanged?.Invoke(ownerId);
                _logger?.LogDebug("Pet deleted for player {PlayerId}", ownerId);
            }
        }

        /// <summary>
        /// Gets the pet object for the given owner, or null.
        /// </summary>
        public PetObject? GetPet(ushort ownerId)
        {
            return _activePets.TryGetValue(ownerId, out var pet) ? pet : null;
        }

        /// <summary>
        /// Gets pet info for the given owner, or null.
        /// </summary>
        public PetInfo? GetPetInfo(ushort ownerId)
        {
            return _petInfos.TryGetValue(ownerId, out var info) ? info : null;
        }

        /// <summary>
        /// Sets the pet command (attack, defense, collect, wait).
        /// </summary>
        public void SetPetCommand(ushort ownerId, PetCommand command)
        {
            if (_activePets.TryGetValue(ownerId, out var pet))
            {
                pet.SetCommand(command);
                _logger?.LogDebug("Pet command {Command} set for player {PlayerId}", command, ownerId);
            }
        }

        /// <summary>
        /// Sets the pet's attack target.
        /// </summary>
        public void SetAttackTarget(ushort ownerId, int targetKey)
        {
            if (_activePets.TryGetValue(ownerId, out var pet))
            {
                pet.SetTarget(targetKey);
            }
        }

        /// <summary>
        /// Clears all pets (on map change or disconnect).
        /// </summary>
        public void ClearAll()
        {
            var ownerIds = new List<ushort>(_activePets.Keys);
            foreach (var id in ownerIds)
            {
                DeletePet(id);
            }
        }

        /// <summary>
        /// Whether the player has an active pet.
        /// </summary>
        public bool HasPet(ushort ownerId) => _activePets.ContainsKey(ownerId);
    }

    /// <summary>
    /// Pet state information matching SourceMain PET_INFO.
    /// </summary>
    public class PetInfo
    {
        public PetType PetType { get; set; }
        public int ItemType { get; set; }
        public byte Level { get; set; } = 1;
        public uint Experience { get; set; }
        public byte DamageMin { get; set; }
        public byte DamageMax { get; set; }
        public byte Defense { get; set; }
        public byte AttackSpeed { get; set; }
        public byte AttackRate { get; set; }
        public byte MagicDefense { get; set; }
        public byte Durability { get; set; }
        public byte MaxDurability { get; set; }
    }
}
