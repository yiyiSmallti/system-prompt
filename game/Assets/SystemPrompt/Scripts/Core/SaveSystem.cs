using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SystemPrompt
{
    [Serializable]
    public class StringVar { public string name; public string value; }

    [Serializable]
    public class FloatVar { public string name; public float value; }

    [Serializable]
    public class BoolVar { public string name; public bool value; }

    // A save is a checkpoint taken when a Yarn node starts: loading replays that node from its first line.
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public string savedAt;
        public float playSeconds;
        public string node;
        public GameState state;
        public List<StringVar> strings = new List<StringVar>();
        public List<FloatVar> floats = new List<FloatVar>();
        public List<BoolVar> bools = new List<BoolVar>();

        public DateTime SavedAtLocal =>
            DateTime.TryParse(savedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime() : DateTime.MinValue;
    }

    public sealed class SaveSlotInfo
    {
        public int Slot;
        public SaveData Data;
        public string Error;
        public bool IsEmpty => Data == null && Error == null;
    }

    public static class SaveSystem
    {
        public const int QuickSlot = 0;
        public const int SlotCount = 13; // quick save + 12 manual slots

        static string Dir => Path.Combine(Application.persistentDataPath, "saves");

        static string PathFor(int slot) => Path.Combine(Dir, $"slot_{slot:00}.json");

        public static SaveSlotInfo Read(int slot)
        {
            var info = new SaveSlotInfo { Slot = slot };
            var path = PathFor(slot);
            if (!File.Exists(path)) return info;
            try
            {
                info.Data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (info.Data?.state == null || string.IsNullOrEmpty(info.Data.node))
                {
                    info.Data = null;
                    info.Error = "存档内容不完整";
                }
            }
            catch (Exception e)
            {
                info.Error = "存档无法读取";
                Debug.LogError($"存档 {path} 无法读取：{e}");
            }
            return info;
        }

        public static List<SaveSlotInfo> ReadAll()
        {
            var list = new List<SaveSlotInfo>();
            for (var i = 0; i < SlotCount; i++) list.Add(Read(i));
            return list;
        }

        public static void Write(int slot, SaveData data)
        {
            Directory.CreateDirectory(Dir);
            data.savedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var path = PathFor(slot);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public static void Delete(int slot)
        {
            var path = PathFor(slot);
            if (File.Exists(path)) File.Delete(path);
        }

        public static SaveSlotInfo MostRecent()
        {
            SaveSlotInfo best = null;
            foreach (var info in ReadAll())
            {
                if (info.Data == null) continue;
                if (best == null || info.Data.SavedAtLocal > best.Data.SavedAtLocal) best = info;
            }
            return best;
        }
    }
}
