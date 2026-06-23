using Photon.Pun;
using Photon.Realtime;
using SevsBetterNametags;
using UnityEngine;

namespace SevsBetterNametags
{
    public class Api : MonoBehaviour
    {
        public static Api Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public string LocalUserId => PhotonNetwork.LocalPlayer?.UserId ?? "";

        public bool IsInRoom => PhotonNetwork.InRoom;

        private static Player FindPlayer(string userId)
        {
            return string.IsNullOrEmpty(userId) ? null : Plugin.GetPhotonPlayerByUserId(userId);
        }

        private static string ReadProp(string userId, string key)
        {
            var player = FindPlayer(userId);
            if (player?.CustomProperties != null && player.CustomProperties.TryGetValue(key, out object value))
                return value?.ToString() ?? "";
            return "";
        }

        public string GetPronouns(string userId)
        {
            return Plugin.Sanitize(ReadProp(userId, Plugin.KEY_PRONOUNS));
        }

        public string GetCustomName(string userId)
        {
            return Plugin.Sanitize(ReadProp(userId, Plugin.KEY_NAME));
        }

        public string GetDisplayName(string userId)
        {
            string custom = GetCustomName(userId);
            if (!string.IsNullOrEmpty(custom)) return custom;

            var player = FindPlayer(userId);
            return Plugin.Sanitize(player?.NickName ?? "");
        }

        public string GetPronounColorData(string userId)
        {
            return ReadProp(userId, Plugin.KEY_PRONCOLOR);
        }

        public string GetFontName(string userId)
        {
            return ReadProp(userId, Plugin.KEY_FONT);
        }

        public bool HasPronouns(string userId)
        {
            return !string.IsNullOrEmpty(GetPronouns(userId));
        }

        public bool IsVerified(string userId)
        {
            return !string.IsNullOrEmpty(userId) && Plugin.UserColors.ContainsKey(userId);
        }

        public bool TryGetVerifiedColor(string userId, out Color color)
        {
            color = Color.white;
            return !string.IsNullOrEmpty(userId) && Plugin.UserColors.TryGetValue(userId, out color);
        }

        public string GetVerifiedLabel(string userId)
        {
            if (!string.IsNullOrEmpty(userId) && Plugin.UserLabels.TryGetValue(userId, out string label))
                return label;
            return "";
        }

        public string GetPronounsMarkup(string userId, float fontSize = 12f)
        {
            return Plugin.BuildPronounsMarkup(GetPronouns(userId), GetPronounColorData(userId), fontSize);
        }
    }
}
