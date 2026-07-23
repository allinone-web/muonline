#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Friend list entry matching SourceMain friend system.
    /// </summary>
    public class FriendEntry
    {
        public string Name { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public byte ServerId { get; set; }
    }

    /// <summary>
    /// Manages friend list state.
    /// Equivalent to SourceMain NewUIFriendWindow backend.
    /// </summary>
    public class FriendManager
    {
        private readonly ILogger<FriendManager> _logger;
        private readonly List<FriendEntry> _friends = new();
        private readonly Dictionary<string, string> _friendChatLog = new();

        public event Action? FriendListChanged;
        public event Action<string, string, string>? FriendMessageReceived; // (senderName, receiverName, message)

        public FriendManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<FriendManager>();
        }

        public IReadOnlyList<FriendEntry> GetFriends() => _friends;

        public void SetFriendList(IEnumerable<FriendEntry> friends)
        {
            _friends.Clear();
            _friends.AddRange(friends);
            _logger?.LogDebug("Friend list updated: {Count} friends", _friends.Count);
            FriendListChanged?.Invoke();
        }

        public void UpdateFriendOnlineStatus(string name, bool isOnline, byte serverId)
        {
            var friend = _friends.Find(f => f.Name == name);
            if (friend != null)
            {
                friend.IsOnline = isOnline;
                friend.ServerId = serverId;
                FriendListChanged?.Invoke();
            }
        }

        public void AddFriend(string name, bool isOnline = false, byte serverId = 0)
        {
            if (_friends.Exists(f => f.Name == name)) return;
            _friends.Add(new FriendEntry { Name = name, IsOnline = isOnline, ServerId = serverId });
            FriendListChanged?.Invoke();
        }

        public void RemoveFriend(string name)
        {
            _friends.RemoveAll(f => f.Name == name);
            FriendListChanged?.Invoke();
        }

        public void AddChatMessage(string sender, string receiver, string message)
        {
            _friendChatLog[$"{sender}->{receiver}"] = message;
            FriendMessageReceived?.Invoke(sender, receiver, message);
        }

        public void Clear()
        {
            _friends.Clear();
            _friendChatLog.Clear();
        }
    }
}
