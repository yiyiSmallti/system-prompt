using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Showcase
{
    // 一个人物在一镜里依次做的动作：每段从 start 秒（镜内时间）起，按 rate 倍速从 offset 秒播起，
    // 在 fade 秒里盖过前一段（fade 为 0 直接切）。循环的动作绕回，只播一次的播完停在最后一帧。
    sealed class Track
    {
        struct Part
        {
            public string Motion, Face;
            public float Start, Rate, Offset, Fade;
        }

        readonly List<Part> parts = new List<Part>();

        public Track(string motion, string face = "neutral", float rate = 1f, float offset = 0f) => Then(0f, motion, face, rate, offset, 0f);

        public Track Then(float start, string motion, string face = "neutral", float rate = 1f, float offset = 0f, float fade = 0.2f)
        {
            if (parts.Count > 0 && start < parts[parts.Count - 1].Start) throw new ArgumentException("Track 的各段要按时间排");
            parts.Add(new Part { Motion = motion, Face = face, Start = start, Rate = rate, Offset = offset, Fade = fade });
            return this;
        }

        // clock 是这个人物眨眼用的时钟。
        public void Apply(Figure f, float t, float clock)
        {
            var i = Math.Max(0, parts.FindLastIndex(p => p.Start <= t));
            var now = parts[i];
            float Local(Part p) => Mathf.Max(0f, t - p.Start) * p.Rate + p.Offset;
            var w = i == 0 || now.Fade <= 0f ? 1f : Stage.Smooth(0f, 1f, (t - now.Start) / now.Fade);
            if (w >= 1f) f.Pose((now.Motion, Local(now), 1f));
            else f.Pose((now.Motion, Local(now), w), (parts[i - 1].Motion, Local(parts[i - 1]), 1f - w));
            f.Face(w >= 0.5f || i == 0 ? now.Face : parts[i - 1].Face, clock);
        }
    }

    // 分镜。片长约 2 分钟：开场群像，一、角色模型（六个模型逐个全身转台），二、游戏内动作（母作的
    // 行走、跑步和五段战斗动作），三、衍生作的格斗动作（24 段 Mixamo 动作分六页，再加一段对打），四、道具，片尾群像。
    // 生成模型的脸只分到几百个三角形，贴图也糊，镜头都停在远处（Far），不推近。
    static class Shots
    {
        static Vector3 Up(float y) => new Vector3(0f, y, 0f);

        // 人物镜头的机位离看点的距离都乘上它，人在画面里只占三分之一上下，看动作和轮廓，不看细节。
        const float Far = 1.6f;

        static void Point(Camera c, Vector3 position, Vector3 target, float fov, float far = 1f)
        {
            position = target + (position - target) * far;
            // 仰拍的机位拉远了会沉到地面以下。
            position.y = Mathf.Max(position.y, 0.3f);
            c.transform.position = position;
            c.transform.LookAt(target);
            c.fieldOfView = fov;
        }

        public static List<Shot> All(Cast cast)
        {
            var shots = new List<Shot> { new Lineup(cast, finale: false) };
            for (var i = 0; i < Cast.Models.Length; i++) shots.Add(new Turntable(cast, Cast.Models[i], i == 0));
            shots.Add(new WalkRun(cast));
            shots.AddRange(Trio.Battle(cast));
            shots.AddRange(Page.All(cast));
            shots.Add(new Fight(cast));
            shots.AddRange(PropPage.All(cast));
            shots.Add(new Lineup(cast, finale: true));
            return shots;
        }

        static void Section(Shot shot, string no, string zh, string en, float end = 2.2f) =>
            shot.Notes.Add(new Note { Kind = "section", Start = 0f, End = end, Fields = { ["no"] = no, ["zh"] = zh, ["en"] = en } });

        // 六个人一字排开，各做自己的待机；开场从中间四人拉远到全员，片尾换成欢呼。
        sealed class Lineup : Shot
        {
            static readonly string[] Order = { "PlayerMale", "PlayerFemale", "DeepSeekCasual", "DeepSeek", "PlayerFemaleBattle", "PlayerMaleBattle" };
            const float Gap = 1.3f;
            readonly Cast cast;
            readonly bool finale;
            readonly Track[] tracks;
            Stage stage;

            public Lineup(Cast cast, bool finale) : base(finale ? "finale" : "open", finale ? 7.5f : 8f)
            {
                this.cast = cast;
                this.finale = finale;
                tracks = Order.Select((m, i) =>
                {
                    var f = cast[m].Figure;
                    if (!finale) return new Track(Figure.Own, "neutral", 1f, 1.7f * i);
                    // 片尾：战斗服的人先摆胜利动作再欢呼，其余一直欢呼，都笑着。
                    var victory = 0.3f + 0.15f * i;
                    return m.EndsWith("Battle") || m == "DeepSeek"
                        ? new Track(Figure.Own, "smile", 1f, 1.7f * i).Then(victory, "Victory", "smile", 1f, 0f, 0.35f)
                            .Then(victory + f.Length("Victory") - 0.4f, "Cheer", "smile", 1f, 0.8f * i, 0.4f)
                        : new Track("Cheer", "smile", 1f, 1.1f * i);
                }).ToArray();
                if (finale)
                {
                    Note("credits", 2.6f, 7.5f);
                    return;
                }
                Note("title", 0.5f, 5.4f);
                for (var i = 0; i < Order.Length; i++)
                {
                    var e = cast[Order[i]];
                    var x = (2.5f - i) * Gap;
                    Note("name", 5.2f, 8f, ("zh", e.Name), ("sub", e.Outfit)).At(7.9f, () => new Vector3(x, 0f, 0.55f));
                }
            }

            public override void Begin(Stage stage)
            {
                this.stage = stage;
                cast.Only(Order.Select(m => cast[m].Figure).ToArray());
                foreach (var m in Order) cast[m].Figure.Rim(Palette.Studio.Rim);
            }

            public override void Frame(float t, Camera camera)
            {
                stage.Use(Palette.Studio, Vector3.zero, 26f, t * 0.04f);
                for (var i = 0; i < Order.Length; i++)
                {
                    var f = cast[Order[i]].Figure;
                    var at = new Vector3((2.5f - i) * Gap, 0f, 0f);
                    f.Root.SetPositionAndRotation(at, Quaternion.Euler(0f, (2.5f - i) * -4f, 0f));
                    tracks[i].Apply(f, t, t + 0.4f * i);
                    f.Sway(t);
                    f.Commit();
                    stage.Ring(i, at, 0.46f);
                    stage.Shadow(i, f);
                }
                if (!finale)
                {
                    var k = Stage.Ease(t / 7.6f);
                    Point(camera, Vector3.Lerp(new Vector3(-0.2f, 1.0f, 7.6f), new Vector3(0f, 1.3f, 9.4f), k),
                        Vector3.Lerp(Up(1.0f), Up(0.9f), k), Mathf.Lerp(32f, 30f, k));
                }
                else
                {
                    var k = Stage.Ease(t / 7.5f);
                    Point(camera, Vector3.Lerp(new Vector3(0.3f, 0.8f, 8.4f), new Vector3(-0.3f, 1.5f, 9.9f), k), Vector3.Lerp(Up(1.0f), Up(1.05f), k), 30f);
                }
            }
        }

        // 一个模型的转台：全身转一整圈。
        sealed class Turntable : Shot
        {
            const float Spin = 6.0f;
            readonly Cast cast;
            readonly Cast.Entry e;
            Stage stage;

            public Turntable(Cast cast, Cast.Entry e, bool first) : base("model-" + e.Model, 6.4f)
            {
                this.cast = cast;
                this.e = e;
                if (first) Section(this, "01", "角色模型", "CHARACTER MODELS");
                Note("card", first ? 1.4f : 0.25f, 6.2f, ("name", e.Name), ("outfit", e.Outfit), ("note", e.Note),
                    ("model", e.Model + ".fbx"), ("height", $"{e.Height:0.00} m"), ("look", !Figure.Toon ? "Lit · 写实光照" : Figure.Expressions ? "Toon · 表情 3×3" : "Toon · 三渲二"));
            }

            public override void Begin(Stage stage)
            {
                this.stage = stage;
                cast.Only(e.Figure);
                e.Figure.Rim(e.Palette.Rim);
            }

            public override void Frame(float t, Camera camera)
            {
                var f = e.Figure;
                stage.Use(e.Palette, Vector3.zero, 18f, t * 0.05f);
                stage.Ring(0, Vector3.zero, 0.52f);
                f.Root.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 360f * Stage.Ease(t / Spin), 0f));
                new Track(Figure.Own, "neutral", 1f, 2.1f).Apply(f, t, t + 0.2f);
                f.Sway(t);
                f.Commit();
                stage.Shadow(0, f);

                // 机位和看点一起往右挪，人站在画面右边，左边留给资料卡；挪多少跟着机位远近，人在画面里的位置不变。
                var h = e.Height;
                var d = 3.05f * h * Far;
                var side = 0.145f * d;
                Point(camera, new Vector3(side, h * 0.53f + 0.03f * Mathf.Sin(t * 0.7f), d), new Vector3(side, h * 0.49f, 0f), 27f);
            }
        }

        // 探索时的移动：三个人原地走、再跑起来，地上的网格往后退。
        sealed class WalkRun : Shot
        {
            static readonly string[] Who = { "PlayerMale", "DeepSeekCasual", "PlayerFemale" };
            const float Yaw = 52f, RunFrom = 4.2f, Blend = 0.7f, WalkSpeed = 1.6f, RunSpeed = 6.2f;
            readonly Cast cast;
            Stage stage;

            public WalkRun(Cast cast) : base("walk-run", 9.2f)
            {
                this.cast = cast;
                Section(this, "02", "游戏内动作", "IN-GAME MOTIONS");
                var walker = cast[Who[0]].Figure;
                Note("motion", 1.2f, RunFrom, ("zh", "行走"), ("en", "Walk"), ("meta", $"{walker.Length("Walk"):0.00} s · 循环 · 探索"));
                Note("motion", RunFrom + 0.3f, 9.2f, ("zh", "跑步"), ("en", "Run"),
                    ("meta", $"{walker.Length("Run"):0.00} s · 循环 · 按速度和行走混合"));
            }

            public override void Begin(Stage stage)
            {
                this.stage = stage;
                cast.Only(Who.Select(m => cast[m].Figure).ToArray());
                foreach (var m in Who) cast[m].Figure.Rim(Palette.Sky.Rim);
            }

            static float Speed(float t) => Mathf.Lerp(WalkSpeed, RunSpeed, Stage.Smooth(RunFrom, RunFrom + Blend, t));

            public override void Frame(float t, Camera camera)
            {
                stage.Use(Palette.Sky, Vector3.zero, 24f, t * 0.05f);
                var travelled = 0f;
                const int steps = 240;
                for (var s = 0; s < steps; s++) travelled += Speed((s + 0.5f) * t / steps) * t / steps;
                var heading = Quaternion.Euler(0f, Yaw, 0f);
                var ahead = heading * Vector3.forward;
                stage.Scroll(new Vector2(ahead.x, ahead.z) * travelled);
                for (var i = 0; i < Who.Length; i++)
                {
                    var f = cast[Who[i]].Figure;
                    var run = Stage.Smooth(RunFrom, RunFrom + Blend, t);
                    f.Root.SetPositionAndRotation(new Vector3((1 - i) * 1.9f, 0f, (i - 1) * 0.5f) - heading * f.Drift("Run") * run, heading);
                    var walkPace = Mathf.Clamp(Speed(t) / WalkSpeed, 0.7f, 1.5f);
                    f.Pose(("Walk", t * walkPace + 0.37f * i, 1f - run), ("Run", t * 1.05f + 0.29f * i, run));
                    f.Face("neutral", t + 0.5f * i);
                    f.Commit();
                    stage.Shadow(i, f);
                }
                var k = Stage.Ease(t / 9.2f);
                Point(camera, Vector3.Lerp(new Vector3(1.2f, 1.1f, 6.6f), new Vector3(-1.4f, 0.85f, 5.9f), k), Up(0.85f), 31f, Far);
            }
        }

        // 三个战斗服的人一起做母作的战斗动作（游戏里由 Dungeon/BattleStage.cs 播），镜头各镜不同。
        sealed class Trio : Shot
        {
            static readonly string[] Who = { "PlayerMaleBattle", "DeepSeek", "PlayerFemaleBattle" };
            readonly Cast cast;
            readonly Palette palette;
            readonly Func<int, Figure, Track> track;
            readonly Func<float, (Vector3 at, Vector3 look, float fov)> lens;
            Track[] tracks;
            Stage stage;

            Trio(Cast cast, string id, float duration, Palette palette, Func<int, Figure, Track> track, Func<float, (Vector3, Vector3, float)> lens) : base(id, duration)
            {
                this.cast = cast;
                this.palette = palette;
                this.track = track;
                this.lens = lens;
            }

            public static IEnumerable<Shot> Battle(Cast cast)
            {
                // 战斗待机打底，at 秒淡入一段动作，播完（或播到 upTo 秒）淡回待机。
                Func<int, Figure, Track> Strike(string motion, float at, string face = "neutral", string after = "neutral", float upTo = 0f) => (i, f) =>
                    new Track("BattleStance", "neutral", 1f, 0.9f * i)
                        .Then(at + 0.06f * i, motion, face, 1f, 0f, 0.16f)
                        .Then(at + 0.06f * i + (upTo > 0f ? upTo : f.Length(motion)) - 0.18f, "BattleStance", after, 1f, 1.3f, 0.3f);
                Trio Make(string id, string motion, string zh, float duration, Palette p, Func<int, Figure, Track> t, Func<float, (Vector3, Vector3, float)> lens, string meta)
                {
                    var shot = new Trio(cast, id, duration, p, t, lens);
                    shot.Note("motion", 0.25f, duration, ("zh", zh), ("en", motion), ("meta", meta));
                    return shot;
                }
                yield return Make("battle-stance", "BattleStance", "战斗待机", 4.4f, Palette.Sea, (i, f) => new Track("BattleStance", "neutral", 1f, 1.3f * i),
                    t => (Vector3.Lerp(new Vector3(2.8f, 0.45f, 4.6f), new Vector3(-2.2f, 0.5f, 4.9f), Stage.Ease(t / 4.4f)), Up(1.0f), 32f), "4.92 s · 循环 · 进入战斗后的待机");
                // 斩击下劈之后有将近一秒弯腰低头，三个人一起弯着很怪，播到下劈为止。
                yield return Make("battle-slash", "Slash", "斩击", 2.9f, Palette.Studio, Strike("Slash", 0.35f, upTo: 1.15f),
                    t => (Vector3.Lerp(new Vector3(1.2f, 0.7f, 5.8f), new Vector3(0.4f, 0.8f, 5.1f), Stage.Ease(t / 2.9f)), Up(0.92f), 32f), "前 1.15 s · DeepSeek 冲到目标跟前挥刀");
                yield return Make("battle-cast", "Cast", "施法", 4.4f, Palette.Sea, Strike("Cast", 0.3f),
                    t => (Vector3.Lerp(new Vector3(0f, 0.35f, 5.9f), new Vector3(0f, 0.55f, 5.2f), Stage.Ease(t / 4.4f)), Up(1.1f), 33f), "3.60 s · 抬手施法，冰枪齐射");
                yield return Make("battle-hit", "Hit", "受击", 2.5f, Palette.Studio, Strike("Hit", 0.45f, "hurt"),
                    t => (Vector3.Lerp(new Vector3(1.9f, 1.0f, 5.3f), new Vector3(1.5f, 1.0f, 4.9f), Stage.Ease(t / 2.5f)), Up(0.98f), 31f), "1.25 s · 被打中的反应");
                yield return Make("battle-cheer", "Cheer", "欢呼", 4.2f, Palette.Terminal, (i, f) => new Track("BattleStance", "neutral", 1f, 0.9f * i).Then(0.35f + 0.1f * i, "Cheer", "smile", 1f, 0.5f * i, 0.3f),
                    t => (Vector3.Lerp(new Vector3(-1.4f, 0.9f, 5.0f), new Vector3(1.2f, 1.1f, 5.3f), Stage.Ease(t / 4.2f)), Up(1.05f), 31f), "12.04 s · 循环 · 胜利时站着的队员欢呼");
            }

            public override void Begin(Stage stage)
            {
                this.stage = stage;
                var figures = Who.Select(m => cast[m].Figure).ToArray();
                cast.Only(figures);
                tracks = figures.Select((f, i) => track(i, f)).ToArray();
                foreach (var f in figures) f.Rim(palette.Rim);
            }

            public override void Frame(float t, Camera camera)
            {
                stage.Use(palette, Vector3.zero, 22f, t * 0.05f);
                for (var i = 0; i < Who.Length; i++)
                {
                    var f = cast[Who[i]].Figure;
                    var at = new Vector3((1 - i) * 1.7f, 0f, i == 1 ? -0.3f : 0f);
                    f.Root.SetPositionAndRotation(at, Quaternion.Euler(0f, (1 - i) * -14f, 0f));
                    tracks[i].Apply(f, t, t + 0.6f * i);
                    f.Sway(t);
                    f.Commit();
                    stage.Ring(i, at, 0.5f);
                    stage.Shadow(i, f);
                }
                var (position, look, fov) = lens(t);
                Point(camera, position, look, fov, Far);
            }
        }

        // 衍生作的 Mixamo 动作：画面分成 2 × 3 格，六个模型各占一格做一段，一页六段。
        // 六个人在世界里相隔很远，每格的镜头只拍到自己那个人；有位移的动作镜头跟一半。
        sealed class Page : Shot
        {
            struct Slot
            {
                public string Model, Motion, Zh, Face, Lead;
                public float Rate;
            }

            // 有位移的动作（走位、翻滚、倒地、起身）只播一次、停在最后；其余播完淡回架势，有空再播一遍。
            static readonly string[] Travels = { "StepForward", "StepBackward", "FlipBackwards", "KnockedOut", "GettingUp" };
            // 往上蹿的动作，镜头也跟着抬，不然人飞出格子。
            static readonly string[] Leaps = { "JumpUp", "FlipBackwards" };
            const float Apart = 14f, Yaw = 24f;
            readonly Cast cast;
            readonly Slot[] slots;
            Track[] tracks;
            float[] yaws;
            Stage stage;

            Page(Cast cast, string id, float duration, string zh, string en, Slot[] slots, bool first) : base(id, duration)
            {
                this.cast = cast;
                this.slots = slots;
                if (first) Section(this, "03", "衍生作的格斗动作", "FIGHTING MOTIONS · MIXAMO");
                Note("page", first ? 1.6f : 0.2f, duration, ("zh", zh), ("en", en), ("sub", "来自衍生作《System Prompt：无敌究极后桥背摔》"));
                for (var i = 0; i < slots.Length; i++)
                {
                    var s = slots[i];
                    var e = cast[s.Model];
                    var length = e.Figure.Length(s.Motion);
                    var meta = $"{length:0.00} s · {(e.Figure.Loops(s.Motion) ? "循环" : "一次")}" + (Math.Abs(s.Rate - 1f) > 0.01f ? $" · ×{s.Rate:0.#}" : "");
                    Note("cell", 0.3f, duration, ("cell", i), ("zh", s.Zh), ("en", s.Motion), ("meta", meta), ("who", e.Name + " · " + e.Outfit));
                }
            }

            static Slot S(string model, string motion, string zh, string face = "neutral", float rate = 1f, string lead = "BouncingFightIdle") =>
                new Slot { Model = model, Motion = motion, Zh = zh, Face = face, Rate = rate, Lead = lead };

            public static IEnumerable<Shot> All(Cast cast)
            {
                yield return new Page(cast, "mixamo-idle", 5.4f, "待机与行走", "IDLES & WALKS", new[]
                {
                    S("PlayerMaleBattle", "BouncingFightIdle", "格斗架势（弹跳）"), S("DeepSeek", "MmaIdle", "格斗架势（MMA）"),
                    S("PlayerMale", "DrunkIdle", "醉步待机", "sleepy"), S("DeepSeekCasual", "FemaleIdle", "站立待机"),
                    S("PlayerFemale", "FemaleWalk", "原地行走"), S("PlayerFemaleBattle", "SadWalk", "低落行走", "concerned"),
                }, true);
                yield return new Page(cast, "mixamo-strike", 4.2f, "步法与出拳", "STEPS & STRIKES", new[]
                {
                    S("PlayerFemaleBattle", "StepForward", "前垫步"), S("PlayerMaleBattle", "StepBackward", "后撤步"),
                    S("DeepSeek", "LeadJab", "前手刺拳"), S("PlayerMale", "PunchCombo", "组合拳"),
                    S("PlayerFemale", "Uppercut", "上勾拳"), S("DeepSeekCasual", "Block", "格挡", "concerned"),
                }, false);
                yield return new Page(cast, "mixamo-kick", 4.4f, "踢击、跳跃与受击", "KICKS, JUMPS & HITS", new[]
                {
                    S("PlayerFemaleBattle", "MmaKick", "正踢"), S("DeepSeek", "RoundhouseKick", "回旋踢"),
                    S("PlayerMale", "JumpUp", "起跳", "smile", 1f, Figure.Own), S("PlayerMaleBattle", "FlipBackwards", "后空翻"),
                    S("PlayerFemale", "LightHit", "轻受击", "hurt"), S("DeepSeekCasual", "BigHit", "重受击", "hurt"),
                }, false);
                yield return new Page(cast, "mixamo-emote", 6.6f, "倒地与表演", "KNOCKDOWN & EMOTES", new[]
                {
                    S("PlayerFemaleBattle", "KnockedOut", "被击倒", "down"), S("DeepSeek", "GettingUp", "倒地起身", "concerned", 1.4f),
                    S("PlayerMaleBattle", "Victory", "胜利", "smile", 1f, Figure.Own), S("PlayerFemale", "Yawn", "打哈欠", "sleepy", 1.4f, Figure.Own),
                    S("PlayerMale", "Texting", "看手机"), S("DeepSeekCasual", "SpinInPlace", "原地转圈", "smile"),
                }, false);
            }

            Track Build(Slot s, Figure f, int i)
            {
                if (f.Loops(s.Motion)) return new Track(s.Motion, s.Face, s.Rate, 0.7f * i);
                var travels = Travels.Contains(s.Motion);
                // 起身从躺着的第一帧开始，没有架势可以接。
                var track = s.Motion == "GettingUp" ? new Track(s.Motion, s.Face, 0f) : new Track(s.Lead, "neutral", 1f, 0.6f * i);
                var length = f.Length(s.Motion) / s.Rate;
                var at = 0.45f + 0.06f * i;
                while (true)
                {
                    track.Then(at, s.Motion, s.Face, s.Rate, 0f, s.Motion == "GettingUp" ? 0f : 0.2f);
                    if (travels) break;
                    var back = at + length - 0.15f;
                    if (back + 0.8f + length > Duration) break;
                    track.Then(back, s.Lead, "neutral", 1f, 0.3f, 0.3f);
                    at = back + 0.8f;
                }
                return track;
            }

            Vector3 Home(int i) => new Vector3(i * Apart, 0f, 0f);

            public override int Cells => 6;

            // 从左上起逐行（camera.rect 的原点在左下）。
            public override Rect Cell(int i) => new Rect(i % 3 / 3f, 0.5f - i / 3 * 0.5f, 1f / 3f, 0.5f);

            public override void Begin(Stage stage)
            {
                this.stage = stage;
                var figures = slots.Select(s => cast[s.Model].Figure).ToArray();
                if (figures.Distinct().Count() != figures.Length) throw new InvalidOperationException($"{Id} 一页里有重复的模型");
                cast.Only(figures);
                tracks = slots.Select((s, i) => Build(s, cast[s.Model].Figure, i)).ToArray();
                yaws = slots.Select(s => Yaw - cast[s.Model].Figure.Turn(s.Motion)).ToArray();
                foreach (var f in figures) f.Rim(Palette.Arena.Rim);
            }

            public override void Frame(float t, Camera camera)
            {
                stage.Use(Palette.Arena, Vector3.zero, 20f, t * 0.06f);
                for (var i = 0; i < slots.Length; i++)
                {
                    var f = cast[slots[i].Model].Figure;
                    f.Root.SetPositionAndRotation(Home(i), Quaternion.Euler(0f, yaws[i], 0f));
                    tracks[i].Apply(f, t, t + 0.5f * i);
                    f.Sway(t);
                    f.Commit();
                    stage.Ring(i, Home(i), 0.5f);
                    stage.Shadow(i, f);
                }
            }

            public override void Aim(int cell, float t, Camera camera)
            {
                var f = cast[slots[cell].Model].Figure;
                var home = Home(cell);
                var follow = Vector3.zero;
                var motion = slots[cell].Motion;
                if (Travels.Contains(motion))
                {
                    follow = (f.Hips - home) * 0.8f;
                    follow.y = 0f;
                }
                if (Leaps.Contains(motion)) follow.y = Mathf.Max(0f, f.Hips.y - 0.53f * f.Height) * 0.8f;
                stage.Centre(home + follow);
                var look = home + follow + Up(0.84f);
                Point(camera, look + new Vector3(0.12f * Mathf.Sin(t * 0.5f + cell), 0.28f, 4.15f - 0.08f * t), look, 30f, Far);
            }
        }

        // 对打：DeepSeek 对陪伴生（男），把几段格斗动作接起来。
        sealed class Fight : Shot
        {
            readonly Cast cast;
            Track left, right;
            Stage stage;
            Figure a, b;
            const float Apart = 1.35f;

            public Fight(Cast cast) : base("mixamo-fight", 10.4f)
            {
                this.cast = cast;
                Note("page", 0.2f, 10.4f, ("zh", "接起来"), ("en", "COMBO"), ("sub", "DeepSeek 对 陪伴生（男）"));
                (float at, string text, bool onLeft)[] calls =
                {
                    (0.55f, "前手刺拳", true), (1.0f, "轻受击", false), (2.0f, "组合拳", false), (2.15f, "格挡", true),
                    (4.35f, "回旋踢", true), (5.35f, "被击倒", false), (7.0f, "胜利", true),
                };
                foreach (var (at, text, onLeft) in calls)
                    Note("callout", at, at + 1.25f, ("text", text), ("side", onLeft ? "left" : "right"))
                        .At(at, () => (onLeft ? a : b).HeadTop + Vector3.up * 0.12f);
            }

            public override void Begin(Stage stage)
            {
                this.stage = stage;
                a = cast["DeepSeek"].Figure;
                b = cast["PlayerMaleBattle"].Figure;
                cast.Only(a, b);
                a.Rim(Palette.Arena.Rim);
                b.Rim(Palette.Arena.Rim);
                left = new Track("BouncingFightIdle", "neutral")
                    .Then(0.5f, "LeadJab")
                    .Then(1.85f, "BouncingFightIdle", "neutral", 1f, 0.4f, 0.25f)
                    .Then(2.1f, "Block", "concerned", 1f, 0f, 0.15f)
                    .Then(3.75f, "BouncingFightIdle", "neutral", 1f, 0.8f, 0.3f)
                    .Then(4.3f, "RoundhouseKick", "shout", 1f, 0f, 0.2f)
                    .Then(6.6f, "BouncingFightIdle", "smile", 1f, 0.2f, 0.3f)
                    .Then(7.0f, "Victory", "smile", 1f, 0f, 0.3f);
                right = new Track("BouncingFightIdle", "neutral", 1f, 1.1f)
                    .Then(0.95f, "LightHit", "hurt", 1f, 0f, 0.1f)
                    .Then(1.7f, "BouncingFightIdle", "neutral", 1f, 0.3f, 0.25f)
                    .Then(1.95f, "PunchCombo", "shout", 1f, 0f, 0.2f)
                    .Then(4.05f, "BouncingFightIdle", "neutral", 1f, 0.5f, 0.25f)
                    .Then(5.3f, "KnockedOut", "hurt", 1f, 0f, 0.12f);
            }

            public override void Frame(float t, Camera camera)
            {
                stage.Use(Palette.Arena, Vector3.zero, 24f, t * 0.06f);
                // 回旋踢转身时鲸尾扫进对手身体里，踢的那一秒往后让开一点。
                var room = 0.35f * Stage.Smooth(4.1f, 4.4f, t) * (1f - Stage.Smooth(5.4f, 5.8f, t));
                a.Root.SetPositionAndRotation(new Vector3(Apart / 2f + room, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
                b.Root.SetPositionAndRotation(new Vector3(-Apart / 2f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                left.Apply(a, t, t);
                right.Apply(b, t, t + 0.7f);
                if (t > 6.4f) b.Face("down", -1f);
                a.Sway(t, 1.4f);
                a.Commit();
                b.Commit();
                stage.Ring(0, new Vector3(0f, 0f, 0f), 2.2f);
                stage.Shadow(0, a);
                stage.Shadow(1, b);
                // 侧面略低，跟着动作慢慢推近，击倒之后绕到正面。
                var k = Stage.Ease(t / 5.4f);
                var at = Vector3.Lerp(new Vector3(0.8f, 0.9f, 5.6f), new Vector3(0.3f, 0.8f, 5.0f), k);
                var j = Stage.Ease((t - 5.6f) / 4.6f);
                at = Vector3.Lerp(at, new Vector3(-0.3f, 1.2f, 5.3f), j);
                Point(camera, at, Vector3.Lerp(Up(0.95f), new Vector3(-0.45f, 0.6f, -0.3f), j), Mathf.Lerp(31f, 34f, j), Far);
            }
        }

        // Tripo 生成的潜空间道具，一页四个在圆台上慢慢转。
        sealed class PropPage : Shot
        {
            readonly Cast cast;
            readonly (string name, string zh, float size, float x)[] props;
            readonly GameObject[] made;
            Stage stage;

            PropPage(Cast cast, string id, float duration, (string, string, float, float)[] props, bool first) : base(id, duration)
            {
                this.cast = cast;
                this.props = props;
                made = new GameObject[props.Length];
                if (first) Section(this, "04", "场景道具", "PROPS");
                Note("page", first ? 1.6f : 0.2f, duration, ("zh", "潜空间道具"), ("en", "PROPS · TRIPO"), ("sub", "缓存海走廊里的残骸与集成里的光之鲸"));
                foreach (var (name, zh, size, x) in props)
                    Note("name", 0.35f, duration, ("zh", zh), ("sub", name)).At(0f, () => new Vector3(-x, 0f, 0.95f));
            }

            public static IEnumerable<Shot> All(Cast cast)
            {
                yield return new PropPage(cast, "props-a", 4.8f, new[]
                {
                    ("FallenRack", "倒下的机柜", 1.9f, -3.3f), ("CableHeap", "线缆堆", 1.5f, -1.1f),
                    ("CableDrum", "线缆盘", 1.2f, 1.1f), ("BoardCoral", "电路板珊瑚", 1.5f, 3.3f),
                }, true);
                yield return new PropPage(cast, "props-b", 4.8f, new[]
                {
                    ("OldMonitor", "旧显示器", 1.0f, -3.5f), ("DrivePile", "硬盘堆", 1.2f, -1.55f),
                    ("DataCrystals", "数据晶簇", 1.4f, 0.4f), ("Whale", "鲸鱼", 2.6f, 2.9f),
                }, false);
            }

            public override void Begin(Stage stage)
            {
                this.stage = stage;
                cast.Only();
                for (var i = 0; i < props.Length; i++)
                {
                    if (made[i] == null) made[i] = stage.Prop(props[i].name, props[i].size);
                    made[i].SetActive(true);
                }
            }

            public override void Frame(float t, Camera camera)
            {
                stage.Use(Palette.Studio, Vector3.zero, 24f, t * 0.05f);
                Shader.SetGlobalVector("_SeaLightDir", (camera.transform.right * 0.5f + Vector3.up * 0.9f - camera.transform.forward * 0.6f).normalized);
                for (var i = 0; i < props.Length; i++)
                {
                    var (_, _, size, x) = props[i];
                    var at = new Vector3(-x, 0f, 0f);
                    made[i].transform.SetPositionAndRotation(at, Quaternion.Euler(0f, -30f + 26f * t + 47f * i, 0f));
                    stage.Ring(i, at, Mathf.Min(0.9f, size * 0.5f));
                    stage.Shadow(i, at, size * 0.4f, 0.55f);
                }
                Point(camera, new Vector3(0.2f * Mathf.Sin(t * 0.6f), 1.65f, 9.2f - 0.15f * t), Up(0.7f), 30f);
            }
        }
    }
}
