using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Personal bests per level, saved between sessions with PlayerPrefs (on this device, per user account).
    /// Best time = fastest completion; best stars = most stars ever earned. Keys start with "EinsteinLaserKit.".
    /// </summary>
    public static class LaserBestTimes
    {
        private const string Prefix = "EinsteinLaserKit.Best.";

        public static bool TryGet(string levelKey, out float bestSeconds, out int bestStars)
        {
            string key = Key(levelKey);
            bestSeconds = PlayerPrefs.GetFloat(key + ".Time", -1f);
            bestStars = PlayerPrefs.GetInt(key + ".Stars", 0);
            return bestSeconds >= 0f;
        }

        /// <summary>Number of times this level was completed.</summary>
        public static int Completions(string levelKey) => PlayerPrefs.GetInt(Key(levelKey) + ".Count", 0);

        /// <summary>Store a finished run. Returns true when it is a new best time (or the first completion).</summary>
        public static bool Submit(string levelKey, float seconds, int stars)
        {
            string key = Key(levelKey);
            bool hadBest = TryGet(levelKey, out float bestSeconds, out int bestStars);
            bool newBest = !hadBest || seconds < bestSeconds;
            if (newBest)
            {
                PlayerPrefs.SetFloat(key + ".Time", seconds);
            }

            if (stars > bestStars)
            {
                PlayerPrefs.SetInt(key + ".Stars", stars);
            }

            PlayerPrefs.SetInt(key + ".Count", Completions(levelKey) + 1);
            PlayerPrefs.Save();
            return newBest;
        }

        public static void Clear(string levelKey)
        {
            string key = Key(levelKey);
            PlayerPrefs.DeleteKey(key + ".Time");
            PlayerPrefs.DeleteKey(key + ".Stars");
            PlayerPrefs.DeleteKey(key + ".Count");
            PlayerPrefs.Save();
        }

        /// <summary>"1:23" for 83 seconds.</summary>
        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        private static string Key(string levelKey)
        {
            var clean = new System.Text.StringBuilder(levelKey.Length);
            foreach (char c in levelKey)
            {
                clean.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return Prefix + clean;
        }
    }
}
