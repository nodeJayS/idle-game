#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Local player identity (PlayerPrefs). A name + a random 4-digit tag for
    /// uniqueness, shown as "Name#1234" (e.g. Jae#1234). Generated once on first run;
    /// the server will own real identity later.
    /// </summary>
    public static class Account
    {
        public static string Name
        {
            get => PlayerPrefs.GetString("accountName", "Adventurer");
            set
            {
                var n = string.IsNullOrWhiteSpace(value) ? "Adventurer" : value.Trim();
                if (n.Length > 16) n = n.Substring(0, 16);
                PlayerPrefs.SetString("accountName", n);
                PlayerPrefs.Save();
            }
        }

        public static int Tag
        {
            get
            {
                if (!PlayerPrefs.HasKey("accountTag"))
                {
                    PlayerPrefs.SetInt("accountTag", Random.Range(0, 10000));
                    PlayerPrefs.Save();
                }
                return PlayerPrefs.GetInt("accountTag");
            }
        }

        public static string Display => $"{Name}#{Tag:0000}";
    }
}
