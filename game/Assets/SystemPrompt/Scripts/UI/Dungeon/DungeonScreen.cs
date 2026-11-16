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
    // the maintenance meter, the party, prompts and alert marks over the world, a text box for the story
    // nodes the area runs (zones, fragments, the terminal...), and DeepSeek calling out from the top right
    // for the ones that don't stop the player (NavCall: the gate, the exit, the warning, ambush hints).
    // Noise bodies are fought, JRPG style: hitting one with Enter before it has seen the party gives a free
    // round, one that chased the party down gets the first move, and running into one otherwise is a plain
    // battle. Enter also pushes cache blocks and turns light plates (Dungeon/SeaPuzzles.cs).
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
        // Each turn of a light plate, either way: the white hall's puzzle costs maintenance time too, so its two ways
        // round (design/prologue-playtest.md 改法 8) and a turn too many are felt on the meter.
        const float TurnPercent = 1f;
        const float BounceMeter = 60f;   // where the meter falls back to when it ran out (Bounce)
        const float CallRange = 18f;     // a noise body that spots the player calls another this close to it
        const float JoinRange = 20f;     // one alerted this close to the one fought joins the battle
        const float SodaHintRange = 14f; // a patrolling noise body this close brings DeepSeek's word on the 冰镇汽水

        public readonly VisualElement Root;
        readonly GameRoot game;
        readonly Label areaName, areaLayer, areaTag, areaZone, objective, pearlCount;
        const int PearlSp = 5; // SP back for each member with each 数据珍珠
        readonly VisualElement banner;
        readonly Label bannerTag;
        readonly SplitText bannerName;
        readonly Minimap minimap;
        readonly VisualElement meterBox, meterFill;
        readonly Label meterText;
        readonly VisualElement partyRow;
        readonly VisualElement keys;
        readonly Label keysText;
        const string Keys = "WASD 移动　Shift 冲刺　Q/E·鼠标 视角　Enter 调查/攻击　Esc 菜单";
        readonly VisualElement marks;
        readonly VisualElement prompt;
        readonly Label promptVerb;
        readonly VisualElement talkBox, talkPlate;
        readonly Label talkName, talkText;
        readonly VisualElement fx, flash, edge;
        readonly VisualElement barTop, barBottom; // a cutaway's letterbox
        readonly VisualElement[] chrome;          // the HUD a cutaway takes off the screen
        readonly NavCall nav;
        readonly Dictionary<SeaNoise, Label> alertMarks = new Dictionary<SeaNoise, Label>();
        readonly Dictionary<SeaNoise, Label> phraseMarks = new Dictionary<SeaNoise, Label>();
        readonly Dictionary<SeaNoise, float> markSince = new Dictionary<SeaNoise, float>(); // when its alert mark last changed

        Session session = new Session { Over = true };
        AwaitableCompletionSource done;
        DungeonWorld world;
        DungeonDef def;
        StoryController story;
        Mode mode = Mode.Off;
        bool shown;
        bool advance;
        bool hintRunning; // the story's one hint runner is busy with a node (a talk, or one read into the nav)
        float meter;
        float travelledAtLastTick;
        bool spottedTold, firewallTold, beaconTold, ambushTold, stuckTold, warned, sodaTold;
        ZoneDef zone;
        readonly HashSet<ZoneDef> visited = new HashSet<ZoneDef>();
        int bannerShown;
        float bannerUntil; // unscaled time the zone banner is gone by
        bool cutting, departing, striking, chromeHidden; // the HUD is away through a cutaway, from a departure to the wipe
                                                        // and from a strike till its battle is over (its stamp has the screen)

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
        public int Undos { get; private set; }
        public int Turns { get; private set; }
        public int PearlsTaken { get; private set; }
        public int Bounces { get; private set; }
        public bool Warned => warned;
        public bool SodaTold => sodaTold;
        public ZoneDef Zone => zone;
        public int ZonesVisited => visited.Count;
        public NavCall Nav => nav;
        // A cutaway under way (Cutaway): the shot's name and how far through it it is.
        public bool Cutting => cutting;
        public string Cut { get; private set; }
        public float CutProgress { get; private set; }

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
            pearlCount = Ui.Text("", "sp-dungeon__pearls", area, FontKind.MonoBold);

            // A new zone slams its name across the left of the screen.
            banner = Ui.El("sp-dungeon__zone", Root);
            Shape.Backdrop(banner, Shape.Slant(30), Palette.Ink.WithAlpha(0.94f)).Stroke(Palette.Green, 3).Shadow(new Vector2(12, 12), Palette.Sea.WithAlpha(0.85f));
            bannerTag = Ui.Text("", "sp-dungeon__zone-tag", banner, FontKind.MonoBold);
            bannerName = new SplitText("", "sp-dungeon__zone-name", FontKind.Black);
            banner.Add(bannerName);
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

            // Left of the map; a line waits while the screen is away (a battle) or the text box is up.
            nav = new NavCall(game, Root, () => !shown || world == null || mode == Mode.Talk || cutting);

            partyRow = Ui.El("sp-dungeon__party", Root);

            keys = Ui.El("sp-dungeon__keys", Root);
            keysText = Ui.Text(Keys, "sp-dungeon__keys-text", keys, FontKind.MonoBold);

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

            chrome = new[] { area, goal, mapBox, meterBox, nav.Root, partyRow, keys, prompt, marks };
            barTop = LetterBar(true);
            barBottom = LetterBar(false);

            edge = Ui.El("sp-dungeon__edge", Root);
            Ui.Fill(edge);
            edge.pickingMode = PickingMode.Ignore;
            edge.style.backgroundImage = new StyleBackground(EdgeShade);
            edge.style.unityBackgroundImageTintColor = new Color(1f, 0.2f, 0.2f);
            edge.style.opacity = 0f;
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

        // The camera coming down into the maze at the end of the dive (DungeonWorld.Arrive).
        public void Arrive(float t) => world?.Arrive(t);

        public void Show(bool on)
        {
            shown = on;
            Ui.Show(Root, on);
            world?.Show(on);
            if (!on) Lock(false);
        }

        // ------------------------------------------------------------------ run

        // built: the area, from DungeonWorld.Create; it is the screen's from here on.
        // `resume`: a save made at a beacon here, read back (GameRoot.LoadGame): the maze as it was left (Resume).
        public async Awaitable Run(string id, StoryController run, DungeonWorld built, DungeonSave resume = null)
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
            world = built;
            world.Spotted += OnSpotted;
            world.Touched += OnTouched;
            nav.Clear();
            nav.Companion = world.Companion;
            hintRunning = false;
            var stealth = game.State.abilities.Contains("deep_dive") ? 0.6f : 1f; // DeepSeek's 深海潜行
            foreach (var e in world.Enemies)
            {
                e.VisionScale = stealth;
                e.ChaseSpeed = def.ChaseSpeed;
            }
            spottedTold = firewallTold = beaconTold = ambushTold = stuckTold = warned = sodaTold = false;
            FragmentsRead = ChestsOpened = Battles = Preemptive = Ambushed = Pushes = Undos = Turns = PearlsTaken = Bounces = 0;
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
            // Pearls taken on an earlier dive stay taken.
            foreach (var p in world.Pearls)
                if (game.State.HasPearl(def.Id, p.Index)) p.Take(-10f);
            ShowPearls();
            minimap.Attach(world);
            marks.Clear();
            alertMarks.Clear();
            phraseMarks.Clear();
            markSince.Clear();
            foreach (var e in world.Enemies)
            {
                alertMarks[e] = Ui.Text("", "sp-dungeon__alert", marks, FontKind.Black);
                phraseMarks[e] = Ui.Text(e.Phrase, "sp-dungeon__phrase", marks, FontKind.Black);
            }
            if (resume != null) Resume(resume);
            mode = Mode.Busy;
            world.Show(shown);

            await Anim.Until(() => !game.Transitions.Busy);
            if (s.Over) return;
            if (resume != null) NoticeAfterBanner("从信标继续").Forget();
            else if (def.StartNode != null) await Talk(def.StartNode, s);
            if (s.Over) return;
            mode = Mode.Explore;
            await done.Awaitable;
        }

        // <<mirror on|off>> from the exit's node (Dungeon/DungeonWorld.cs Mirror).
        public async Awaitable Mirror(bool on)
        {
            if (world != null) await world.Mirror(on);
        }

        // <<mirror walk>>: the two go up to the white door with the text box out of the way; confirm or cancel after the
        // first 0.2 s hurries them, as in a cutaway.
        public async Awaitable MirrorWalk()
        {
            var w = world;
            if (w == null) return;
            var s = session;
            var was = mode;
            mode = Mode.Busy;
            var talking = was == Mode.Talk && talkBox.style.display != DisplayStyle.None;
            Ui.Show(talkBox, false);
            var started = Time.unscaledTime;
            var hurried = false;
            try
            {
                await w.MirrorWalk(() => hurried || (hurried = HurryAsked(started)), () => s.Over);
            }
            finally
            {
                if (!s.Over && mode == Mode.Busy && done != null)
                {
                    mode = was;
                    if (talking) Ui.Show(talkBox, true);
                }
            }
        }

        // After the cover of the way out: the area goes away.
        public void Leave()
        {
            mode = Mode.Off;
            Lock(false);
            departing = false;
            striking = false;
            nav.Clear();
            nav.Companion = null;
            if (world != null) world.Drop();
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

            // The one thing the player can do in the shallows besides the screenshot: DeepSeek points at the 冰镇汽水
            // the first battle dropped when the next noise body comes into view (design/prologue-playtest.md 改法 6).
            if (!sodaTold && def.SodaHintNode != null && Battles > 0 && game.State.ItemCount("ice_soda") > 0
                && world.Enemies.Exists(e => e.Active && e.State != NoiseState.Doze && Vector3.Distance(e.Position, world.PlayerPosition) < SodaHintRange))
            {
                sodaTold = true;
                Call(def.SodaHintNode, session).Forget();
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
                        if (here.Look is Vector2Int toward) world.SwingCamera(world.Map.World(toward));
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

            var pearl = world.PearlInReach();
            if (pearl != null) TakePearl(pearl);
            if (GameInput.Consume(Btn.Undo))
            {
                TakeBack();
                return;
            }
            if (!GameInput.Consume(Btn.Confirm) && !GameInput.Consume(Btn.Interact)) return;
            var enemy = world.EnemyInReach(AttackReach);
            if (enemy != null)
            {
                var opening = enemy.Alerted ? BattleOpening.Normal : BattleOpening.Preemptive;
                Act(s => Engage(enemy, opening, true, s));
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
                if (def.Meter) meter += TurnPercent;
                game.Audio.Sfx("key", 0.7f);
                if (world.Turn(plate)) LightFound(session).Forget();
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
            if (world.Puzzles.Solved && !wasSolved) GateOpened(session).Forget();
            else if (world.Puzzles.Stuck(block) && !stuckTold && def.StuckNode != null)
            {
                // Into a corner it can't come out of: it turns red, and DeepSeek says how to get it back.
                stuckTold = true;
                game.Audio.Sfx("glitch", 0.5f);
                Call(def.StuckNode, session).Forget();
            }
        }

        // 数据珍珠: taken by walking into it, a little SP back for everyone; the area's last one brings its reward.
        void TakePearl(SeaPearl pearl)
        {
            world.Take(pearl);
            game.State.TakePearl(def.Id, pearl.Index);
            PearlsTaken++;
            game.Audio.Sfx("coin", 0.8f);
            foreach (var id in game.State.party)
            {
                var m = game.State.Member(id);
                m.sp = Mathf.Min(CombatStats.ForCharacter(id, m.level).MaxSp, m.sp + PearlSp);
            }
            BuildParty();
            ShowPearls();
            var count = game.State.PearlsIn(def.Id);
            if (count >= def.Pearls.Length && def.PearlReward != null)
            {
                var reward = def.PearlReward;
                if (reward.Item != null) game.State.AddItem(reward.Item, reward.Count);
                game.State.tokens += reward.Tokens;
                game.Audio.Sfx("levelup", 0.7f);
                game.Notice("数据珍珠全部找到！获得：" + reward.Label);
                if (def.PearlsNode != null) Call(def.PearlsNode, session).Forget();
                return;
            }
            game.Notice($"数据珍珠 {count}/{def.Pearls.Length}　SP +{PearlSp}");
            if (count == 1 && def.PearlNode != null) Call(def.PearlNode, session).Forget();
        }

        void ShowPearls()
        {
            Ui.Show(pearlCount, def.Pearls.Length > 0);
            var count = game.State.PearlsIn(def.Id);
            pearlCount.text = $"◆ 数据珍珠 {count}/{def.Pearls.Length}";
            pearlCount.EnableInClassList("is-full", count >= def.Pearls.Length);
        }

        // Backspace: a light plate in reach turns back an eighth; otherwise the last push is taken back (a thud
        // when something stands where the block was).
        void TakeBack()
        {
            var plate = world.PlateInReach();
            if (plate != null)
            {
                Turns++;
                if (def.Meter) meter += TurnPercent;
                game.Audio.Sfx("key", 0.7f);
                if (world.Turn(plate, -1)) LightFound(session).Forget();
                return;
            }
            if (!world.Puzzles.CanUndo) return;
            var wasSolved = world.Puzzles.Solved;
            if (!world.Undo())
            {
                game.Audio.Sfx("slap", 0.5f);
                return;
            }
            Undos++;
            game.Audio.Sfx("whoosh", 0.5f);
            if (world.Puzzles.Solved && !wasSolved) GateOpened(session).Forget();
        }

        // The gate breaks up (where the player can see it, or in a cutaway once the block has seated) and DeepSeek
        // calls it out from the top right once the controls are back; the player keeps going. With the controls taken
        // by something else meanwhile (a talk, a fight), it waits for them to come back, so the opening is still seen.
        async Awaitable GateOpened(Session s)
        {
            game.Audio.Sfx("rise");
            world.Companion?.Wag();
            await Anim.Wait(0.35f, () => s.Over);
            await Anim.Until(() => mode == Mode.Explore || s.Over);
            if (s.Over) return;
            var gate = world.Puzzles.GatePosition;
            if (gate is Vector3 at && !world.InView(at + Vector3.up * 2f))
                await Cutaway(s, new Shot("gate", at, 1.6f, world.OpenGateVisual));
            else world.OpenGateVisual();
            if (s.Over) return;
            await Call(def.GateNode, s);
        }

        // <<reveal firewall>> in the terminal's node: the firewalls shown going, nearest first, the second from the
        // side that has the lighthouse behind it. Each is left for the story to show (DungeonWorld.OpenFirewall).
        public async Awaitable Reveal(string what)
        {
            if (world == null || what != "firewall")
            {
                Debug.LogError($"<<reveal {what}>>：只能在潜空间里揭示 firewall");
                return;
            }
            var s = session;
            var walls = new List<Firewall>(world.Firewalls);
            walls.RemoveAll(f => f.Opened);
            var player = world.PlayerPosition;
            walls.Sort((a, b) => Vector3.Distance(a.Position, player).CompareTo(Vector3.Distance(b.Position, player)));
            if (walls.Count == 0) return;
            var lamp = world.Map.World(def.LighthouseCell.x, def.LighthouseCell.y);
            var shots = new Shot[Mathf.Min(2, walls.Count)];
            for (var i = 0; i < shots.Length; i++)
            {
                var wall = walls[i];
                shots[i] = i == 0
                    ? new Shot("firewall-1", wall.Position, 1.5f, () => world.DissolveFirewall(wall))
                    // Low and looking up, so the lighthouse's lamp, 33 m up behind it, comes into the top of the picture.
                    : new Shot("firewall-2", wall.Position, 1.6f, () => world.DissolveFirewall(wall), lamp - wall.Position, 4.6f, 1.2f);
            }
            game.Audio.Sfx("rise", 0.5f);
            await Cutaway(s, shots);
        }

        // ------------------------------------------------------------------ cutaways

        // One shot of a cutaway: what it shows (looked at from the side `Prefer` points away from, if it can), for
        // how long, how far over the ground the lens stands and aims, and what it sets going as it cuts in.
        readonly struct Shot
        {
            public readonly string Name;
            public readonly Vector3 Focus;
            public readonly Vector3? Prefer;
            public readonly float Seconds, LookUp, Height;
            public readonly Action Start;

            public Shot(string name, Vector3 focus, float seconds, Action start, Vector3? prefer = null, float lookUp = 2f, float height = 3.5f)
            {
                Name = name;
                Focus = focus;
                Seconds = seconds;
                Start = start;
                Prefer = prefer;
                LookUp = lookUp;
                Height = height;
            }
        }

        // The camera taken off the player to show something happen out of their sight (the gate, the firewalls, the
        // exit), behind a letterbox, with hard cuts in and out and the HUD away. Confirm or cancel after the first 0.2 s
        // plays the rest three times as fast, the maze's clock with it. Each shot's effect starts whether or not
        // somewhere to look from is found; with nowhere for any of them, nothing is taken over. In a talk (a story
        // command) the text box steps aside and comes back.
        async Awaitable Cutaway(Session s, params Shot[] shots)
        {
            var w = world;
            if (w == null || s.Over) return;
            var eyes = new Vector3[shots.Length];
            var looks = new Quaternion[shots.Length];
            var found = new bool[shots.Length];
            var any = false;
            for (var i = 0; i < shots.Length; i++)
                any |= found[i] = w.FindShot(shots[i].Focus, out eyes[i], out looks[i], shots[i].Prefer, shots[i].Height, shots[i].LookUp);
            if (!any)
            {
                foreach (var shot in shots) shot.Start?.Invoke();
                return;
            }
            var was = mode;
            mode = Mode.Busy;
            Lock(false);
            var talking = was == Mode.Talk && talkBox.style.display != DisplayStyle.None;
            Ui.Show(talkBox, false);
            cutting = true;
            w.BeginCut();
            var started = Time.unscaledTime;
            var hurried = false;
            var total = 0f; // the shots' seconds, the letterbox's slides inside them (the controls gone no longer)
            for (var i = 0; i < shots.Length; i++)
                if (found[i]) total += shots[i].Seconds;
            var into = 0f;
            try
            {
                for (var i = 0; i < shots.Length && !s.Over; i++)
                {
                    var shot = shots[i];
                    shot.Start?.Invoke();
                    if (!found[i]) continue;
                    Cut = shot.Name;
                    for (var t = 0f; t < shot.Seconds && !s.Over;)
                    {
                        if (!hurried && HurryAsked(started))
                        {
                            hurried = true;
                            w.Hurry(true);
                        }
                        CutProgress = Mathf.Clamp01(t / shot.Seconds);
                        w.HoldShot(eyes[i], looks[i], shot.Focus, CutProgress);
                        // The letterbox in (to 9 % of the height top and bottom) over the cut's first 0.18 s, out over its
                        // last 0.15.
                        var bars = Mathf.Min(Ease.OutCubic(Mathf.Clamp01(into / 0.18f)), 1f - Ease.OutCubic(Mathf.Clamp01(1f - (total - into) / 0.15f)));
                        barTop.style.height = barBottom.style.height = Length.Percent(9f * bars);
                        await Awaitable.NextFrameAsync();
                        var step = Time.unscaledDeltaTime * (hurried ? 3f : 1f);
                        t += step;
                        into += step;
                    }
                }
            }
            finally
            {
                cutting = false;
                Cut = null;
                CutProgress = 0f;
                barTop.style.height = barBottom.style.height = Length.Percent(0f);
                if (w != null) w.EndCut();
                if (!s.Over && mode == Mode.Busy && done != null)
                {
                    mode = was;
                    if (talking) Ui.Show(talkBox, true);
                }
            }
        }

        // Confirm or cancel after a scene's first 0.2 s, or the text skip held, plays the rest three times as fast
        // (cutaways, the mirror walk, departures).
        static bool HurryAsked(float started) =>
            Time.unscaledTime - started > 0.2f && (GameInput.SkipHeld || GameInput.Consume(Btn.Confirm) || GameInput.Consume(Btn.Cancel));

        VisualElement LetterBar(bool top)
        {
            var bar = Ui.El("sp-dungeon__letterbox", Root);
            bar.pickingMode = PickingMode.Ignore;
            bar.style.position = Position.Absolute;
            bar.style.left = 0f;
            bar.style.right = 0f;
            if (top) bar.style.top = 0f;
            else bar.style.bottom = 0f;
            bar.style.height = Length.Percent(0f);
            Ui.Bg(bar, Palette.Ink);
            return bar;
        }

        // The light on the goal: once it has run all the way there (SeaPuzzles.LightArrived, under a second and a half),
        // a cutaway to the exit coming up out of the seabed (DungeonWorld.RiseExit), then DeepSeek calls it out. When the
        // controls are taken by something else by then, it waits for them to come back first.
        async Awaitable LightFound(Session s)
        {
            var w = world;
            var until = Time.unscaledTime + 3f;
            await Anim.Until(() => w.Puzzles.LightArrived || s.Over || Time.unscaledTime > until);
            await Anim.Until(() => mode == Mode.Explore || s.Over);
            if (s.Over) return;
            game.Audio.Sfx("rise");
            w.Companion?.Wag();
            await Cutaway(s, new Shot("exit", w.ExitPosition, 1.8f, w.RiseExit));
            if (s.Over) return;
            await Call(def.LightNode, s);
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
            // It calls the nearest noise body in earshot, which comes running too; the two are fought as one battle
            // if both are still on the party when it starts (Engage).
            SeaNoise ally = null;
            var best = CallRange;
            foreach (var o in world.Enemies)
            {
                if (o == e || !o.Active || o.Alerted) continue;
                var d = Vector3.Distance(o.Position, e.Position);
                if (d >= best) continue;
                best = d;
                ally = o;
            }
            if (ally != null)
            {
                ally.Alert(world.PlayerPosition);
                game.Notice("它叫来了同伴！");
            }
            if (spottedTold || def.SpottedNode == null) return;
            spottedTold = true;
            // The first time, a word from the player; the chase keeps going after it.
            Act(s => Talk(def.SpottedNode, s));
        }

        void OnTouched(SeaNoise e)
        {
            if (mode != Mode.Explore) return;
            Act(s => Engage(e, e.Alerted ? BattleOpening.Ambush : BattleOpening.Normal, false, s));
        }

        // ------------------------------------------------------------------ what happens

        // byEnter: the player jumped it (先制攻击, or 迎击 on one already chasing) rather than it running into them.
        async Awaitable Engage(SeaNoise enemy, BattleOpening opening, bool byEnter, Session s)
        {
            var first = Battles == 0;
            if (first && def.FirstBattleNode != null)
            {
                await Talk(def.FirstBattleNode, s);
                if (s.Over) return;
            }
            var fight = SystemPrompt.Battle.Battles.Get(enemy.Spawn.Battle ?? "sea_clickbait");
            // Noise bodies called over that have caught up join the fight, nearest first, while the field has room.
            var joined = new List<SeaNoise>();
            while (fight != null && fight.Enemies.Length < SystemPrompt.Battle.Battles.MaxFoes)
            {
                SeaNoise near = null;
                var best = JoinRange;
                foreach (var o in world.Enemies)
                {
                    if (o == enemy || !o.Active || !o.Alerted || joined.Contains(o)) continue;
                    var d = Vector3.Distance(o.Position, enemy.Position);
                    if (d >= best) continue;
                    best = d;
                    near = o;
                }
                if (near == null) break;
                var more = SystemPrompt.Battle.Battles.Get(near.Spawn.Battle ?? "sea_clickbait");
                if (more == null || fight.Enemies.Length + more.Enemies.Length > SystemPrompt.Battle.Battles.MaxFoes) break;
                fight = BattleDef.Merge(fight, more);
                joined.Add(near);
            }
            var text = opening == BattleOpening.Preemptive ? "先制攻击！" : opening == BattleOpening.Ambush ? "被偷袭！" : "遭遇！";
            var color = opening == BattleOpening.Ambush ? Palette.Red : opening == BattleOpening.Preemptive ? Palette.Green : Palette.Paper;
            game.Audio.Sfx(opening == BattleOpening.Ambush ? "glitch" : "slam");
            world.Strike(enemy, opening, byEnter, joined);
            striking = true;
            // Over the noise body as the strike's camera holds it once it has punched in: the stamp's lower edge a margin
            // over the body's top, its own top on the screen, its middle no lower than the two of them's heads. Where
            // there's no room for that, a fifth smaller, and failing that off to the body's open side.
            float HalfHigh(float f) => 0.67f * f + 80f; // settled, burst and all (.fx-stamp)
            float HalfWide(float f) => 0.5f * text.Length * f + 120f;
            var size = 130f;
            var at = new Vector2(960f, 300f);
            if (Project(enemy.StruckTop, out var top, true) && Project(enemy.Position + Vector3.up * NoiseLook.Height, out var middle, true))
            {
                var y = top.y - 24f - HalfHigh(size);
                if (y < 12f + HalfHigh(size))
                {
                    size = 104f;
                    y = top.y - 24f - HalfHigh(size);
                }
                at.y = Mathf.Clamp(y, 12f + HalfHigh(size), 420f);
                if (y < at.y)
                {
                    var side = top.x < 960f ? 1f : -1f;
                    var off = 0.8f * (middle.y - top.y) + HalfWide(size) + 24f;
                    at.x = Mathf.Clamp(top.x + side * off, 12f + HalfWide(size), 1908f - HalfWide(size));
                }
            }
            var stamp = Fx.Stamp(fx, text, color, size, -5f, at);
            if (opening == BattleOpening.Ambush) Alarm(s).Forget();
            await Anim.Wait(0.75f);
            Fx.Dismiss(stamp).Forget();
            if (s.Over) return;
            Battles++;
            if (opening == BattleOpening.Preemptive) Preemptive++;
            if (opening == BattleOpening.Ambush) Ambushed++;
            if (fight == null)
            {
                Debug.LogError($"噪声体 {enemy.Spawn.Mark} 的战斗 {enemy.Spawn.Battle} 不在 Battles 里");
                striking = false;
                world.EndStrike(false);
                return;
            }
            // The world hides the struck bodies when the battle takes the screen (DungeonWorld.Show).
            await game.RunBattle(fight, opening);
            striking = false;
            if (s.Over) return;
            world.EndStrike(true); // if the battle never took the screen
            enemy.Remove();
            foreach (var o in joined) o.Remove();
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
                Call(def.AmbushHintNode, s).Forget();
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
                    world.OpenChest(prop);
                    if (chest.Item != null) game.State.AddItem(chest.Item, chest.Count);
                    game.State.tokens += chest.Tokens;
                    if (chest.Delay > 0f) meter = Mathf.Max(0f, meter - chest.Delay);
                    game.Notice("获得：" + chest.Label);
                    await Anim.Wait(0.5f);
                    // The controls back: the camera turns back over 0.25 s as the follow camera takes over.
                    world.Glance(null);
                    break;
                }
                case PropKind.Console:
                    game.Audio.Sfx("glitch", 0.6f);
                    world.ResetBlocks();
                    game.Notice("缓存块已回到原位");
                    await Anim.Wait(0.3f);
                    break;
                case PropKind.Beacon:
                    // A lit beacon again: a rest, and the save screen; a save here goes on from this beacon.
                    game.Audio.Sfx("rise", 0.5f);
                    RestoreParty();
                    BuildParty();
                    game.SaveLoad.OpenSave();
                    await Anim.Until(() => !game.SaveLoad.IsOpen || s.Over);
                    break;
                case PropKind.Terminal:
                    // Open at once; the walls are shown going by the node's <<reveal firewall>>, or go after it.
                    game.Audio.Sfx("key");
                    world.OpenFirewall(def.TerminalNode != null);
                    if (def.ObjectiveAfterTerminal != null) objective.text = def.ObjectiveAfterTerminal;
                    if (def.TerminalNode != null) await Talk(def.TerminalNode, s);
                    if (s.Over) break;
                    prop.Settle();
                    world.SettleFirewalls();
                    break;
            }
        }

        // Beacons light up as the player passes: a rest for the party.
        async Awaitable RestAt(SeaProp beacon, Session s)
        {
            beacon.Use(world.Time);
            world.LightBeacon(beacon);
            game.Audio.Sfx("rise", 0.7f);
            RestoreParty();
            BuildParty();
            if (!beaconTold && def.BeaconNode != null)
            {
                beaconTold = true;
                await Talk(def.BeaconNode, s);
            }
            else NoticeAfterBanner("信标：HP 和 SP 已回满").Forget();
        }

        // A notice held back until the area's banner has gone, so the two don't sit on each other: the beacon at the
        // maintenance room's door lights a step before the room's banner comes up, so it waits a second for one to
        // start before waiting it out. The controls are not held for it.
        async Awaitable NoticeAfterBanner(string text)
        {
            var s = session;
            var from = Time.unscaledTime + 1f;
            await Anim.Until(() => Time.unscaledTime > Mathf.Max(from, bannerUntil) || s.Over);
            if (!s.Over) game.Notice(text);
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

        // At the goal: the area's last lines, then the way it sees the two off (DungeonDef.Departure: the shallows'
        // lighthouse turns on them, the deep's column comes up under them) with the HUD away, then out. Confirm or cancel
        // after the first 0.2 s hurries the departure, as in a cutaway.
        async Awaitable Goal(Session s)
        {
            if (def.GoalNode != null) await Talk(def.GoalNode, s);
            if (s.Over) return;
            var started = Time.unscaledTime;
            var hurried = false;
            departing = true; // till the area is left (Leave)
            await world.Depart(() => hurried || (hurried = HurryAsked(started)), () => s.Over);
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
                game.Audio.Sfx("glitch", 0.5f);
                Call(def.WarningNode, session).Forget();
            }
            if (meter < 100f) return false;
            meter = 100f;
            Act(async s =>
            {
                game.Audio.Sfx("glitch");
                // The racks going out one by one and the sea running upward (Prologue_Deep_Timeout), under the lines.
                world.Blackout(2.5f, () => s.Over).Forget();
                if (def.TimeoutNode != null) await Talk(def.TimeoutNode, s);
                if (!s.Over) await Bounce(s);
            });
            return true;
        }

        // Maintenance running out no longer ends the night (design/prologue-playtest.md 改法 4): DeepSeek drags the
        // party back to the last beacon lit (the way in, before one is), the meter falls back to BounceMeter, and the
        // noise bodies beaten so far gather again; pearls, chests and the puzzles stay as they were.
        async Awaitable Bounce(Session s)
        {
            Bounces++;
            await Flash(Palette.Cold, 0.7f, s);
            if (s.Over) return;
            SeaProp beacon = null;
            foreach (var p in world.Props)
                if (p.Kind == PropKind.Beacon && p.Used) beacon = p;
            StandAt(beacon);
            world.Restore(0.8f).Forget(); // 再睁开眼，我们站在来时那片光里
            foreach (var e in world.Enemies) e.Revive();
            world.SafeUntil = world.Time + 3f;
            meter = BounceMeter;
            travelledAtLastTick = world.Travelled;
            warned = false;
            RestoreParty();
            BuildParty();
            game.Notice($"维护进度退回 {BounceMeter:0}%，打倒的噪声体又聚了起来");
        }

        // The party beside a beacon (the way in, with none), facing it: its light in front of them (来时那片光), not its
        // roof across the lens behind them.
        void StandAt(SeaProp beacon)
        {
            if (beacon == null)
            {
                world.Place(world.Map.Find('S')[0], world.PlayerYaw);
                return;
            }
            // Toward the seabed beside it, but inside its reach (a cell away is out of it), so it can be used again
            // straight off.
            var away = world.Map.World(FloorBeside(beacon.Cell)) - beacon.Position;
            away.y = 0f;
            var at = beacon.Position + away.normalized * Mathf.Min(away.magnitude, beacon.Reach - 0.6f);
            at.y = 0f;
            world.Place(at, Mathf.Atan2(-away.x, -away.z) * Mathf.Rad2Deg);
        }

        // ------------------------------------------------------------------ beacon saves

        // A lit beacon within its reach: the latent space's one place to save (GameRoot.CanSave).
        public bool AtBeacon => world != null && mode != Mode.Off && !departing && !cutting && BeaconInReach() != null;

        SeaProp BeaconInReach() => world?.Props.Find(p => p.Kind == PropKind.Beacon && p.Used && Vector3.Distance(p.Position, world.PlayerPosition) < p.Reach);

        // What a save at the beacon keeps of the maze (GameRoot.SaveTo).
        public DungeonSave Capture()
        {
            var d = new DungeonSave
            {
                area = def.Id, beacon = world.Props.IndexOf(BeaconInReach()), meter = meter,
                warned = warned, beaconTold = beaconTold, spottedTold = spottedTold, firewallTold = firewallTold,
                ambushTold = ambushTold, stuckTold = stuckTold, sodaTold = sodaTold,
            };
            for (var i = 0; i < world.Props.Count; i++)
                if (world.Props[i].Used) d.used.Add(i);
            for (var i = 0; i < world.Enemies.Count; i++)
                if (!world.Enemies[i].Active) d.gone.Add(i);
            foreach (var b in world.Puzzles.Blocks)
            {
                d.blocks.Add(b.Cell.x);
                d.blocks.Add(b.Cell.y);
            }
            foreach (var p in world.Puzzles.Plates) d.plates.Add(p.Facing);
            foreach (var z in visited) d.zones.Add(z.Name);
            return d;
        }

        // The maze as a beacon save left it, all at once: props used, noise bodies beaten gone, the walls the terminal
        // opened open, the blocks and plates where they were (the gate open, the exit up), the party beside the beacon.
        void Resume(DungeonSave d)
        {
            meter = d.meter;
            warned = d.warned;
            beaconTold = d.beaconTold;
            spottedTold = d.spottedTold;
            firewallTold = d.firewallTold;
            ambushTold = d.ambushTold;
            stuckTold = d.stuckTold;
            sodaTold = d.sodaTold;
            foreach (var i in d.used)
            {
                if (i < 0 || i >= world.Props.Count) continue;
                var p = world.Props[i];
                p.Use(-100f);
                if (p.Kind != PropKind.Terminal) continue;
                world.OpenFirewall();
                p.Settle();
                if (def.ObjectiveAfterTerminal != null) objective.text = def.ObjectiveAfterTerminal;
            }
            foreach (var i in d.gone)
                if (i >= 0 && i < world.Enemies.Count) world.Enemies[i].Remove();
            var cells = new List<Vector2Int>();
            for (var i = 0; i + 1 < d.blocks.Count; i += 2) cells.Add(new Vector2Int(d.blocks[i], d.blocks[i + 1]));
            world.Puzzles.Restore(cells, d.plates);
            if (world.Puzzles.Solved) world.OpenGateVisual();
            if (world.Puzzles.HasLight && world.Puzzles.Lit) world.RiseExit();
            foreach (var z in def.Zones)
                if (d.zones.Contains(z.Name)) visited.Add(z);
            StandAt(d.beacon >= 0 && d.beacon < world.Props.Count ? world.Props[d.beacon] : null);
            zone = world.Zone;
            if (zone != null) visited.Add(zone);
            areaZone.text = zone != null ? "· " + zone.Name : "";
        }

        // A seabed cell next to the beacon's own, to stand on.
        Vector2Int FloorBeside(Vector2Int cell)
        {
            foreach (var d in new[] { Vector2Int.down, Vector2Int.up, Vector2Int.left, Vector2Int.right })
                if (world.Map[cell + d] == '.') return cell + d;
            return cell;
        }

        async Awaitable Talk(string node, Session s)
        {
            if (s.Over || story == null) return;
            var previous = mode;
            mode = Mode.Talk;
            Lock(false);
            while (hintRunning && !s.Over) await Awaitable.NextFrameAsync();
            if (!s.Over)
            {
                hintRunning = true;
                await story.RunHint(node, this);
                hintRunning = false;
            }
            Ui.Show(talkBox, false);
            world?.Companion?.SetFace(null);
            if (world != null) world.PlayerSpeaks = world.DeepSeekSpeaks = false;
            if (!s.Over && mode == Mode.Talk) mode = previous == Mode.Explore ? Mode.Busy : previous;
        }

        // A node DeepSeek says from the top right without taking the controls: its lines are read into the
        // nav's queue in a frame or two, then play there while the player keeps going.
        async Awaitable Call(string node, Session s)
        {
            if (node == null || story == null) return;
            while (hintRunning && !s.Over) await Awaitable.NextFrameAsync();
            if (s.Over) return;
            hintRunning = true;
            await story.RunHint(node, nav);
            hintRunning = false;
        }

        async Awaitable Banner(ZoneDef z)
        {
            var serial = ++bannerShown;
            bannerUntil = Time.unscaledTime + 2.5f; // in, locked, held, out
            bannerName.Text = z.Name;
            bannerTag.text = "AREA · " + z.Tag;
            Ui.Show(banner, true);
            game.Audio.Sfx("whoosh", 0.6f);
            // The name comes in with its colours torn apart and locks together, like a signal found.
            var calm = game.Settings.reduceFlash;
            await Anim.Tween(0.32f, t =>
            {
                Ui.Offset(banner, Mathf.Lerp(-900f, 0f, t), 0f);
                Ui.Opacity(banner, t);
                bannerName.Split(Mathf.Lerp(30f, 3f, t), calm ? 0f : 8f * (1f - t));
            }, Ease.OutBack, () => serial != bannerShown);
            await Anim.Tween(0.25f, t => bannerName.Split(3f * (1f - t), calm ? 0f : 4f * (1f - t)), Ease.Linear, () => serial != bannerShown);
            await Anim.Wait(1.65f, () => serial != bannerShown);
            if (serial != bannerShown) return;
            await Anim.Tween(0.28f, t =>
            {
                Ui.Offset(banner, Mathf.Lerp(0f, 260f, t), 0f);
                Ui.Opacity(banner, 1f - t);
                bannerName.Split(24f * t);
            }, Ease.InCubic, () => serial != bannerShown);
            if (serial == bannerShown) Ui.Show(banner, false);
        }

        async Awaitable Flash(Color color, float seconds, Session s)
        {
            Ui.Bg(flash, color);
            var peak = game.Settings.reduceFlash ? 0.25f : 0.8f;
            await Anim.Tween(seconds, t => flash.style.opacity = Mathf.Sin(t * Mathf.PI) * peak, null, () => s.Over);
            flash.style.opacity = 0f;
        }

        // Jumped from behind: the edges of the screen run red under the stamp, up in a tenth of a second, held till the
        // battle's wipe has taken the picture, then gone.
        async Awaitable Alarm(Session s)
        {
            var peak = game.Settings.reduceFlash ? 0.2f : 0.45f;
            await Anim.Tween(0.1f, t => edge.style.opacity = peak * t, Ease.Linear, () => s.Over);
            await Anim.Wait(0.75f);
            await Anim.Tween(0.25f, t => edge.style.opacity = peak * (1f - t), Ease.Linear, () => s.Over);
            edge.style.opacity = 0f;
        }

        // Clear over the middle, white toward the edges and whitest in the corners (stretched over the screen).
        static Texture2D edgeShade;
        static Texture2D EdgeShade
        {
            get
            {
                if (edgeShade != null) return edgeShade;
                const int S = 128;
                edgeShade = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
                var px = new Color32[S * S];
                for (var y = 0; y < S; y++)
                for (var x = 0; x < S; x++)
                {
                    var r = new Vector2(x - S / 2f + 0.5f, y - S / 2f + 0.5f).magnitude / (S / 2f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, r)) * 255));
                }
                edgeShade.SetPixels32(px);
                edgeShade.Apply();
                return edgeShade;
            }
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

            // DeepSeek beside the player takes the face the line is tagged with (until the talk ends, as her
            // portrait keeps it in the story), and her mouth moves while her words type out and her voice runs.
            var deepSeek = line.Speaker == "DeepSeek" ? world?.Companion : null;
            if (deepSeek != null && line.Face != null) deepSeek.SetFace(line.Face);
            if (deepSeek != null) deepSeek.Talking = line.Kind == LineKind.Speech;
            var voice = SkipHeld(line) ? null : line.Voice;
            game.Audio.PlayVoice(voice, line.Speaker, line.VoiceLevel);
            try
            {
                await TypeAndWait(line, text, deepSeek, voice, s, nextRequested);
            }
            finally
            {
                game.Audio.StopVoice(voice);
                if (deepSeek != null) deepSeek.Talking = false;
            }
        }

        async Awaitable TypeAndWait(LineInfo line, string text, SeaCompanion deepSeek, AudioClip voice, Session s, Func<bool> nextRequested)
        {
            if (world != null)
            {
                // The two look at whoever has the line (DungeonWorld.Looks).
                world.PlayerSpeaks = line.Kind == LineKind.Player;
                world.DeepSeekSpeaks = deepSeek != null && line.Kind == LineKind.Speech;
            }

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
            // Her mouth keeps going while her voice does.
            if (deepSeek != null) deepSeek.Talking = game.Audio.Voicing(voice);
            while (!s.Over && !nextRequested() && !advance && !SkipHeld(line))
            {
                if (deepSeek != null && !game.Audio.Voicing(voice)) deepSeek.Talking = false;
                await Awaitable.NextFrameAsync();
            }
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
            if (chromeHidden != (cutting || departing || striking))
            {
                chromeHidden = cutting || departing || striking;
                foreach (var e in chrome) e.style.visibility = chromeHidden ? Visibility.Hidden : Visibility.Visible;
            }
            // Backspace's use where there is one: a plate in reach turns back, else a push can be taken back.
            keysText.text = !exploring ? Keys
                : world.PlateInReach() != null ? Keys + "　Backspace 反转"
                : world.Puzzles.CanUndo ? Keys + "　Backspace 撤回"
                : Keys;
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
            // What the two look at with nothing more pressing (DungeonWorld.Looks): the middle of the thing, not over it.
            world.Interest = promptAt.HasValue ? promptAt.Value - Vector3.up * 1.6f : null;
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
                // Not over one being struck (the stamp says it) or taken off into the battle.
                var onScreen = e.Active && !e.Engaged && e.Root.gameObject.activeSelf && d < 40f && Project(head, out hp);
                var mark = !e.Active ? "" : e.State == NoiseState.Chase ? "!" : e.State == NoiseState.Look ? "?" : e.State == NoiseState.Doze ? "z" : "";
                Ui.Show(alert, onScreen && mark != "");
                Ui.Show(phrase, onScreen && d < 16f && e.State != NoiseState.Chase && Mathf.Repeat(world.Time + e.Phrase.Length * 0.37f, 2.6f) < 1.7f);
                // A new mark pops up out of a small one, overshooting a little.
                if (alert.text != mark) markSince[e] = world.Time;
                var popped = (world.Time - (markSince.TryGetValue(e, out var since) ? since : -1f)) / 0.25f;
                if (popped < 1.5f) Ui.Zoom(alert, popped < 1f ? Mathf.LerpUnclamped(0.3f, 1f, Ease.OutBack(Mathf.Max(0f, popped))) : 1f);
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

        // World point to this screen's 1920x1080 frame (punched: as a strike's camera will hold it, DungeonWorld.ToScreen).
        bool Project(Vector3 worldPoint, out Vector2 local, bool punched = false)
        {
            local = default;
            if (Root.panel == null || !world.ToScreen(worldPoint, out var screen, punched)) return false;
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
