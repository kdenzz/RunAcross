using UnityEngine;

namespace RunAcross.Core
{
    /// <summary>The level list, which levels are unlocked, and best times. Level 01 also hosts the main menu.</summary>
    public static class Levels
    {
        public struct Info
        {
            public string scene, title;
            public Info(string scene, string title) { this.scene = scene; this.title = title; }
        }

        public static readonly Info[] All =
        {
            new Info("Level_01", "The Arches"),
            new Info("Level_02", "The Drift"),
        };

        public static int Count => All.Length;
        public static string Label(int i) => $"LEVEL {i + 1:00}  —  {All[i].title.ToUpperInvariant()}";

        public static bool IsUnlocked(int i) => i == 0 || PlayerPrefs.GetInt("ra.unlocked." + i, 0) == 1;

        public static void Unlock(int i)
        {
            if (i <= 0 || i >= Count) return;
            PlayerPrefs.SetInt("ra.unlocked." + i, 1);
            PlayerPrefs.Save();
        }

        public static float BestTime(int i) => PlayerPrefs.GetFloat($"ra.best.level{i + 1:00}", 0f);

        public static void SetBestTime(int i, float t)
        {
            PlayerPrefs.SetFloat($"ra.best.level{i + 1:00}", t);
            PlayerPrefs.Save();
        }

        public static int Cleared()
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (BestTime(i) > 0f) n++;
            return n;
        }
    }
}
