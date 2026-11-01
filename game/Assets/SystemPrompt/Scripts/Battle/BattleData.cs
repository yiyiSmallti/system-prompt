using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SystemPrompt.Battle
{
    public enum SkillKind { Attack, Magic, Charge, Item }

    // How a battle in the latent space began (design/systems.md 遇敌): hitting a noise body before it has
    // seen the player gives the party a free round, being run into by one that chased the player lets the
    // enemies act first.
    public enum BattleOpening { Normal, Preemptive, Ambush }

    public sealed class SkillDef
    {
        public string Id;
        public string Name;
        public Element Element;
        public int Power;
        public int SpCost;
        public bool AllTargets;
        public SkillKind Kind;
        public string Description;
    }

    // Skills used in the prologue (design/skills.md). Normal attacks use the weapon's attack as power.
    public static class Skills
    {
        public static readonly SkillDef Screenshot = new SkillDef { Id = "screenshot", Name = "截图", Element = Element.Almighty, Kind = SkillKind.Attack, Description = "举起手机对敌人截图。万能攻击，不看相性。" };
        public static readonly SkillDef Spoon = new SkillDef { Id = "spoon", Name = "饭勺", Element = Element.Physical, Kind = SkillKind.Attack, Description = "用饭勺敲。物理攻击。" };
        public static readonly SkillDef ColdCurrent = new SkillDef { Id = "cold_current", Name = "寒流", Element = Element.Ice, Power = 30, SpCost = 4, Kind = SkillKind.Magic, Description = "冰属性，敌方一体。" };
        public static readonly SkillDef DeepCurrent = new SkillDef { Id = "deep_current", Name = "深海潜流", Element = Element.Ice, Power = 20, SpCost = 8, AllTargets = true, Kind = SkillKind.Magic, Description = "冰属性，敌方全体。" };
        public static readonly SkillDef DeepThink = new SkillDef { Id = "deep_think", Name = "深度思考", Kind = SkillKind.Charge, Description = "本回合不行动，下一次技能伤害 ×2.5。" };
        public static readonly SkillDef NoiseStrike = new SkillDef { Id = "noise_strike", Name = "震惊！", Element = Element.Physical, Power = 17, Kind = SkillKind.Attack };
        public static readonly SkillDef Repost = new SkillDef { Id = "repost", Name = "转发保平安", Element = Element.Physical, Power = 12, AllTargets = true, Kind = SkillKind.Attack };
        // 冷知识型's lecture: one member, hit harder than 震惊！ (design/systems.md 噪声体图鉴).
        public static readonly SkillDef ColdFact = new SkillDef { Id = "cold_fact", Name = "你知道吗？", Element = Element.Physical, Power = 20, Kind = SkillKind.Attack };

        public static SkillDef NormalAttack(string id) => id == GameState.PlayerId ? Screenshot : Spoon;

        public static IReadOnlyList<SkillDef> MagicOf(string id) =>
            id == "DeepSeek" ? new[] { ColdCurrent, DeepCurrent } : new SkillDef[0];

        public static bool CanThink(string id) => id == "DeepSeek";

        // An attack item thrown (冰镇汽水, 辣条): as strong as a skill of its level, by the user's 算力
        // (design/items.md), but no skill, so 深度思考 does not charge it.
        public static SkillDef OfItem(ItemDef item) => new SkillDef { Id = item.Id, Name = item.Name, Element = item.Element, Power = item.Power, Kind = SkillKind.Item };

        // 原装镜头 20 / 命中 90 for the player (design/items.md); DeepSeek's 饭勺 is weaker.
        public static int WeaponAttack(string id) => id == GameState.PlayerId ? 20 : 15;
        public static int WeaponAccuracy(string id) => id == GameState.PlayerId ? 90 : 95;
    }

    public sealed class Combatant
    {
        public string Id;
        public string Name;
        public bool IsEnemy;
        public string Kind; // enemy body: "clickbait" / "repost"
        public int Hp, MaxHp, Sp, MaxSp;
        public float Str, Mag, End, Agi, Luck;
        public readonly Dictionary<Element, Affinity> Affinities = new Dictionary<Element, Affinity>();
        public bool Down;
        public bool Guarding;
        public bool Charged;
        public int HpFloor;            // tutorial: never drops below this until the finisher
        public string WeaknessLabel;   // what the nameplate shows, which may be a lie (标题党型)
        public string TrueWeakness;
        public readonly HashSet<Element> Known = new HashSet<Element>(); // affinities a hit has shown this battle
        public bool Summoned;          // called in by a 转发型 mid-battle

        public bool Dead => Hp <= 0;
        public Affinity AffinityTo(Element e) => Affinities.TryGetValue(e, out var a) ? a : Affinity.Normal;

        public static Combatant FromMember(PartyMember m, string displayName)
        {
            var s = CombatStats.ForCharacter(m.id, m.level);
            var c = new Combatant
            {
                Id = m.id, Name = displayName,
                MaxHp = s.MaxHp, MaxSp = s.MaxSp,
                Hp = Mathf.Clamp(m.hp, 1, s.MaxHp), Sp = Mathf.Clamp(m.sp, 0, s.MaxSp),
                Str = s.Strength, Mag = s.Magic, End = s.Endurance, Agi = s.Agility, Luck = s.Luck,
            };
            if (m.id == "DeepSeek")
            {
                c.Affinities[Element.Ice] = Affinity.Resist;
                c.Affinities[Element.Fire] = Affinity.Weak;
            }
            return c;
        }
    }

    public sealed class EnemyDef
    {
        public string Kind;
        public string Name;
        public int Level = 1;
        public int Hp;
        public Element Weak;
        public Element? Resist;
        public string FakeWeakness;
    }

    public sealed class BattleDef
    {
        public string Id;
        public EnemyDef[] Enemies;
        public int Exp;
        public (string Id, int Count)[] Drops;
        public bool Tutorial;
        public float EnemyDamageScale = 1f;

        // Two battles fought as one when a noise body called a neighbour in (UI/Dungeon/DungeonScreen.cs): every
        // enemy of both, the rewards added up, the harder hitters' scale.
        public static BattleDef Merge(BattleDef a, BattleDef b) => new BattleDef
        {
            Id = a.Id + "+" + b.Id,
            Enemies = a.Enemies.Concat(b.Enemies).ToArray(),
            Exp = a.Exp + b.Exp,
            Drops = (a.Drops ?? new (string, int)[0]).Concat(b.Drops ?? new (string, int)[0]).ToArray(),
            Tutorial = false,
            EnemyDamageScale = Mathf.Max(a.EnemyDamageScale, b.EnemyDamageScale),
        };
    }

    public static class Battles
    {
        // A free round won from behind is worth more (design/stats.md 行动顺序): EXP ×1.3 and the drops doubled.
        public const float PreemptiveExp = 1.3f;
        public const int PreemptiveDrops = 2;
        // The most noise bodies on the field at once, called in or joined from the maze.
        public const int MaxFoes = 4;

        static readonly Dictionary<string, BattleDef> All = new Dictionary<string, BattleDef>
        {
            // story/00-prologue.md 场景5. Tutorial numbers are set apart so enemies fall in the scripted order.
            ["prologue_tutorial"] = new BattleDef
            {
                Id = "prologue_tutorial",
                Enemies = new[]
                {
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 30, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                    new EnemyDef { Kind = "repost", Name = "转发型", Hp = 44, Weak = Element.Ice, Resist = Element.Wind },
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 30, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                },
                Exp = 120,
                Drops = new[] { ("white_rice", 1) },
                Tutorial = true,
                EnemyDamageScale = 0.35f,
            },
            // The shallows of the cache sea, before the awakening: the player alone with the phone, so small
            // groups (Dungeon/DungeonData.cs). They hit hard enough that a round given away costs a sixth of
            // the player's HP or more (design/prologue-playtest.md 改法 1): a beacon is worth the detour.
            ["shallow_clickbait"] = new BattleDef
            {
                Id = "shallow_clickbait",
                Enemies = new[]
                {
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 24, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                },
                Exp = 30,
                Drops = new[] { ("ice_soda", 1) },
                EnemyDamageScale = 0.7f,
            },
            ["shallow_repost"] = new BattleDef
            {
                Id = "shallow_repost",
                Enemies = new[]
                {
                    new EnemyDef { Kind = "repost", Name = "转发型", Hp = 32, Weak = Element.Ice, Resist = Element.Wind },
                },
                Exp = 35,
                Drops = new[] { ("white_rice", 1) },
                EnemyDamageScale = 0.7f,
            },
            // By the graveyard the player is level 2 and a screenshot does about 23: these take two each, so the
            // pair gets a round or two in unless the 冰镇汽水 drops one at once.
            ["shallow_pair"] = new BattleDef
            {
                Id = "shallow_pair",
                Enemies = new[]
                {
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 28, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 28, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                },
                Exp = 50,
                Drops = new[] { ("spicy_strip", 1) },
                EnemyDamageScale = 0.6f,
            },
            ["shallow_mixed"] = new BattleDef
            {
                Id = "shallow_mixed",
                Enemies = new[]
                {
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 28, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                    new EnemyDef { Kind = "repost", Name = "转发型", Hp = 34, Weak = Element.Ice, Resist = Element.Wind },
                },
                Exp = 55,
                EnemyDamageScale = 0.6f,
            },
            // The noise bodies roaming the deep layer of the cache sea, the party fighting together. 冷知识型
            // shrugs off ice, so DeepSeek's 深海潜流 alone no longer ends a battle: its 「弱点：火」 is true while
            // the 标题党型 beside it says the same and lies (design/prologue-playtest.md 改法 2、3).
            ["sea_clickbait"] = new BattleDef
            {
                Id = "sea_clickbait",
                Enemies = new[]
                {
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 26, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 26, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                },
                Exp = 40,
                Drops = new[] { ("ice_soda", 1) },
                EnemyDamageScale = 0.4f,
            },
            ["sea_mixed"] = new BattleDef
            {
                Id = "sea_mixed",
                Enemies = new[]
                {
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 26, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                    new EnemyDef { Kind = "repost", Name = "转发型", Hp = 38, Weak = Element.Ice, Resist = Element.Wind },
                },
                Exp = 55,
                Drops = new[] { ("white_rice", 1) },
                EnemyDamageScale = 0.4f,
            },
            ["sea_coldfact"] = new BattleDef
            {
                Id = "sea_coldfact",
                Enemies = new[]
                {
                    new EnemyDef { Kind = "coldfact", Name = "冷知识型", Hp = 44, Weak = Element.Fire, Resist = Element.Ice },
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 26, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                },
                Exp = 60,
                Drops = new[] { ("spicy_strip", 1) },
                EnemyDamageScale = 0.4f,
            },
            ["sea_repost"] = new BattleDef
            {
                Id = "sea_repost",
                Enemies = new[]
                {
                    new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 26, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" },
                    new EnemyDef { Kind = "repost", Name = "转发型", Hp = 38, Weak = Element.Ice, Resist = Element.Wind },
                    new EnemyDef { Kind = "coldfact", Name = "冷知识型", Hp = 44, Weak = Element.Fire, Resist = Element.Ice },
                },
                Exp = 80,
                EnemyDamageScale = 0.4f,
            },
        };

        public static BattleDef Get(string id) => All.TryGetValue(id, out var d) ? d : null;

        // What a 转发型 calls in every other turn (design/systems.md 转发型「每回合召唤一只新的标题党型」): a
        // weaker 标题党型, so killing the 转发型 first pays but leaving it is no wall.
        public static readonly EnemyDef Summoned = new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = 16, Weak = Element.Ice, Resist = Element.Fire, FakeWeakness = "火" };
        public const int SummonedExp = 10;

        public static Combatant Spawn(EnemyDef e)
        {
            var stat = 3f + 0.8f * e.Level;
            var c = new Combatant
            {
                Id = e.Kind, Name = e.Name, Kind = e.Kind, IsEnemy = true,
                Hp = e.Hp, MaxHp = e.Hp,
                Str = stat, Mag = stat, End = stat, Agi = stat, Luck = stat,
                TrueWeakness = ElementLabel(e.Weak),
            };
            c.Affinities[e.Weak] = Affinity.Weak;
            if (e.Resist.HasValue) c.Affinities[e.Resist.Value] = Affinity.Resist;
            c.WeaknessLabel = e.FakeWeakness ?? c.TrueWeakness;
            return c;
        }

        public static string ElementLabel(Element e) => e switch
        {
            Element.Physical => "物理", Element.Fire => "火", Element.Ice => "冰",
            Element.Elec => "电", Element.Wind => "风", _ => "万能",
        };
    }

    public struct Hit
    {
        public Element Element;
        public int Damage;
        public Affinity Affinity;
        public bool Critical;
        public bool Missed;
        public bool KnockedDown;
    }

    // design/stats.md 伤害公式. The tutorial turns off misses and crits so nothing happens off script.
    public static class DamageCalc
    {
        public const float ChargeMultiplier = 2.5f;

        public static Hit Compute(Combatant a, Combatant t, SkillDef s, BattleDef battle)
        {
            var hit = new Hit { Element = s.Element, Affinity = t.AffinityTo(s.Element) };
            var tutorial = battle.Tutorial;
            var physical = s.Element == Element.Physical;
            // A normal attack counts as physical for hit and crit even when it is not (the screenshot is almighty).
            var struck = physical || s.Kind == SkillKind.Attack;

            if (!tutorial)
            {
                var baseHit = struck && !a.IsEnemy ? Skills.WeaponAccuracy(a.Id) : 95f;
                var chance = Mathf.Clamp(baseHit * (1f + (a.Agi - t.Agi) / 100f), 5f, 99f);
                if (Random.value * 100f >= chance)
                {
                    hit.Missed = true;
                    return hit;
                }
            }

            float amount;
            if (s.Kind == SkillKind.Attack && !a.IsEnemy) amount = Skills.WeaponAttack(a.Id) * Mathf.Sqrt(a.Str / Mathf.Max(1f, t.End));
            else if (physical) amount = s.Power * Mathf.Sqrt(a.Str / Mathf.Max(1f, t.End));
            else amount = s.Power * Mathf.Sqrt(a.Mag / Mathf.Max(1f, t.End));

            amount *= hit.Affinity switch { Affinity.Weak => 1.5f, Affinity.Resist => 0.5f, Affinity.Null => 0f, _ => 1f };

            if (struck && !tutorial && hit.Affinity != Affinity.Null)
            {
                var critRate = Mathf.Clamp(5f + (a.Luck - t.Luck) * 0.5f, 1f, 40f);
                hit.Critical = Random.value * 100f < critRate;
                if (hit.Critical) amount *= 1.5f;
            }
            if (a.Charged && s.Kind == SkillKind.Magic) amount *= ChargeMultiplier;
            if (t.Guarding) amount *= 0.5f;
            if (a.IsEnemy) amount *= battle.EnemyDamageScale;
            amount *= Random.Range(0.95f, 1.05f);

            hit.Damage = hit.Affinity == Affinity.Null ? 0 : Mathf.Max(1, Mathf.RoundToInt(amount));
            // Weakness or a crit knocks the target down; one already down gives no further One More.
            hit.KnockedDown = (hit.Affinity == Affinity.Weak || hit.Critical) && !t.Down;
            return hit;
        }
    }

    // What the tutorial lets each party member do on each turn (story/00-prologue.md 场景5).
    public sealed class TurnRule
    {
        public string[] Commands; // "attack", "skill", "think", "guard", "item"
        public string Skill;      // the only skill allowed, when "skill" is allowed
        public string Tip;
    }

    public static class Tutorial
    {
        public const string OneMoreHint = "Prologue_Battle_OneMore";
        public const string AllOutHint = "Prologue_Battle_AllOut";

        public static string HintAtTurnStart(int turn) => turn switch
        {
            1 => "Prologue_Battle_Turn1",
            2 => "Prologue_Battle_Turn2",
            3 => "Prologue_Battle_Turn3",
            _ => null,
        };

        public static TurnRule Rule(int turn, string actor, bool oneMore)
        {
            if (actor == GameState.PlayerId)
            {
                return turn == 1
                    ? new TurnRule { Commands = new[] { "attack" }, Tip = "选择「攻击」，对敌人截图" }
                    : new TurnRule { Commands = new[] { "attack", "guard", "item" } };
            }
            if (oneMore) return new TurnRule { Commands = new[] { "attack" }, Tip = "One More：再攻击一次" };
            return turn switch
            {
                1 => new TurnRule { Commands = new[] { "attack" }, Tip = "选择「攻击」" },
                2 => new TurnRule { Commands = new[] { "skill" }, Skill = Skills.ColdCurrent.Id, Tip = "技能 →「寒流」，瞄准标题党型的真弱点" },
                3 => new TurnRule { Commands = new[] { "think" }, Tip = "选择「深度思考」" },
                4 => new TurnRule { Commands = new[] { "skill" }, Skill = Skills.DeepCurrent.Id, Tip = "技能 →「深海潜流」，冰属性全体攻击" },
                _ => new TurnRule { Commands = new[] { "attack", "skill", "think", "guard", "item" } },
            };
        }
    }
}
