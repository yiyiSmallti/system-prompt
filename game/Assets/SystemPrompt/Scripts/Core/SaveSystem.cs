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

    // Where a save made at a lit beacon in the latent space left the area (UI/Dungeon/DungeonScreen.cs Capture,
    // Resume): the beacon, the maintenance meter, and what had changed in the maze. Pearls are in GameState already.
    // `area` is empty on every other save (JsonUtility gives an empty one, never null).
    [Serializable]
    public class DungeonSave
    {
        public string area;
        public int beacon;                           // the beacon's index in DungeonWorld.Props
        public float meter;
        public List<int> used = new List<int>();     // props used (fragments, chests, the terminal, beacons lit), by index
        public List<int> gone = new List<int>();     // noise bodies beaten, by index in DungeonWorld.Enemies
        public List<int> blocks = new List<int>();   // each cache block's cell, x then y, in SeaPuzzles.Blocks order
        public List<int> plates = new List<int>();   // each light plate's facing, in SeaPuzzles.Plates order
        public List<string> zones = new List<string>(); // zones whose banner has been shown
        public bool warned, beaconTold, spottedTold, firewallTold, ambushTold, stuckTold, sodaTold;
    }

    // A save is a checkpoint taken when a Yarn node starts: loading replays that node from its first line. One
    // made at a beacon is the state at that moment instead, with the area's (dungeon), and goes back into the maze.
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
        public DungeonSave dungeon = new DungeonSave();
        public bool InDungeon => dungeon != null && !string.IsNullOrEmpty(dungeon.area);

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
