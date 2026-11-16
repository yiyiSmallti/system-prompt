using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Battle
{
    public enum Element { Physical, Fire, Ice, Elec, Wind, Almighty }

    public enum Affinity { Normal, Weak, Resist, Null }

    // Level-based stats from design/stats.md: HP/SP grow linearly, the five stats grow by their growth
    // values (each row sums to +4 per level) with fractions carried over.
    public sealed class CombatStats
    {
        public int MaxHp;
        public int MaxSp;
        public int Strength;   // 体能
        public int Magic;      // 算力
        public int Endurance;  // 稳定
        public int Agility;    // 响应
        public int Luck;       // 温度

        sealed class Growth
        {
            public int Hp, Sp, HpPerLevel, SpPerLevel;
            public float[] Base;   // 体能/算力/稳定/响应/温度 at level 1
            public float[] PerLevel;
        }

        static readonly Dictionary<string, Growth> Table = new Dictionary<string, Growth>
        {
            [GameState.PlayerId] = new Growth { Hp = 60, Sp = 20, HpPerLevel = 9, SpPerLevel = 5, Base = new[] { 4f, 2f, 3f, 4f, 3f }, PerLevel = new[] { 1.0f, 0.6f, 0.8f, 1.0f, 0.6f } },
            ["DeepSeek"] = new Growth { Hp = 45, Sp = 23, HpPerLevel = 6, SpPerLevel = 7, Base = new[] { 2f, 5f, 2f, 2f, 3f }, PerLevel = new[] { 0.5f, 1.4f, 0.7f, 0.5f, 0.9f } },
            ["ChatGPT"] = new Growth { Hp = 55, Sp = 38, HpPerLevel = 8, SpPerLevel = 6, Base = new[] { 2f, 4f, 4f, 3f, 2f }, PerLevel = new[] { 0.6f, 1.0f, 1.0f, 0.8f, 0.6f } },
            ["Claude"] = new Growth { Hp = 50, Sp = 42, HpPerLevel = 7, SpPerLevel = 7, Base = new[] { 2f, 5f, 3f, 3f, 2f }, PerLevel = new[] { 0.5f, 1.2f, 0.8f, 0.8f, 0.7f } },
        };

        public static CombatStats ForCharacter(string id, int level)
        {
            if (!Table.TryGetValue(id, out var g))
            {
                Debug.LogError($"design/stats.md 里没有 {id} 的成长值");
                g = Table[GameState.PlayerId];
            }
            var n = Mathf.Clamp(level, 1, 99) - 1;
            int Stat(int i) => Mathf.Min(99, Mathf.FloorToInt(g.Base[i] + g.PerLevel[i] * n));
            return new CombatStats
            {
                MaxHp = Mathf.Min(999, g.Hp + g.HpPerLevel * n),
                MaxSp = Mathf.Min(999, g.Sp + g.SpPerLevel * n),
                Strength = Stat(0),
                Magic = Stat(1),
                Endurance = Stat(2),
                Agility = Stat(3),
                Luck = Stat(4),
            };
        }

        // Cumulative EXP to reach a level: 50 × (level − 1)².
        public static int ExpForLevel(int level) => 50 * (level - 1) * (level - 1);

        public static int LevelForExp(int exp)
        {
            var level = 1;
            while (level < 99 && exp >= ExpForLevel(level + 1)) level++;
            return level;
        }
    }
}
