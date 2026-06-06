#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Guild member status.
    /// </summary>
    public enum GuildMemberStatus
    {
        Normal = 0,
        BattleMaster = 1,
        GuildMaster = 2,
        Assistant = 3,
    }

    /// <summary>
    /// Guild member info.
    /// </summary>
    public class GuildMemberInfo
    {
        public string Name { get; set; } = string.Empty;
        public GuildMemberStatus Status { get; set; } = GuildMemberStatus.Normal;
        public bool IsOnline { get; set; }
        public byte ServerId { get; set; }
    }

    /// <summary>
    /// Manages guild state for the local character.
    /// Equivalent to SourceMain GuildManager + GuildCache.
    /// </summary>
    public class GuildManager
    {
        private readonly ILogger<GuildManager> _logger;

        public string GuildName { get; private set; } = string.Empty;
        public byte[] GuildLogo { get; private set; } = Array.Empty<byte>();
        public string GuildMasterName { get; private set; } = string.Empty;
        public int GuildMemberCount { get; private set; }
        public int GuildMaxMembers { get; private set; } = 80;

        /// <summary>PlayerName → GuildInfo for cached guild assignments.</summary>
        private readonly Dictionary<string, string> _playerGuilds = new();

        /// <summary>Current guild members list.</summary>
        private readonly List<GuildMemberInfo> _members = new();

        public event Action? GuildInfoChanged;
        public event Action? GuildMemberListUpdated;

        public GuildManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GuildManager>();
        }

        /// <summary>
        /// Whether the local character is in a guild.
        /// </summary>
        public bool HasGuild => !string.IsNullOrEmpty(GuildName);

        /// <summary>
        /// Sets the guild info for the local character.
        /// </summary>
        public void SetGuildInfo(string name, byte[] logo, string masterName, int memberCount)
        {
            GuildName = name;
            GuildLogo = logo;
            GuildMasterName = masterName;
            GuildMemberCount = memberCount;
            _logger?.LogInformation("Guild set: {Name}, Master: {Master}, Members: {Count}", name, masterName, memberCount);
            GuildInfoChanged?.Invoke();
        }

        /// <summary>
        /// Clears guild info (left guild or disconnected).
        /// </summary>
        public void ClearGuild()
        {
            GuildName = string.Empty;
            GuildLogo = Array.Empty<byte>();
            GuildMasterName = string.Empty;
            GuildMemberCount = 0;
            _members.Clear();
            _playerGuilds.Clear();
            GuildInfoChanged?.Invoke();
        }

        /// <summary>
        /// Gets guild members list.
        /// </summary>
        public IReadOnlyList<GuildMemberInfo> GetMembers() => _members;

        /// <summary>
        /// Updates the guild member list from server response.
        /// </summary>
        public void UpdateMemberList(IEnumerable<GuildMemberInfo> members)
        {
            _members.Clear();
            _members.AddRange(members);
            GuildMemberListUpdated?.Invoke();
        }

        /// <summary>
        /// Caches a player's guild name.
        /// </summary>
        public void SetPlayerGuild(string playerName, string guildName)
        {
            _playerGuilds[playerName] = guildName;
        }

        /// <summary>
        /// Gets cached guild name for a player.
        /// </summary>
        public string? GetPlayerGuild(string playerName)
        {
            return _playerGuilds.TryGetValue(playerName, out var guild) ? guild : null;
        }

        /// <summary>
        /// Requests guild member list from server.
        /// </summary>
        public async System.Threading.Tasks.Task RequestGuildMemberList()
        {
            var charService = MuGame.Network?.GetCharacterService();
            if (charService != null)
                await charService.SendGuildJoinRequestAsync(0); // placeholder for guild list request
        }
    }
}
