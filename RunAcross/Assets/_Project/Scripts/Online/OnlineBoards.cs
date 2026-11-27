using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RunAcross.Core;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Exceptions;
using Unity.Services.Leaderboards.Models;
using UnityEngine;

namespace RunAcross.Online
{
    public struct BoardRow
    {
        public int rank;      // 1-based
        public string name;
        public float time;
        public bool isMe;
    }

    /// <summary>One level's board: the top rows plus the player's own entry (which may sit outside the top).</summary>
    public class BoardView
    {
        public string error;  // null when the fetch worked
        public int total;
        public readonly List<BoardRow> top = new();
        public BoardRow? me;
    }

    /// <summary>
    /// Online per-level leaderboards on Unity Gaming Services: anonymous sign-in, a player-chosen callsign,
    /// and one ascending keep-best board per level (Services/level_NN.lb). Every call fails soft: when offline
    /// the game keeps its local best times and parks unsent runs in PlayerPrefs until the next sign-in.
    /// </summary>
    public static class OnlineBoards
    {
        // From the Core registry, not LeaderboardsService.Instance: in the Editor the package nulls that static
        // after the first scene loads, which wipes it if sign-in started during Awake.
        static ILeaderboardsService LB => UnityServices.Instance.GetLeaderboardsService();

        static Task<bool> _ready;
        static string _name;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _ready = null; _name = null; }

        public static bool IsReady => _ready is { IsCompletedSuccessfully: true, Result: true };
        public static bool HasName => !string.IsNullOrEmpty(_name);

        /// <summary>The callsign without UGS's "#1234" suffix.</summary>
        public static string DisplayName => Strip(_name);

        static string Strip(string n)
        {
            if (string.IsNullOrEmpty(n)) return "";
            int hash = n.LastIndexOf('#');
            return hash > 0 ? n.Substring(0, hash) : n;
        }

        /// <summary>Initialises services and signs in once; retries on the next call if the last attempt failed.</summary>
        public static Task<bool> EnsureReady()
        {
            if (_ready == null || (_ready.IsCompleted && !IsReady)) _ready = Init();
            return _ready;
        }

        static async Task<bool> Init()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();
                Debug.Log("[Online] services initialised");

                var auth = AuthenticationService.Instance;
                if (!auth.IsSignedIn) await auth.SignInAnonymouslyAsync();
                Debug.Log($"[Online] signed in as {auth.PlayerId}");

                _name = await auth.GetPlayerNameAsync(autoGenerate: false);
                Debug.Log($"[Online] callsign: {(HasName ? _name : "(none yet)")}");

                await FlushPending();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Online] offline: {e.Message}");
                return false;
            }
        }

        // ---------------------------------------------------------------- callsign

        public const int NameMin = 3, NameMax = 16;

        /// <summary>Returns an error to show, or null when the name is acceptable.</summary>
        public static string ValidateName(string name)
        {
            name = name?.Trim() ?? "";
            if (name.Length < NameMin || name.Length > NameMax) return $"USE {NameMin}–{NameMax} CHARACTERS";
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) && c < 128) && c != '_' && c != '-')
                    return "LETTERS, DIGITS, _ AND - ONLY";
            return null;
        }

        /// <summary>Sets the callsign. Returns null on success, otherwise an error to show.</summary>
        public static async Task<string> SetName(string name)
        {
            string invalid = ValidateName(name);
            if (invalid != null) return invalid;
            if (!await EnsureReady()) return "OFFLINE · TRY AGAIN LATER";
            try
            {
                _name = await AuthenticationService.Instance.UpdatePlayerNameAsync(name.Trim());
                Debug.Log($"[Online] callsign set: {_name}");
                await FlushPending();
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Online] name update failed: {e.Message}");
                return "COULDN'T SAVE THAT NAME";
            }
        }

        // ---------------------------------------------------------------- scores

        static string PendingKey(int level) => $"ra.pending.level{level + 1:00}";

        /// <summary>Parks a run to submit later, keeping only the fastest unsent one per level.</summary>
        public static void SavePending(int level, float seconds)
        {
            float old = PlayerPrefs.GetFloat(PendingKey(level), 0f);
            if (old > 0f && old <= seconds) return;
            PlayerPrefs.SetFloat(PendingKey(level), seconds);
            PlayerPrefs.Save();
        }

        static async Task FlushPending()
        {
            if (!HasName) return; // without a callsign UGS would invent one
            for (int i = 0; i < Levels.Count; i++)
            {
                float t = PlayerPrefs.GetFloat(PendingKey(i), 0f);
                if (t <= 0f) continue;
                if (await Send(i, t))
                {
                    PlayerPrefs.DeleteKey(PendingKey(i));
                    PlayerPrefs.Save();
                    Debug.Log($"[Online] sent parked run for level {i + 1}: {t:0.00}s");
                }
            }
        }

        /// <summary>Submits a run (the board keeps each player's best). Parks it locally when it can't be sent.</summary>
        public static async Task<bool> Submit(int level, float seconds)
        {
            if (await EnsureReady() && HasName && await Send(level, seconds)) return true;
            SavePending(level, seconds);
            return false;
        }

        static async Task<bool> Send(int level, float seconds)
        {
            try
            {
                var e = await LB.AddPlayerScoreAsync(Levels.All[level].boardId, Math.Round(seconds, 3));
                Debug.Log($"[Online] submitted {seconds:0.00}s to {Levels.All[level].boardId}; best {e.Score:0.00}s, rank {e.Rank + 1}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Online] submit failed: {e.Message}");
                return false;
            }
        }

        public static async Task<BoardView> Fetch(int level, int limit)
        {
            var view = new BoardView();
            if (!await EnsureReady()) { view.error = "OFFLINE"; return view; }
            string id = Levels.All[level].boardId;
            string myId = AuthenticationService.Instance.PlayerId;
            try
            {
                var page = await LB.GetScoresAsync(id, new GetScoresOptions { Limit = limit });
                view.total = page.Total;
                if (page.Results != null)
                    foreach (var e in page.Results) view.top.Add(Row(e, myId));
                try { view.me = Row(await LB.GetPlayerScoreAsync(id), myId); }
                catch (LeaderboardsException e) when (e.Reason == LeaderboardsExceptionReason.EntryNotFound) { }
                Debug.Log($"[Online] fetched {id}: {view.top.Count} of {view.total}");
            }
            catch (LeaderboardsException e) when (e.Reason == LeaderboardsExceptionReason.LeaderboardNotFound)
            {
                Debug.LogWarning($"[Online] leaderboard '{id}' isn't deployed");
                view.error = "BOARD NOT SET UP YET";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Online] fetch failed: {e.Message}");
                view.error = "OFFLINE";
            }
            return view;
        }

        static BoardRow Row(LeaderboardEntry e, string myId) => new()
        {
            rank = e.Rank + 1,
            name = Strip(e.PlayerName),
            time = (float)e.Score,
            isMe = e.PlayerId == myId,
        };
    }
}
