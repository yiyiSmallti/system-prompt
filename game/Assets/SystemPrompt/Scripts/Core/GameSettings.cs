using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SystemPrompt
{
    [Serializable]
    public class GameSettings
    {
        public static readonly string[] TextSpeedLabels = { "很慢", "慢", "标准", "快", "很快", "瞬间" };
        static readonly float[] TextSpeedCps = { 12f, 20f, 30f, 45f, 70f };
        public static readonly int[] FrameCaps = { 30, 60, 120, 0 };
        public static readonly string[] ThinkingSpeedLabels = { "慢", "标准", "快" };

        public float masterVolume = 0.8f;
        public float musicVolume = 0.6f;
        public float sfxVolume = 0.8f;

        public int textSpeed = 2;
        public float autoDelay = 1.5f;
        public bool skipUnread;
        public float boxOpacity = 0.94f;
        public int thinkingSpeed = 1;
        public bool largeText;

        public bool fullscreen;
        public int resolutionWidth;
        public int resolutionHeight;
        public bool vsync = true;
        public int frameCap = 60;

        public bool reduceFlash;
        public bool screenShake = true;
        public bool backgroundMotion = true; // particles and drifting backgrounds

        public float CharsPerSecond => textSpeed >= TextSpeedCps.Length ? float.PositiveInfinity : TextSpeedCps[Mathf.Clamp(textSpeed, 0, TextSpeedCps.Length - 1)];

        public float ThinkingMultiplier => thinkingSpeed switch { 0 => 1.5f, 2 => 5f, _ => 3f };

        public float TextScale => largeText ? 1.15f : 1f;

        static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static GameSettings Load()
        {
            if (!File.Exists(FilePath)) return new GameSettings();
            try
            {
                return JsonUtility.FromJson<GameSettings>(File.ReadAllText(FilePath)) ?? new GameSettings();
            }
            catch (Exception e)
            {
                Debug.LogError($"设置文件无法读取，已改用默认设置：{FilePath}\n{e}");
                return new GameSettings();
            }
        }

        public void Save() => File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));

        public GameSettings Copy() => JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(this));

        public static List<Vector2Int> AvailableResolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!list.Contains(size)) list.Add(size);
            }
            var current = new Vector2Int(Screen.width, Screen.height);
            if (!list.Contains(current)) list.Add(current);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        public void ApplyDisplay()
        {
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            Application.targetFrameRate = frameCap > 0 ? frameCap : -1;
            if (Application.isEditor) return; // the Game view owns its size in the editor
            var mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var w = resolutionWidth > 0 ? resolutionWidth : Screen.width;
            var h = resolutionHeight > 0 ? resolutionHeight : Screen.height;
            Screen.SetResolution(w, h, mode);
        }
    }

    // Data shared by every save: which lines have been read (for skip), kept apart from the save slots.
    [Serializable]
    public class SystemData
    {
        public List<string> readLines = new List<string>();

        [NonSerialized] HashSet<string> readSet;

        static string FilePath => Path.Combine(Application.persistentDataPath, "system.json");

        public static SystemData Load()
        {
            SystemData data = null;
            if (File.Exists(FilePath))
            {
                try { data = JsonUtility.FromJson<SystemData>(File.ReadAllText(FilePath)); }
                catch (Exception e) { Debug.LogError($"已读记录无法读取：{FilePath}\n{e}"); }
            }
            data ??= new SystemData();
            data.readSet = new HashSet<string>(data.readLines);
            return data;
        }

        public bool IsRead(string lineId) => readSet.Contains(lineId);

        public void MarkRead(string lineId)
        {
            if (string.IsNullOrEmpty(lineId) || !readSet.Add(lineId)) return;
            readLines.Add(lineId);
        }

        public void Save() => File.WriteAllText(FilePath, JsonUtility.ToJson(this));
    }
}
