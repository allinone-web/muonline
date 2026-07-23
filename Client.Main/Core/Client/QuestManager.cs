#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Quest state matching SourceMain CSQuest quest states.
    /// </summary>
    public enum QuestState
    {
        Undefined = 0,
        InProgress = 1,
        Complete = 2,
        Failed = 3,
    }

    /// <summary>
    /// Central quest state manager.
    /// Tracks quest progression and provides quest information.
    /// Equivalent to SourceMain CSQuest + QuestMng.
    /// </summary>
    public class QuestManager
    {
        private readonly ILogger<QuestManager> _logger;

        /// <summary>QuestIndex → current state.</summary>
        private readonly Dictionary<byte, QuestState> _questStates = new();

        /// <summary>Kill tracking: monsterType → count killed.</summary>
        private readonly Dictionary<int, int> _killCounts = new();

        /// <summary>Current active quest index being viewed.</summary>
        private byte _currentQuestIndex;

        public event Action? QuestStateChanged;

        public QuestManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<QuestManager>();
        }

        /// <summary>
        /// Sets the quest list from server (bitmask).
        /// Equivalent: SourceMain CSQuest::setQuestLists.
        /// </summary>
        public void SetQuestLists(byte[] list, int count)
        {
            _questStates.Clear();
            _killCounts.Clear();

            for (int i = 0; i < count; i++)
            {
                _questStates[(byte)i] = QuestState.Undefined;
            }

            _logger?.LogDebug("Quest list set: {Count} quests", count);
        }

        /// <summary>
        /// Updates a single quest state.
        /// </summary>
        public void SetQuestState(byte questIndex, QuestState state)
        {
            _questStates[questIndex] = state;
            _logger?.LogDebug("Quest {Index} state = {State}", questIndex, state);
            QuestStateChanged?.Invoke();
        }

        /// <summary>
        /// Gets current state for a quest.
        /// </summary>
        public QuestState GetQuestState(byte questIndex)
        {
            return _questStates.TryGetValue(questIndex, out var state) ? state : QuestState.Undefined;
        }

        /// <summary>
        /// Sets the currently viewed quest.
        /// </summary>
        public void SetCurrentQuest(byte index)
        {
            _currentQuestIndex = index;
        }

        public byte GetCurrentQuestIndex() => _currentQuestIndex;

        /// <summary>
        /// Updates kill count for a monster type.
        /// Equivalent: SourceMain CSQuest::SetKillMobInfo.
        /// </summary>
        public void SetKillCount(int monsterType, int count)
        {
            _killCounts[monsterType] = count;
            _logger?.LogDebug("Kill count: Monster={Type}, Count={Count}", monsterType, count);
        }

        /// <summary>
        /// Gets kill count for a monster type.
        /// </summary>
        public int GetKillCount(int monsterType)
        {
            return _killCounts.TryGetValue(monsterType, out var count) ? count : 0;
        }

        /// <summary>
        /// Gets quest NPC name by quest index.
        /// Equivalent: SourceMain CSQuest::GetNPCName.
        /// </summary>
        public string GetNpcName(byte questIndex)
        {
            return questIndex switch
            {
                // Class change quests
                0 => "Sebina the Priestess",
                1 => "Marlon the Wizard",
                2 => "Devin the Warrior",
                3 => "Devin the Dark Knight",
                // Default
                _ => $"Quest NPC #{questIndex}"
            };
        }

        /// <summary>
        /// Gets quest title by index.
        /// </summary>
        public string GetQuestTitle(byte questIndex)
        {
            return questIndex switch
            {
                0 => "First Class Change",
                1 => "Second Class Change",
                2 => "Third Class Change (Part 1)",
                3 => "Third Class Change (Part 2)",
                _ => $"Quest {questIndex}"
            };
        }

        /// <summary>
        /// Gets quest requirements as human-readable text.
        /// </summary>
        public string GetQuestRequirements(byte questIndex)
        {
            return questIndex switch
            {
                0 => "Reach level 150\nCollect Scroll of the Emperor",
                1 => "Reach level 220\nCollect Marlon's Quest Item",
                2 => "Reach level 380\nCollect Three Divine Items",
                3 => "Reach level 400\nDefeat Dark Elf enemies\nSpeak with Devin",
                _ => "Unknown requirements"
            };
        }

        /// <summary>
        /// Gets quest reward text.
        /// </summary>
        public string GetQuestReward(byte questIndex)
        {
            return questIndex switch
            {
                0 => "First Class Change: New skills unlocked",
                1 => "Second Class Change: Advanced skills and stats",
                2 => "Third Class Change: Master skills (Part 1)",
                3 => "Third Class Change: Master class complete (Part 2)",
                _ => "Unknown reward"
            };
        }

        /// <summary>
        /// Resets all quest state (on map change).
        /// </summary>
        public void Reset()
        {
            _questStates.Clear();
            _killCounts.Clear();
            _currentQuestIndex = 0;
        }
    }
}
