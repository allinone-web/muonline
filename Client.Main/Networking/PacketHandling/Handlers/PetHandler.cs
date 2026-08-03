using System;
using System.Threading.Tasks;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Player;
using Client.Main.Objects.Pets;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using PetType = MUnique.OpenMU.Network.Packets.ClientToServer.PetType;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Applies the authoritative Dark Raven command and attack packets to the local scene.
    /// </summary>
    internal sealed class PetHandler : IGamePacketHandler
    {
        private readonly ILogger<PetHandler> _logger;

        public PetHandler(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<PetHandler>();
        }

        [PacketHandler(0xA7, PacketRouter.NoSubCode)]
        public Task HandlePetModeAsync(Memory<byte> packet)
        {
            if (packet.Length < PetMode.Length)
                return Task.CompletedTask;

            var modePacket = new PetMode(packet);
            if (modePacket.Pet != PetType.DarkRaven ||
                (byte)modePacket.PetCommandMode > (byte)PetCommandMode.AttackTarget)
            {
                return Task.CompletedTask;
            }

            DarkRavenCommandMode mode = (DarkRavenCommandMode)(byte)modePacket.PetCommandMode;
            ushort targetId = modePacket.TargetId;
            MuGame.ScheduleOnMainThread(() =>
            {
                if (MuGame.Instance.ActiveScene is not GameScene scene ||
                    scene.Hero?.EquippedHelper == null)
                {
                    return;
                }

                scene.Hero.EquippedHelper.SetDarkRavenCommand(mode, targetId);
                _logger.LogDebug("Dark Raven mode confirmed by server: {Mode}, target {TargetId:X4}.", mode, targetId);
            });

            return Task.CompletedTask;
        }

        [PacketHandler(0xA8, PacketRouter.NoSubCode)]
        public Task HandlePetAttackAsync(Memory<byte> packet)
        {
            if (packet.Length < PetAttack.Length)
                return Task.CompletedTask;

            var attackPacket = new PetAttack(packet);
            if (attackPacket.Pet != PetType.DarkRaven)
                return Task.CompletedTask;

            ushort ownerId = (ushort)(attackPacket.OwnerId & 0x7FFF);
            ushort targetId = attackPacket.TargetId;
            DarkRavenAttackKind attackKind = attackPacket.SkillType == PetAttack.PetSkillType.Range
                ? DarkRavenAttackKind.Range
                : DarkRavenAttackKind.SingleTarget;

            MuGame.ScheduleOnMainThread(() =>
            {
                if (MuGame.Instance.ActiveScene is not GameScene scene ||
                    scene.World is not WalkableWorldControl world)
                {
                    return;
                }

                PlayerObject owner = null;
                if (scene.Hero != null && (scene.Hero.NetworkId & 0x7FFF) == ownerId)
                    owner = scene.Hero;
                else
                    owner = world.FindPlayerById(ownerId);

                if (owner?.EquippedHelper?.Kind != FlyingHelperKind.DarkRaven)
                {
                    _logger.LogTrace("Dark Raven attack owner {OwnerId:X4} is not present or has no raven.", ownerId);
                    return;
                }

                owner.EquippedHelper.StartDarkRavenAttack(attackKind, targetId);
            });

            return Task.CompletedTask;
        }
    }
}
