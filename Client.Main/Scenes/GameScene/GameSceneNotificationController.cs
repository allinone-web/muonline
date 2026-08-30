using System.Collections.Generic;
using Client.Main.Controls.UI;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Scenes
{
    internal sealed class GameSceneNotificationController
    {
        private readonly Controls.UI.NotificationManager _notificationManager;
        private readonly ChatLogWindow _chatLog;
        private readonly List<(ServerMessage.MessageType Type, string Message)> _pendingNotifications = new();
        private static readonly Dictionary<ServerMessage.MessageType, Color> NotificationColors = new()
        {
            { ServerMessage.MessageType.GoldenCenter, Color.Goldenrod },
            { ServerMessage.MessageType.BlueNormal, new Color(100, 150, 255) },
            { ServerMessage.MessageType.GuildNotice, new Color(144, 238, 144) }
        };

        public GameSceneNotificationController(Controls.UI.NotificationManager notificationManager, ChatLogWindow chatLog)
        {
            _notificationManager = notificationManager;
            _chatLog = chatLog;
        }

        public void AddPending(IEnumerable<(ServerMessage.MessageType Type, string Message)> messages)
        {
            if (messages == null)
                return;

            lock (_pendingNotifications)
            {
                _pendingNotifications.AddRange(messages);
            }
        }

        public void Enqueue(ServerMessage.MessageType messageType, string message)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                lock (_pendingNotifications)
                {
                    _pendingNotifications.Add((messageType, message));
                }
            });
        }

        public void ProcessPending()
        {
            if (_notificationManager == null)
                return;

            List<(ServerMessage.MessageType Type, string Message)> currentBatch;
            lock (_pendingNotifications)
            {
                if (_pendingNotifications.Count == 0)
                    return;

                currentBatch = new List<(ServerMessage.MessageType Type, string Message)>(_pendingNotifications);
                _pendingNotifications.Clear();
            }

            foreach (var pending in currentBatch)
            {
                // 手機：<b>全部走同一個公告區</b>（左上角的 note 區）。
                //
                // 原本是分兩處：金色與公會公告飄在畫面左下，藍色的寫進 note 區。
                // 於是同一件事會出現在兩個地方、兩種字級、兩種底色，而左下那一塊
                // 又正好在搖桿與藥水鈕附近。同一種東西只該有一個位置。
                if (MobileUi.IsMobile)
                {
                    _chatLog?.AddMessage(string.Empty, pending.Message, ToChatType(pending.Type));
                    continue;
                }

                if (NotificationColors.TryGetValue(pending.Type, out Color notificationColor))
                {
                    if (pending.Type != ServerMessage.MessageType.BlueNormal)
                    {
                        _notificationManager.AddNotification(pending.Message, notificationColor);
                    }
                }
                else
                {
                    _chatLog?.AddMessage(string.Empty, pending.Message, MessageType.System);
                }

                if (pending.Type == ServerMessage.MessageType.BlueNormal)
                {
                    _chatLog?.AddMessage(string.Empty, pending.Message, MessageType.System);
                }
            }
        }

        /// <summary>公告的種類對應到 note 區的訊息種類（顏色仍然分得出來）。</summary>
        private static MessageType ToChatType(ServerMessage.MessageType type) => type switch
        {
            ServerMessage.MessageType.GuildNotice => MessageType.Guild,
            ServerMessage.MessageType.GoldenCenter => MessageType.Info,
            _ => MessageType.System,
        };
    }
}
