using System;
using System.Collections.Generic;
using SystemPrompt.Battle;
using SystemPrompt.Dungeon;
using SystemPrompt.Story;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // <<dungeon>>: one latent-space area played in 3D (Dungeon/DungeonWorld.cs) under a P5-style HUD: the
    // area card with the zone the player is in (a banner the first time), the objective, the corner map,
    // the maintenance meter, the party, prompts and alert marks over the world, and a text box for the
    // story nodes the area runs (zones, fragments, the terminal...). Noise bodies are fought, JRPG style:
    // hitting one with Enter before it has seen the party gives a free round, one that chased the party
    // down gets the first move, and running into one otherwise is a plain battle. Enter also pushes cache
    // blocks and turns light plates (Dungeon/SeaPuzzles.cs).
    public sealed class DungeonScreen : IInputLayer, ILinePresenter
    {
        enum Mode { Off, Explore, Talk, Busy }

        // Ends one run; every await checks the run it started with, so a run abandoned by loading a save
        // can never act on the next one.
        sealed class Session { public bool Over; }

        const float MouseDegreesPerPixel = 0.12f;
        const float AttackReach = 2.6f;
        const float MetresPerPercent = 6f;
        const float BattlePercent = 6f;

        public readonly VisualElement Root;
        readonly GameRoot game;
        readonly Label areaName, areaLayer, areaTag, areaZone, objective;
        readonly VisualElement banner;
        readonly Label bannerName, bannerTag;
        readonly Minimap minimap;
        readonly VisualElement meterBox, meterFill;
        readonly Label meterText;
        readonly VisualElement partyRow;
        readonly VisualElement keys;
        readonly VisualElement marks;
        readonly VisualElement prompt;
        readonly Label promptVerb;
        readonly VisualElement talkBox, talkPlate;
        readonly Label talkName, talkText;
        readonly VisualElement fx, flash;
        readonly Dictionary<SeaNoise, Label> alertMarks = new Dictionary<SeaNoise, Label>();
        readonly Dictionary<SeaNoise, Label> phraseMarks = new Dictionary<SeaNoise, Label>();

        Session session = new Session { Over = true };
        AwaitableCompletionSource done;
        DungeonWorld world;
        DungeonDef def;
        StoryController story;
        Mode mode = Mode.Off;
        bool shown;
        bool advance;
        float meter;
        float travelledAtLastTick;
        bool spottedTold, firewallTold, beaconTold, ambushTold, warned;
        ZoneDef zone;
        readonly HashSet<ZoneDef> visited = new HashSet<ZoneDef>();
        int bannerShown;

        // What the end-to-end run checks.
        public bool Exploring => mode == Mode.Explore && world != null;
        public bool Talking => mode == Mode.Talk;
        public DungeonWorld World => world;
        public float Meter => meter;
        public int FragmentsRead { get; private set; }
        public int ChestsOpened { get; private set; }
        public int Battles { get; private set; }
        public int Preemptive { get; private set; }
        public int Ambushed { get; private set; }
        public int Pushes { get; private set; }
        public int Turns { get; private set; }
        public ZoneDef Zone => zone;
        public int ZonesVisited => visited.Count;

        public DungeonScreen(GameRoot game, VisualElement parent)
        {
            this.game = game;
            Root = Ui.El("sp-dungeon", parent);
            Ui.Fill(Root);
            Root.pickingMode = PickingMode.Ignore;

            marks = Ui.El("sp-dungeon__marks", Root);
            Ui.Fill(marks);
            marks.pickingMode = PickingMode.Ignore;

            var area = Ui.El("sp-dungeon__area", Root);
            Shape.Backdrop(area, Shape.Slant(26), Palette.Ink.WithAlpha(0.92f)).Stroke(Palette.Sea, 3).Shadow(new Vector2(10, 10), Palette.Sea.WithAlpha(0.8f));
            areaTag = Ui.Text("LATENT SPACE", "sp-dungeon__area-tag", area, FontKind.MonoBold);
            var nameRow = Ui.El("sp-dungeon__area-row", area);
            areaName = Ui.Text("", "sp-dungeon__area-name", nameRow, FontKind.Black);
            areaLayer = Ui.Text("", "sp-dungeon__area-layer", nameRow, FontKind.Black);
            areaZone = Ui.Text("", "sp-dungeon__area-zone", area, FontKind.Bold);

            // A new zone slams its name across the left of the screen.
            banner = Ui.El("sp-dungeon__zone", Root);
            Shape.Backdrop(banner, Shape.Slant(30), Palette.Ink.WithAlpha(0.94f)).Stroke(Palette.Green, 3).Shadow(new Vector2(12, 12), Palette.Sea.WithAlpha(0.85f));
            bannerTag = Ui.Text("", "sp-dungeon__zone-tag", banner, FontKind.MonoBold);
            bannerName = Ui.Text("", "sp-dungeon__zone-name", banner, FontKind.Black);
            Ui.Show(banner, false);
            var goal = Ui.El("sp-dungeon__goal", Root);
            var goalTag = Ui.El("sp-dungeon__goal-tag", goal);
            Shape.Backdrop(goalTag, Shape.Slant(10), Palette.Green);
            Ui.Text("目标", "sp-dungeon__goal-tag-text", goalTag, FontKind.Black);
            var goalBody = Ui.El("sp-dungeon__goal-body", goal);
            Shape.Backdrop(goalBody, Shape.Slant(14), Palette.Paper).Shadow(new Vector2(6, 6), Palette.Ink.WithAlpha(0.7f));
            objective = Ui.Text("", "sp-dungeon__goal-text", goalBody, FontKind.Bold);

            var mapBox = Ui.El("sp-dungeon__map", Root);
            Shape.Backdrop(mapBox, Shape.Crooked(12), Palette.Ink.WithAlpha(0.82f)).Stroke(Palette.Paper, 3).Shadow(new Vector2(8, 8), Palette.Sea.WithAlpha(0.7f));
            minimap = new Minimap();
            minimap.AddToClassList("sp-dungeon__map-view");
            mapBox.Add(minimap);
            Ui.Text("N", "sp-dungeon__map-north", mapBox, FontKind.MonoBold);

            meterBox = Ui.El("sp-dungeon__meter", Root);
            Shape.Backdrop(meterBox, Shape.Slant(14), Palette.Ink.WithAlpha(0.92f)).Stroke(Palette.Sea, 2);
            var meterHead = Ui.El("sp-dungeon__meter-head", meterBox);
            Ui.Text("维护进度", "sp-dungeon__meter-label", meterHead, FontKind.Black);
            meterText = Ui.Text("", "sp-dungeon__meter-value", meterHead, FontKind.MonoBold);
            var track = Ui.El("sp-dungeon__meter-track", meterBox);
            meterFill = Ui.El("sp-dungeon__meter-fill", track);

            partyRow = Ui.El("sp-dungeon__party", Root);

            keys = Ui.El("sp-dungeon__keys", Root);
            Ui.Text("WASD 移动　Shift 冲刺　Q/E·鼠标 视角　Enter 调查/攻击　Esc 菜单", "sp-dungeon__keys-text", keys, FontKind.MonoBold);

            prompt = Ui.El("sp-dungeon__prompt", Root);
            Shape.Backdrop(prompt, Shape.Slant(12), Palette.Ink).Stroke(Palette.Paper, 2).Shadow(new Vector2(6, 6), Palette.Green);
            var cap = Ui.El("sp-dungeon__prompt-key", prompt);
            Shape.Backdrop(cap, Shape.Slant(5), Palette.Paper);
            Ui.Text("Enter", "sp-dungeon__prompt-key-text", cap, FontKind.MonoBold);
            promptVerb = Ui.Text("", "sp-dungeon__prompt-verb", prompt, FontKind.Black);

            talkBox = Ui.El("sp-dungeon__talk", Root);
            Shape.Backdrop(talkBox, Shape.Crooked(12), Palette.Ink.WithAlpha(0.94f)).Stroke(Palette.Paper, 3).Shadow(new Vector2(12, 12), Palette.Sea);
            talkPlate = Ui.El("sp-dungeon__talk-plate", talkBox);
            Shape.Backdrop(talkPlate, Shape.Slant(14), Palette.Paper);
            talkName = Ui.Text("", "sp-dungeon__talk-name", talkPlate, FontKind.Black);
            talkText = Ui.Text("", "sp-dungeon__talk-text", talkBox, FontKind.Regular);

            fx = Ui.El("sp-dungeon__fx", Root);
            Ui.Fill(fx);
            fx.pickingMode = PickingMode.Ignore;
            flash = Ui.El("sp-dungeon__flash", Root);
            Ui.Fill(flash);
            flash.pickingMode = PickingMode.Ignore;

            Root.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0 && mode == Mode.Talk) advance = true;
            });
            Root.schedule.Execute(Hud).Every(0);
            Ui.Show(Root, false);
        }

        public void Show(bool on)
        {
            shown = on;
            Ui.Show(Root, on);
            world?.Show(on);
            if (!on) Lock(false);
        }

        // ------------------------------------------------------------------ run

        public async Awaitable Run(string id, StoryController run)
        {
            Abort();
            var s = session = new Session { Over = false };
            def = Dungeons.Get(id);
            if (def == null)
            {
                Debug.LogError($"<<dungeon {id}>>：Dungeons 里没有这个区域，直接跳过");
                return;
            }
            story = run;
            done = new AwaitableCompletionSource();
            world = DungeonWorld.Create(def, game.State.playerGender);
            world.Spotted += OnSpotted;
            world.Touched += OnTouched;
            var stealth = game.State.abilities.Contains("deep_dive") ? 0.6f : 1f; // DeepSeek's 深海潜行
            foreach (var e in world.Enemies) e.VisionScale = stealth;
            spottedTold = firewallTold = beaconTold = ambushTold = warned = false;
            FragmentsRead = ChestsOpened = Battles = Preemptive = Ambushed = Pushes = Turns = 0;
            meter = def.MeterStart;
            travelledAtLastTick = 0f;
            areaName.text = def.Area;
            areaLayer.text = def.Layer;
            areaTag.text = "LATENT SPACE · " + def.LayerTag;
            objective.text = def.Objective;
            // The zone the area starts in needs no banner: the start node introduces it.
            visited.Clear();
            zone = world.Zone;
            if (zone != null) visited.Add(zone);
            areaZone.text = zone != null ? "· " + zone.Name : "";
            bannerShown++;
            Ui.Show(banner, false);
            Ui.Show(meterBox, def.Meter);
            Ui.Show(talkBox, false);
            Ui.Show(prompt, false);
            RestoreParty(); // the way in counts as a beacon: no fight carries over
            BuildParty();
            minimap.Attach(world);
            marks.Clear();
            alertMarks.Clear();
            phraseMarks.Clear();
            foreach (var e in world.Enemies)
            {
                alertMarks[e] = Ui.Text("", "sp-dungeon__alert", marks, FontKind.Black);
                phraseMarks[e] = Ui.Text(e.Phrase, "sp-dungeon__phrase", marks, FontKind.Black);
            }
            mode = Mode.Busy;
            world.Show(shown);

            await Anim.Until(() => !game.Transitions.Busy);
            if (s.Over) return;
            if (def.StartNode != null) await Talk(def.StartNode, s);
            if (s.Over) return;
            mode = Mode.Explore;
            await done.Awaitable;
        }

        // After the cover of the way out: the area goes away.
        public void Leave()
        {
            mode = Mode.Off;
            Lock(false);
            if (world != null) UnityEngine.Object.Destroy(world.gameObject);
            world = null;
            minimap.Attach(null);
        }

        public void Abort()
        {
            session.Over = true;
            var d = done;
            done = null;
            d?.TrySetResult();
            Leave();
        }

        // Nulling done first keeps Act from handing the controls back on the way out.
        void Finish()
        {
            mode = Mode.Busy;
            var d = done;
            done = null;
            d?.TrySetResult();
        }

        // ------------------------------------------------------------------ input

        public void HandleInput()
        {
            if (world == null) return;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            switch (mode)
            {
                case Mode.Talk:
                    world.Idle(dt);
                    if (GameInput.Consume(Btn.Menu)) { game.Menu.Open(); return; }
                    if (GameInput.Consume(Btn.Log)) { game.Backlog.Open(); return; }
                    if (GameInput.Consume(Btn.Confirm) || GameInput.Consume(Btn.Interact) || GameInput.ScrollY < 0) advance = true;
                    break;
                case Mode.Explore:
                    if (GameInput.Consume(Btn.Menu)) { game.Menu.Open(); return; }
                    if (GameInput.Consume(Btn.Phone)) { game.Phone.Open(); return; }
                    if (GameInput.Consume(Btn.Log)) { game.Backlog.Open(); return; }
                    Explore(dt);
                    break;
                default:
                    world.Idle(dt);
                    break;
            }
        }

        void Explore(float dt)
        {
            var look = GameInput.Look * MouseDegreesPerPixel;
            world.Step(dt, GameInput.Move, GameInput.SprintHeld, GameInput.Turn, look);
            if (mode != Mode.Explore) return; // ran into something during the step

            if (def.Meter)
            {
                meter += (world.Travelled - travelledAtLastTick) / MetresPerPercent;
                travelledAtLastTick = world.Travelled;
                if (CheckMeter()) return;
            }

            var here = world.Zone;
            if (here != null && here != zone)
            {
                zone = here;
                areaZone.text = "· " + here.Name;
                if (visited.Add(here))
                {
                    Banner(here).Forget();
                    if (here.Node != null)
                    {
                        Act(s => Talk(here.Node, s));
                        return;
                    }
                }
            }

            foreach (var p in world.Props)
            {
                if (p.Kind == PropKind.Beacon && !p.Used && Vector3.Distance(p.Position, world.PlayerPosition) < p.Reach)
                {
                    Act(s => RestAt(p, s));
                    return;
                }
                if (p.Kind == PropKind.Goal && world.PlayerCell == p.Cell && world.GoalOpen)
                {
                    Act(Goal);
                    return;
                }
            }
            if (!firewallTold && def.FirewallNode != null && world.FirewallNear(Dungeons.Cell * 1.4f))
            {
                firewallTold = true;
                Act(s => Talk(def.FirewallNode, s));
                return;
            }

            if (!GameInput.Consume(Btn.Confirm) && !GameInput.Consume(Btn.Interact)) return;
            var enemy = world.EnemyInReach(AttackReach);
            if (enemy != null)
            {
                var opening = enemy.Alerted ? BattleOpening.Normal : BattleOpening.Preemptive;
                Act(s => Engage(enemy, opening, s));
                return;
            }
            var block = world.BlockInReach(out var direction);
            if (block != null)
            {
                Push(block, direction);
                return;
            }
            var plate = world.PlateInReach();
            if (plate != null)
            {
                Turns++;
                game.Audio.Sfx("key", 0.7f);
                if (world.Turn(plate)) Act(LightFound);
                return;
            }
            var prop = world.PropInReach();
            if (prop != null) Act(s => Use(prop, s));
        }

        // A cache block a cell on, or a thud when something is in the way; the last one into its socket
        // opens the gate.
        void Push(CacheBlock block, Vector2Int direction)
        {
            var wasSolved = world.Puzzles.Solved;
            if (!world.Push(block, direction))
            {
                game.Audio.Sfx("slap", 0.5f);
                return;
            }
            Pushes++;
            game.Audio.Sfx(world.Puzzles.InSocket(block) ? "confirm" : "whoosh", 0.6f);
            if (world.Puzzles.Solved && !wasSolved) Act(GateOpened);
        }

        async Awaitable GateOpened(Session s)
        {
            game.Audio.Sfx("rise");
            var stamp = Fx.Stamp(fx, "门开了！", Palette.Green, 110f, -4f, new Vector2(960, 440));
            await Anim.Wait(0.9f);
            Fx.Dismiss(stamp).Forget();
            if (s.Over) return;
            if (def.GateNode != null) await Talk(def.GateNode, s);
        }

        // The light on the goal: the exit shows where it lands.
        async Awaitable LightFound(Session s)
        {
            game.Audio.Sfx("rise");
            await Flash(Palette.Paper, 0.5f, s);
            var stamp = Fx.Stamp(fx, "出口出现了！", Palette.Sea, 110f, -4f, new Vector2(960, 440));
            await Anim.Wait(0.9f);
            Fx.Dismiss(stamp).Forget();
            if (s.Over) return;
            if (def.LightNode != null) await Talk(def.LightNode, s);
        }

        // Runs one thing that takes the controls away (talking, a battle...) and gives them back after.
        void Act(Func<Session, Awaitable> body)
        {
            mode = Mode.Busy;
            Lock(false);
            Do().Forget();

            async Awaitable Do()
            {
                var s = session;
                await body(s);
                if (!s.Over && mode == Mode.Busy && done != null) mode = Mode.Explore;
            }
        }

        void OnSpotted(SeaNoise e)
        {
            game.Audio.Sfx("heartbeat", 0.8f);
            if (spottedTold || def.SpottedNode == null) return;
            spottedTold = true;
            // The first time, a word from the player; the chase keeps going after it.
            Act(s => Talk(def.SpottedNode, s));
        }

        void OnTouched(SeaNoise e)
        {
            if (mode != Mode.Explore) return;
            Act(s => Engage(e, e.Alerted ? BattleOpening.Ambush : BattleOpening.Normal, s));
        }

        // ------------------------------------------------------------------ what happens

        async Awaitable Engage(SeaNoise enemy, BattleOpening opening, Session s)
        {
            var first = Battles == 0;
            if (first && def.FirstBattleNode != null)
            {
                await Talk(def.FirstBattleNode, s);
                if (s.Over) return;
            }
            var text = opening == BattleOpening.Preemptive ? "先制攻击！" : opening == BattleOpening.Ambush ? "被偷袭！" : "遭遇！";
            var color = opening == BattleOpening.Ambush ? Palette.Red : opening == BattleOpening.Preemptive ? Palette.Green : Palette.Paper;
            game.Audio.Sfx(opening == BattleOpening.Ambush ? "glitch" : "slam");
            var stamp = Fx.Stamp(fx, text, color, 130f, -5f, new Vector2(960, 440));
            await Anim.Wait(0.75f);
            Fx.Dismiss(stamp).Forget();
            if (s.Over) return;
            Battles++;
            if (opening == BattleOpening.Preemptive) Preemptive++;
            if (opening == BattleOpening.Ambush) Ambushed++;
            await game.RunBattle(enemy.Spawn.Battle ?? "sea_clickbait", opening);
            if (s.Over) return;
            enemy.Remove();
            foreach (var e in world.Enemies) e.Calm();
            world.SafeUntil = world.Time + 3f;
            BuildParty();
            meter += BattlePercent;
            if (CheckMeter()) return;
            if (first && def.FirstWinNode != null) await Talk(def.FirstWinNode, s);
            if (s.Over) return;
            if (opening == BattleOpening.Ambush && !ambushTold && def.AmbushHintNode != null)
            {
                ambushTold = true;
                await Talk(def.AmbushHintNode, s);
            }
        }

        async Awaitable Use(SeaProp prop, Session s)
        {
            prop.Use(world.Time);
            switch (prop.Kind)
            {
                case PropKind.Fragment:
                    FragmentsRead++;
                    game.Audio.Sfx("confirm");
                    if (prop.Index < def.FragmentNodes.Length) await Talk(def.FragmentNodes[prop.Index], s);
                    break;
                case PropKind.Chest:
                {
                    ChestsOpened++;
                    game.Audio.Sfx("coin");
                    var chest = prop.Index < def.Chests.Length ? def.Chests[prop.Index] : null;
                    if (chest == null)
                    {
                        Debug.LogError($"迷宫 {def.Id} 的第 {prop.Index + 1} 个宝箱没有内容");
                        break;
                    }
                    if (chest.Item != null) game.State.AddItem(chest.Item, chest.Count);
                    game.State.tokens += chest.Tokens;
                    if (chest.Delay > 0f) meter = Mathf.Max(0f, meter - chest.Delay);
                    game.Notice("获得：" + chest.Label);
                    await Anim.Wait(0.5f);
                    break;
                }
                case PropKind.Console:
                    game.Audio.Sfx("glitch", 0.6f);
                    world.ResetBlocks();
                    game.Notice("缓存块已回到原位");
                    await Anim.Wait(0.3f);
                    break;
                case PropKind.Terminal:
                    game.Audio.Sfx("key");
                    world.OpenFirewall();
                    if (def.ObjectiveAfterTerminal != null) objective.text = def.ObjectiveAfterTerminal;
                    if (def.TerminalNode != null) await Talk(def.TerminalNode, s);
                    break;
            }
        }

        // Beacons light up as the player passes: a rest for the party.
        async Awaitable RestAt(SeaProp beacon, Session s)
        {
            beacon.Use(world.Time);
            game.Audio.Sfx("rise", 0.7f);
            RestoreParty();
            BuildParty();
            if (!beaconTold && def.BeaconNode != null)
            {
                beaconTold = true;
                await Talk(def.BeaconNode, s);
            }
            else game.Notice("信标：HP 和 SP 已回满");
        }

        void RestoreParty()
        {
            foreach (var id in game.State.party)
            {
                var m = game.State.Member(id);
                var stats = CombatStats.ForCharacter(id, m.level);
                m.hp = stats.MaxHp;
                m.sp = stats.MaxSp;
            }
        }

        async Awaitable Goal(Session s)
        {
            if (def.GoalNode != null) await Talk(def.GoalNode, s);
            if (s.Over) return;
            Finish();
        }

        // True when the night is over.
        bool CheckMeter()
        {
            if (!def.Meter) return false;
            if (!warned && meter >= 80f && def.WarningNode != null)
            {
                warned = true;
                Act(s => Talk(def.WarningNode, s));
                return true;
            }
            if (meter < 100f) return false;
            meter = 100f;
            Act(async s =>
            {
                game.Audio.Sfx("glitch");
                if (def.TimeoutNode != null) await Talk(def.TimeoutNode, s);
                if (!s.Over) Finish();
            });
            return true;
        }

        async Awaitable Talk(string node, Session s)
        {
            if (s.Over || story == null) return;
            var previous = mode;
            mode = Mode.Talk;
            Lock(false);
            await story.RunHint(node, this);
            Ui.Show(talkBox, false);
            if (!s.Over && mode == Mode.Talk) mode = previous == Mode.Explore ? Mode.Busy : previous;
        }

        async Awaitable Banner(ZoneDef z)
        {
            var serial = ++bannerShown;
            bannerName.text = z.Name;
            bannerTag.text = "AREA · " + z.Tag;
            Ui.Show(banner, true);
            game.Audio.Sfx("whoosh", 0.6f);
            await Anim.Tween(0.32f, t =>
            {
                Ui.Offset(banner, Mathf.Lerp(-900f, 0f, t), 0f);
                Ui.Opacity(banner, t);
            }, Ease.OutBack, () => serial != bannerShown);
            await Anim.Wait(1.9f, () => serial != bannerShown);
            if (serial != bannerShown) return;
            await Anim.Tween(0.28f, t =>
            {
                Ui.Offset(banner, Mathf.Lerp(0f, 260f, t), 0f);
                Ui.Opacity(banner, 1f - t);
            }, Ease.InCubic, () => serial != bannerShown);
            if (serial == bannerShown) Ui.Show(banner, false);
        }

        async Awaitable Flash(Color color, float seconds, Session s)
        {
            Ui.Bg(flash, color);
            await Anim.Tween(seconds, t => flash.style.opacity = Mathf.Sin(t * Mathf.PI) * 0.8f, null, () => s.Over);
            flash.style.opacity = 0f;
        }

        // ------------------------------------------------------------------ lines (ILinePresenter)

        public async Awaitable PresentLine(LineInfo line, Func<bool> nextRequested, Func<bool> hurryRequested)
        {
            var s = session;
            advance = false;
            var text = line.Kind == LineKind.Inner ? $"（{line.Text}）" : line.Text;
            talkBox.EnableInClassList("is-system", line.Kind == LineKind.System || line.Kind == LineKind.Screen);
            talkBox.EnableInClassList("is-narration", line.Kind == LineKind.Narration);
            talkBox.EnableInClassList("is-inner", line.Kind == LineKind.Inner);
            string name;
            Color plate;
            switch (line.Kind)
            {
                case LineKind.Narration: name = null; plate = Palette.Paper; break;
                case LineKind.System: name = "SYSTEM"; plate = Palette.Green; break;
                case LineKind.Screen: name = "屏幕"; plate = Palette.Green; break;
                case LineKind.Inner: name = "心声"; plate = Palette.Green; break;
                default:
                    name = line.Speaker;
                    plate = GameData.Character(line.Speaker)?.Color ?? (line.Kind == LineKind.Player ? Palette.Green : Palette.Paper);
                    break;
            }
            Ui.Show(talkPlate, name != null);
            talkName.text = name ?? "";
            talkPlate.Q<Shape>()?.Fill(plate);
            Ui.Show(talkBox, true);

            var cps = game.Settings.CharsPerSecond;
            var shown = float.IsInfinity(cps) ? text.Length : 0f;
            while (shown < text.Length && !s.Over && !nextRequested())
            {
                if (advance || SkipHeld(line))
                {
                    advance = false;
                    break;
                }
                shown += Time.unscaledDeltaTime * cps;
                talkText.text = Ui.GluePunctuation(text.Substring(0, Mathf.Min(text.Length, (int)shown)));
                await Awaitable.NextFrameAsync();
            }
            talkText.text = Ui.GluePunctuation(text);
            while (!s.Over && !nextRequested() && !advance && !SkipHeld(line)) await Awaitable.NextFrameAsync();
            advance = false;
            game.Records.MarkRead(line.Id);
            game.Story.Backlog.Add(new BacklogEntry
            {
                Speaker = line.Kind == LineKind.Narration ? null : line.Speaker,
                Text = text,
                Kind = line.Kind,
            });
        }

        public async Awaitable<int> PresentOptions(string[] texts, bool[] available, Func<bool> cancelled)
        {
            Debug.LogWarning("潜空间里的剧本节点不支持选项，已跳过");
            await Awaitable.NextFrameAsync();
            return -1;
        }

        bool SkipHeld(LineInfo line) => GameInput.SkipHeld && (game.Settings.skipUnread || game.Records.IsRead(line.Id));

        // ------------------------------------------------------------------ HUD

        void BuildParty()
        {
            partyRow.Clear(); // shown by Hud
            if (def == null) return;
            foreach (var id in game.State.party)
            {
                var m = game.State.Member(id);
                var stats = CombatStats.ForCharacter(id, m.level);
                var card = Ui.El("sp-dungeon__member", partyRow);
                var color = id == GameState.PlayerId ? Palette.Green : GameData.Character(id)?.Color ?? Palette.Paper;
                Shape.Backdrop(card, Shape.Slant(12), Palette.Ink.WithAlpha(0.92f)).Stroke(color, 2).Shadow(new Vector2(6, 6), color.WithAlpha(0.6f));
                Ui.Text(id == GameState.PlayerId ? game.State.playerName : id, "sp-dungeon__member-name", card, FontKind.Black).style.color = color;
                Ui.Text($"Lv{m.level}", "sp-dungeon__member-level", card, FontKind.MonoBold);
                Bar(card, "HP", m.hp, stats.MaxHp, Palette.Green);
                Bar(card, "SP", m.sp, stats.MaxSp, Palette.Sea);
            }
        }

        static void Bar(VisualElement parent, string label, int value, int max, Color color)
        {
            var row = Ui.El("sp-dungeon__bar", parent);
            Ui.Text(label, "sp-dungeon__bar-label", row, FontKind.MonoBold);
            var track = Ui.El("sp-dungeon__bar-track", row);
            var fill = Ui.El("sp-dungeon__bar-fill", track);
            fill.style.width = Length.Percent(100f * Mathf.Clamp01(max > 0 ? (float)value / max : 0f));
            Ui.Bg(fill, color);
            Ui.Text($"{value}/{max}", "sp-dungeon__bar-value", row, FontKind.Mono);
        }

        // Every frame while shown: what floats over the world, the map, the meter, the cursor.
        void Hud()
        {
            if (world == null || !shown) return;
            minimap.Refresh();
            if (def.Meter)
            {
                var m = Mathf.Clamp(meter, 0f, 100f);
                meterText.text = $"{Mathf.FloorToInt(m)}%";
                meterFill.style.width = Length.Percent(m);
                meterBox.EnableInClassList("is-late", m >= 80f);
            }

            var exploring = mode == Mode.Explore;
            Lock(exploring && !game.ModalOpen && !game.Transitions.Busy && Application.isFocused);
            // The text box takes the bottom of the screen while someone talks.
            Ui.Show(partyRow, mode != Mode.Talk);
            Ui.Show(keys, mode != Mode.Talk);
            Root.pickingMode = mode == Mode.Talk ? PickingMode.Position : PickingMode.Ignore; // a click anywhere reads on

            // The prompt over what Enter would act on.
            Vector3? promptAt = null;
            var enemy = exploring ? world.EnemyInReach(AttackReach) : null;
            CacheBlock block = null;
            LightPlate plate = null;
            if (enemy != null)
            {
                promptVerb.text = enemy.Alerted ? "迎击" : "先制攻击！";
                prompt.EnableInClassList("is-attack", true);
                promptAt = enemy.Position + Vector3.up * 2.9f;
            }
            else if (exploring && (block = world.BlockInReach(out _)) != null)
            {
                promptVerb.text = "推";
                prompt.EnableInClassList("is-attack", false);
                promptAt = world.Map.World(block.Cell) + Vector3.up * 3.2f;
            }
            else if (exploring && (plate = world.PlateInReach()) != null)
            {
                promptVerb.text = "转动反射板";
                prompt.EnableInClassList("is-attack", false);
                promptAt = world.Map.World(plate.Cell) + Vector3.up * 3.4f;
            }
            else if (exploring && world.PropInReach() is SeaProp prop)
            {
                promptVerb.text = prop.Kind == PropKind.Fragment && prop.Index < def.FragmentTitles.Length
                    ? $"{prop.Verb}「{def.FragmentTitles[prop.Index]}」"
                    : prop.Verb;
                prompt.EnableInClassList("is-attack", false);
                promptAt = prop.Position + Vector3.up * (prop.Kind == PropKind.Fragment ? 3.2f : 2.4f);
            }
            if (promptAt.HasValue && Project(promptAt.Value, out var pp))
            {
                // Beside the point, clear of the alert mark above an enemy's head.
                Ui.Show(prompt, true);
                prompt.style.left = pp.x + 36f;
                prompt.style.top = pp.y - 24f;
            }
            else Ui.Show(prompt, false);

            foreach (var e in world.Enemies)
            {
                var alert = alertMarks[e];
                var phrase = phraseMarks[e];
                var d = Vector3.Distance(e.Position, world.PlayerPosition);
                var head = e.Position + Vector3.up * 2.5f;
                var hp = Vector2.zero;
                var onScreen = e.Active && d < 40f && Project(head, out hp);
                var mark = !e.Active ? "" : e.State == NoiseState.Chase ? "!" : e.State == NoiseState.Look ? "?" : e.State == NoiseState.Doze ? "z" : "";
                Ui.Show(alert, onScreen && mark != "");
                Ui.Show(phrase, onScreen && d < 16f && e.State != NoiseState.Chase && Mathf.Repeat(world.Time + e.Phrase.Length * 0.37f, 2.6f) < 1.7f);
                if (!onScreen) continue;
                alert.text = mark;
                alert.EnableInClassList("is-chase", e.State == NoiseState.Chase);
                alert.EnableInClassList("is-doze", e.State == NoiseState.Doze);
                alert.style.left = hp.x - 30f;
                alert.style.top = hp.y - 90f;
                phrase.style.left = hp.x - 90f;
                phrase.style.top = hp.y - 40f;
            }
        }

        // World point to this screen's 1920x1080 frame.
        bool Project(Vector3 worldPoint, out Vector2 local)
        {
            local = default;
            if (Root.panel == null || !world.ToScreen(worldPoint, out var screen)) return false;
            var top = Root.panel.visualTree.layout;
            var panelPoint = new Vector2(screen.x * top.width / Screen.width, screen.y * top.height / Screen.height);
            local = Root.WorldToLocal(panelPoint);
            return local.x > -200f && local.x < 2120f && local.y > -200f && local.y < 1280f;
        }

        static void Lock(bool on)
        {
            var want = on ? CursorLockMode.Locked : CursorLockMode.None;
            if (UnityEngine.Cursor.lockState == want) return;
            UnityEngine.Cursor.lockState = want;
            UnityEngine.Cursor.visible = !on;
        }
    }
}
