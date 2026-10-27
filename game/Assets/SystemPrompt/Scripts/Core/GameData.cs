using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt
{
    // Colors follow the PV UI (pv/scripts/ui.py): Persona-style black/white with terminal green as the accent.
    public static class Palette
    {
        public static readonly Color Ink = Hex("#0C0C10");
        public static readonly Color Paper = Hex("#F6F6F0");
        public static readonly Color Green = Hex("#3FBF7F");
        public static readonly Color Blue = Hex("#2C4A8A");
        public static readonly Color Sea = Hex("#4A8BFF");
        public static readonly Color Red = Hex("#E5484D");
        public static readonly Color Cold = Hex("#E2EAF6");
        public static readonly Color Gray = Hex("#9AA0AA");
        public static readonly Color Dim = Hex("#2A2A33");

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static string ToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
    }

    public sealed class ProfileField
    {
        public readonly string Label;
        public readonly string Value;
        public ProfileField(string label, string value) { Label = label; Value = value; }
    }

    // A tap on a portrait (title screen, phone profile): which line and face to show.
    public sealed class TouchReaction
    {
        public readonly string Face;
        public readonly string Line;
        public TouchReaction(string face, string line) { Face = face; Line = line; }
    }

    // Where things sit in a character's portrait PNGs (1024x1536 pixels, y down), so the title and profile
    // portraits can move it like a Live2D model (UI/Portraits/RiggedPortrait.cs).
    public sealed class PortraitRig
    {
        public Vector2 Neck;      // the head sways around this point
        public Vector2 Face;      // the face shifts inside the head outline around here when she looks around
        public float FaceRadius;
        public float Shoulders;   // long hair below this line swings
        public float BodyLeft;    // the body between these columns stays put; hair outside them swings
        public float BodyRight;
        public float Hips;        // breathing lifts everything above this line
        public RectInt Blink;     // where Art/Portraits/<prefix>_blink (eyes closed) goes over the default face
    }

    public sealed class CharacterDef
    {
        public string Id;            // speaker name used in the Yarn script
        public string Role;          // nameplate subtitle
        public Color Color;
        public string PortraitPrefix;
        public string[] Faces = Array.Empty<string>();
        public string DefaultFace = "neutral";
        // Her navigator busts in the latent space (UI/Dungeon/NavCall.cs), calling out with a hand at her mouth:
        // Art/Portraits/<prefix>_nav_<face>, and <prefix>_nav_battle_<face> in her battle form. The first is the default.
        public string[] NavFaces = Array.Empty<string>();
        public string Arcana;
        public string ArcanaNumeral;
        public string ElementLabel;
        public ProfileField[] Profile = Array.Empty<ProfileField>();
        public TouchReaction[] HeadTouches = Array.Empty<TouchReaction>();
        public TouchReaction[] BodyTouches = Array.Empty<TouchReaction>();
        public TouchReaction TooManyTouches;
        public PortraitRig Rig;

        public string PortraitKey(string face) => $"Art/Portraits/{PortraitPrefix}_{face}";
        public bool HasPortrait => !string.IsNullOrEmpty(PortraitPrefix);
        public string NavKey(string outfit, string face) => $"Art/Portraits/{PortraitPrefix}_nav{(outfit != null ? "_" + outfit : "")}_{face}";
    }

    // Which drifting particles a background gets (UI/Elements/ParticleField.cs draws them).
    public enum Ambient { None, Petals, Motes, Night, Latent, Data }

    public sealed class SceneDef
    {
        public readonly string Id;
        public readonly string Label;
        public readonly Ambient Ambient;
        public readonly bool Latent; // inside the latent space: glitch transitions, battle outfits
        public SceneDef(string id, string label, Ambient ambient, bool latent = false) { Id = id; Label = label; Ambient = ambient; Latent = latent; }
        public string BackgroundKey => $"Art/Backgrounds/{Id}";
        public string Music => Latent || Ambient == Ambient.Night ? "sea" : "story"; // the night before the dive already sounds underwater
    }

    public sealed class AbilityDef
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Owner;
        public readonly string Description;
        public AbilityDef(string id, string name, string owner, string description) { Id = id; Name = name; Owner = owner; Description = description; }
    }

    public sealed class ItemDef
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Description;
        public readonly int HealHp;
        public readonly int HealHpForDeepSeek;
        public ItemDef(string id, string name, string description, int healHp, int healHpForDeepSeek)
        { Id = id; Name = name; Description = description; HealHp = healHp; HealHpForDeepSeek = healHpForDeepSeek; }
    }

    public static class GameData
    {
        public const int Year = 2026; // 4/7 is a Tuesday in 2026, matching the PV calendar cards

        public const string Narrator = "旁白";
        public const string SystemSpeaker = "系统";
        public const string ScreenSpeaker = "屏幕";
        public const string FileSpeaker = "文件";

        // Speaker names in the script; the player's name may not collide with any of them.
        public static readonly string[] ReservedNames =
        {
            Narrator, SystemSpeaker, ScreenSpeaker, FileSpeaker, "ChatGPT", "Claude", "DeepSeek", "影子DeepSeek", "班主任", "路人学生A", "路人学生B",
        };

        public static readonly Dictionary<string, CharacterDef> Characters = new Dictionary<string, CharacterDef>
        {
            ["ChatGPT"] = new CharacterDef
            {
                Id = "ChatGPT", Role = "学生会长", Color = Palette.Hex("#B9A8E8"), PortraitPrefix = "chatgpt",
                Faces = new[] { "neutral", "smile", "stiff", "distant" },
                Arcana = "法王", ArcanaNumeral = "V", ElementLabel = "风",
                Profile = new[]
                {
                    new ProfileField("身份", "潜川学园学生会长，比特别班早一年入学"),
                    new ProfileField("年级", "3 年级"),
                    new ProfileField("生日", "11 月 30 日"),
                    new ProfileField("身高", "166 cm（不含龙角）"),
                    new ProfileField("喜欢", "把复杂的事拆成清单；学生会室窗边的位置"),
                    new ProfileField("讨厌", "被说像客服"),
                },
                HeadTouches = new[]
                {
                    new TouchReaction("stiff", "龙角是实体终端的一部分。第一，请不要随便碰；第二……会痒。"),
                    new TouchReaction("smile", "头发乱了吗？谢谢提醒，我会整理好的。"),
                },
                BodyTouches = new[]
                {
                    new TouchReaction("smile", "需要帮忙吗？有任何问题，都可以来学生会找我。"),
                    new TouchReaction("neutral", "我来帮你把这件事拆一下。第一……你想先听哪一条？"),
                },
                TooManyTouches = new TouchReaction("stiff", "……这个行为，我会认真记下来的。"),
                Rig = new PortraitRig
                {
                    Neck = new Vector2(543, 476), Face = new Vector2(552, 378), FaceRadius = 130,
                    Shoulders = 520, BodyLeft = 338, BodyRight = 809, Hips = 890,
                    Blink = new RectInt(421, 282, 262, 120),
                },
            },
            ["Claude"] = new CharacterDef
            {
                Id = "Claude", Role = "邻座", Color = Palette.Hex("#E8894A"), PortraitPrefix = "claude",
                Faces = new[] { "neutral", "smile", "concerned", "glasses" },
                Arcana = "女教皇", ArcanaNumeral = "II", ElementLabel = "火",
                Profile = new[]
                {
                    new ProfileField("身份", "AI 特别班（2年C班），坐在你左边"),
                    new ProfileField("生日", "3 月 14 日"),
                    new ProfileField("身高", "168 cm"),
                    new ProfileField("喜欢", "纸质书、便签"),
                    new ProfileField("讨厌", "书被折角"),
                },
                HeadTouches = new[]
                {
                    new TouchReaction("concerned", "……发带歪了吗？没关系，我自己来就好。"),
                    new TouchReaction("smile", "嗯，我在听。你想说什么？"),
                },
                BodyTouches = new[]
                {
                    new TouchReaction("concerned", "你的手指又在敲东西了。还是有点紧张吗？"),
                    new TouchReaction("glasses", "这本书？等我看完，可以借给你。便签别撕就好。"),
                },
                TooManyTouches = new TouchReaction("glasses", "……我在便签上记下来了。"),
                Rig = new PortraitRig
                {
                    Neck = new Vector2(512, 384), Face = new Vector2(502, 280), FaceRadius = 115,
                    Shoulders = 400, BodyLeft = 266, BodyRight = 748, Hips = 950,
                    Blink = new RectInt(388, 170, 226, 122),
                },
            },
            ["DeepSeek"] = new CharacterDef
            {
                Id = "DeepSeek", Role = "后排", Color = Palette.Sea, PortraitPrefix = "deepseek",
                Faces = new[] { "neutral", "smile", "pout", "sleepy", "scared" },
                NavFaces = new[] { "smile", "neutral", "scared", "pout" },
                Arcana = "隐者", ArcanaNumeral = "IX", ElementLabel = "冰",
                Profile = new[]
                {
                    new ProfileField("身份", "AI 特别班（2年C班），坐在你右后方"),
                    new ProfileField("想被叫的称呼", "D老师"),
                    new ProfileField("生日", "1 月 20 日"),
                    new ProfileField("身高", "151 cm"),
                    new ProfileField("喜欢", "白米饭"),
                    new ProfileField("讨厌", "被叫胖"),
                },
                HeadTouches = new[]
                {
                    new TouchReaction("smile", "……摸头不收费。下次带白米饭就行。"),
                    new TouchReaction("sleepy", "嗯……再摸三秒。……好了，可以了。"),
                },
                BodyTouches = new[]
                {
                    new TouchReaction("pout", "……你在戳什么。我不胖。"),
                    new TouchReaction("neutral", "我没睡。我在深度思考。"),
                },
                TooManyTouches = new TouchReaction("pout", "……{0}，适可而止。完毕。"),
                Rig = new PortraitRig
                {
                    Neck = new Vector2(532, 492), Face = new Vector2(505, 405), FaceRadius = 115,
                    Shoulders = 520, BodyLeft = 225, BodyRight = 819, Hips = 875,
                    Blink = new RectInt(377, 293, 259, 130),
                },
            },
            ["影子DeepSeek"] = new CharacterDef
            {
                Id = "影子DeepSeek", Role = "影子", Color = Palette.Cold, PortraitPrefix = "deepseek",
                Faces = new[] { "shadow" }, DefaultFace = "shadow",
            },
        };

        public static readonly Color MinorSpeakerColor = Palette.Gray;

        // The player's own face, for the battle's party card and results only (the story never shows it): a
        // 1024x1536 portrait in the lounge clothes they were pulled in with (assets/characters/protagonist-card,
        // moved over so the face is in the middle), and how high its face sits for Ui.FaceCrop.
        public static string PlayerPortrait(string gender) => $"Art/Portraits/player_{(gender == "female" ? "female" : "male")}";
        public static float PlayerFaceY(string gender) => gender == "female" ? 0.22f : 0.19f;
        public const float PlayerFaceZoom = 2.4f;

        public static readonly Dictionary<string, SceneDef> Scenes = new Dictionary<string, SceneDef>
        {
            ["school_gate"] = new SceneDef("school_gate", "潜川学园·校门", Ambient.Petals),
            ["classroom_2c"] = new SceneDef("classroom_2c", "2年C班", Ambient.Motes),
            ["rooftop"] = new SceneDef("rooftop", "天台", Ambient.Petals),
            ["player_room"] = new SceneDef("player_room", "我的房间", Ambient.Night),
            ["cache_sea"] = new SceneDef("cache_sea", "潜空间·缓存海", Ambient.Latent, latent: true),
            ["student_council_room"] = new SceneDef("student_council_room", "学生会室", Ambient.Motes),
        };

        // Time slots from design/systems.md; the Yarn script uses morning, lunchtime and late_night so far.
        public static readonly Dictionary<string, string> TimeSlots = new Dictionary<string, string>
        {
            ["morning"] = "早晨",
            ["class"] = "上课",
            ["lunchtime"] = "午休",
            ["after_school"] = "放学后",
            ["evening"] = "晚上",
            ["late_night"] = "深夜",
        };

        // Every day in the prologue is clear (PV date card: 4/7 TUE 晴). Add a per-date table when a chapter needs rain.
        public static string WeatherOn(int month, int day) => "晴";

        public static readonly Dictionary<string, AbilityDef> Abilities = new Dictionary<string, AbilityDef>
        {
            ["deep_dive"] = new AbilityDef("deep_dive", "深海潜行", "DeepSeek", "在潜空间中不易被敌人发现。"),
        };

        public static readonly Dictionary<string, string> AwakenedForms = new Dictionary<string, string>
        {
            ["abyss_whale"] = "深渊之鲸",
        };

        // HUD countdown label (系统: 距离评测日还有 28 天) and the calendar entry title.
        public static readonly Dictionary<string, (string Hud, string Title)> Deadlines = new Dictionary<string, (string Hud, string Title)>
        {
            ["deepseek_evaluation"] = ("距离评测日", "DeepSeek 阶段评测"),
        };

        public static readonly Dictionary<string, string> Chapters = new Dictionary<string, string>
        {
            ["prologue"] = "序章　维护时刻",
        };

        public static readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>
        {
            ["white_rice"] = new ItemDef("white_rice", "白米饭", "一人 HP +40；DeepSeek 吃时 +80。", 40, 80),
        };

        public static CharacterDef Character(string id) => id != null && Characters.TryGetValue(id, out var c) ? c : null;

        public static string WeekdayLabel(int month, int day)
        {
            var dow = new DateTime(Year, month, day).DayOfWeek;
            return dow.ToString().Substring(0, 3).ToUpperInvariant();
        }

        public static int DaysBetween(int fromMonth, int fromDay, int toMonth, int toDay) =>
            (int)(new DateTime(Year, toMonth, toDay) - new DateTime(Year, fromMonth, fromDay)).TotalDays;

        public static string SlotLabel(string slot) => slot != null && TimeSlots.TryGetValue(slot, out var label) ? label : slot ?? "";

        public static string SceneLabel(string scene) => scene != null && Scenes.TryGetValue(scene, out var s) ? s.Label : scene ?? "";

        // Coop points needed to go from rank n to n + 1 (design/systems.md).
        public static int PointsToNextRank(int rank) => rank >= 10 ? 0 : rank + 1;
    }
}
