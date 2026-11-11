#if UNITY_EDITOR || SP_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SystemPrompt.Dungeon;
using SystemPrompt.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace SystemPrompt.E2E
{
    // End-to-end run of the prologue in the E2E player build (game/Tools/e2e.sh): a virtual keyboard plays
    // from the title screen to the chapter end through the real input path, a screenshot is saved at every
    // checkpoint, and report.md lists the checkpoints, the state checks and every error in the log.
    // In the latent-space maze it holds the movement keys along a shortest path of cells, goes after the
    // noise bodies and fights them with the first command, pushes the cache blocks and turns the light plates.
    // Screen transitions, a modal opening and the title portraits up close are filmed: a sheet of frames taken
    // a fixed interval apart.
    // Only starts when the player is launched with `-e2e <output dir>`; release builds don't contain it.
    public sealed class PrologueRun : MonoBehaviour
    {
        const float RunTimeout = 1500f;
        const float StepTimeout = 180f; // the tutorial battle alone takes ~130 s on a busy machine
        const string PlayerName = "测试员";
        const string Nickname = "大肥鱼"; // what the run calls DeepSeek on the rooftop: the community's name for her, and a sore one

        sealed class Checkpoint
        {
            public string Id;
            public string Title;
            public bool Required = true;
            public Func<bool> Reached; // null: taken by the scenario itself
            public float Settle;
            public int Frames;     // > 0: a film of this many frames instead of one screenshot
            public float Interval; // seconds between film frames
            public Rect Region = new Rect(0f, 0f, 1f, 1f); // part of the screen filmed, top-left origin, 0..1
            public int FrameWidth = 384, FrameHeight = 216, Columns = 5;
            public float At = -1f;
            public string Shot;
        }

        // A screen transition as it ran, by the wall clock (Seconds) and by the game's own (Game, the frames'
        // Time.unscaledDeltaTime, which the wipe runs on). A wipe counts a frame longer than 0.1 s as 0.1 s
        // (Transitions.Step), so Over, what its frames ran past that, is time pressing Confirm can't hurry.
        sealed class WipeTiming
        {
            public Wipe Style;
            public float At, Seconds, Game, Slowest, Over;
            public int Frames;
            public bool Hurried;
        }

        // The meshes, materials and textures made at run time (Made) when a battle or a dive starts and once the
        // screen it came from is back, the battle field or the area gone.
        sealed class Leftovers
        {
            public string What;
            public float At;
            public int Meshes, Materials, Textures, MeshesAfter = -1, MaterialsAfter = -1, TexturesAfter = -1;
        }

        string outDir;
        Keyboard keyboard;
        GameRoot game;
        VisualElement ui;
        readonly List<Checkpoint> checkpoints = new List<Checkpoint>();
        readonly List<string> failures = new List<string>();
        readonly List<string> passes = new List<string>();
        readonly List<string> logErrors = new List<string>();
        readonly List<string> logWarnings = new List<string>();
        readonly List<WipeTiming> wipeTimings = new List<WipeTiming>();
        readonly List<Leftovers> leftovers = new List<Leftovers>();
        WipeTiming wipe;
        Leftovers battle, dive;
        bool inBattle, inDungeon;
        float started;
        int shotCount;
        int hold;
        bool finished;
        // Every music track heard, and where the game was when each first came up ("title", "story:<scene>", "dungeon", "battle").
        readonly HashSet<string> heard = new HashSet<string>();
        readonly Dictionary<string, string> heardAt = new Dictionary<string, string>();
        bool deepTaught; // the first battle DeepSeek fights in has shown 防御, 白米饭 and the 辣条

        float Now => Time.realtimeSinceStartup - started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-e2e");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("E2E Prologue Run");
            DontDestroyOnLoad(go);
            go.AddComponent<PrologueRun>().outDir = args[i + 1];
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(outDir);
            started = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
            // Keep the virtual keyboard working when the window isn't focused, and keep the run quiet.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("E2E Keyboard");
            // The desktop's real mouse resting over the window would select whatever it hovers (a settings
            // row, a menu item); the run drives the keyboard only.
            foreach (var device in InputSystem.devices.OfType<Pointer>().ToList()) InputSystem.DisableDevice(device);
            AudioListener.volume = 0f;

            while (GameRoot.Instance == null) yield return null;
            game = GameRoot.Instance;
            ui = game.GetComponent<UIDocument>().rootVisualElement;
            DefineCheckpoints();
            StartCoroutine(WatchLoop());
            yield return Scenario();
            Finish();
        }

        void Update()
        {
            // With v-sync on, a window that gets covered is presented at a fraction of the rate while every frame
            // still counts as 1/60 s: the whole game runs in slow motion and the run times out. Paced by the frame
            // cap instead (as the visual lab is), a slowed frame carries its real time. The game's settings put
            // v-sync back whenever they are applied, so this is held every frame.
            if (QualitySettings.vSyncCount != 0 || Application.targetFrameRate != 60)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
            }
            var track = game != null && game.Audio != null ? game.Audio.Music : null;
            if (track != null && heard.Add(track))
                heardAt[track] = game.InBattle ? "battle" : game.InDungeon ? "dungeon" : game.InStory ? "story:" + game.State.scene : "title";
            if (!finished && started > 0 && Now > RunTimeout)
            {
                Fail($"整体超时（{RunTimeout:0} 秒）");
                Finish();
            }
            if (game == null || finished) return;
            TimeWipe();
            battle = CountLeftovers(battle, game.InBattle, ref inBattle, "战斗");
            dive = CountLeftovers(dive, game.InDungeon, ref inDungeon, "潜入");
        }

        void TimeWipe()
        {
            var wipes = game.Transitions;
            if (wipe != null)
            {
                var dt = Time.unscaledDeltaTime;
                wipe.Frames++;
                wipe.Seconds = Now - wipe.At;
                wipe.Game += dt;
                wipe.Slowest = Mathf.Max(wipe.Slowest, dt);
                wipe.Over += Mathf.Max(0f, dt - 0.1f);
                wipe.Hurried |= wipes.Hurried;
                if (!wipes.Busy || wipes.Style != wipe.Style) wipe = null;
            }
            if (wipe == null && wipes.Busy) wipeTimings.Add(wipe = new WipeTiming { Style = wipes.Style, At = Now });
        }

        // A battle or a dive should leave no meshes or materials behind (BattleStage.Drop, DungeonWorld.Drop); a
        // few may be the UI's own (a font's glyph page). Textures are only listed: the story's portraits and the
        // sea's shared pictures are made on the way. `open` is the count waiting for its end; returns it, or null
        // once checked.
        Leftovers CountLeftovers(Leftovers open, bool on, ref bool was, string what)
        {
            if (on && !was)
                leftovers.Add(open = new Leftovers { What = what, At = Now, Meshes = Made<Mesh>(), Materials = Made<Material>(), Textures = Made<Texture>() });
            was = on;
            if (open == null || on || game.Transitions.Busy) return open;
            open.MeshesAfter = Made<Mesh>();
            open.MaterialsAfter = Made<Material>();
            open.TexturesAfter = Made<Texture>();
            Check(open.MeshesAfter - open.Meshes <= 4 && open.MaterialsAfter - open.Materials <= 4,
                $"{what}没有留下网格和材质（网格 {open.Meshes} → {open.MeshesAfter}，材质 {open.Materials} → {open.MaterialsAfter}）");
            return null;
        }

        // Made at run time (a negative instance id) and not kept for the whole run (Parts.Free, FxSurface).
        static int Made<T>() where T : UnityEngine.Object =>
            Resources.FindObjectsOfTypeAll<T>().Count(o => o.GetInstanceID() < 0 && (o.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0);

        // ------------------------------------------------------------------ scenario

        IEnumerator Scenario()
        {
            // 标题
            yield return Until(() => TitleReady, "标题菜单出现");
            yield return Snap("title", "标题画面");
            // The faces up close at half size, 2 s: breathing, head sway, hair and (most likely) a blink.
            yield return Clip("title-live", "标题立绘动态（胶片：呼吸、摆头、头发、眨眼）", 20, 0.1f,
                new Rect(900f / 1920f, 90f / 1080f, 960f / 1920f, 500f / 1080f), 480, 250, 4);
            // Every menu step below counts from 开始游戏, which is only selected when there is no save.
            if (SaveSystem.MostRecent() != null)
            {
                Fail("已有存档：e2e.sh 没清掉上次的数据，菜单位置会错");
                Finish();
                yield break;
            }
            var heads = ui.Q(className: "sp-title").Query(className: "sp-touch__head").ToList();
            if (heads.Count == 3)
            {
                // One after another: each new bubble replaces the last, above all three portraits.
                Click(heads[0]); // ChatGPT
                yield return Wait(0.5f);
                yield return Snap("title-touch-chatgpt", "标题：点 ChatGPT 的头（气泡尾巴指向她）");
                Click(heads[1]); // Claude
                yield return Wait(0.5f);
                yield return Snap("title-touch-claude", "标题：接着点 Claude 的头");
                Click(heads[2]); // DeepSeek
                yield return Wait(0.5f);
                yield return Snap("title-touch", "标题：接着点 DeepSeek 的头");
                var shown = ui.Q(className: "sp-title").Query(className: "sp-touch__bubble").ToList().Count(Visible);
                Check(shown == 1, $"连点三人后只剩最后一个气泡（实际 {shown} 个）");
            }
            else Fail($"标题上应有 3 个可点的立绘，实际 {heads.Count} 个");

            // 设置：开始游戏 → 读取存档 → 设置
            yield return Press(Key.DownArrow);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            yield return Until(() => game.SettingsModal.IsOpen, "设置打开");
            yield return Wait(0.7f);
            yield return Snap("settings-audio", "设置·音频");
            yield return Press(Key.RightArrow); // 文本页
            yield return Press(Key.DownArrow); // 文字速度
            for (var i = 0; i < 3; i++) yield return Press(Key.RightArrow); // 标准 → 瞬间
            yield return Wait(0.8f);
            yield return Snap("settings-text", "设置·文本（文字速度调到「瞬间」）");
            Check(game.Settings.textSpeed == GameSettings.TextSpeedLabels.Length - 1, "文字速度为「瞬间」");
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen, "设置关闭");
            yield return Press(Key.UpArrow);
            yield return Press(Key.UpArrow);

            // 陪伴生登记
            yield return Press(Key.Enter);
            yield return Until(() => game.NewGame.IsOpen, "登记界面打开");
            yield return Wait(0.6f);
            // Fill the name like a form filler: IME composition is outside this test.
            ui.Q<TextField>(className: "sp-newgame__field").value = PlayerName;
            yield return Snap("newgame", "陪伴生登记");
            for (var i = 0; i < 4 && !game.Confirm.IsOpen; i++) yield return Press(Key.Enter);
            yield return Until(() => game.Confirm.IsOpen, "登记确认");
            yield return Wait(0.45f);
            yield return Snap("newgame-confirm", "登记确认");
            yield return Press(Key.Enter);
            yield return Until(() => game.InStory, "进入序章");
            Check(game.State.playerName == PlayerName, $"主角名为「{PlayerName}」");

            // 序章前半，到 Claude 第一次说话时顺路打开手机、资料、菜单、回看，再快速存档
            yield return Pump(() => SpeakerIs("Claude"), "Claude 第一次说话");
            // The label reset at the top of the USS once outranked every label rule and zeroed their spacing.
            // Read while Claude's name plate is on screen: it is laid out only then.
            var role = ui.Q<Label>(className: "sp-nameplate__role").resolvedStyle;
            Check(role.marginLeft > 0 && role.marginTop > 0, "文字的 USS 边距生效（名牌头衔）");
            yield return SideTrips();

            // 一路到教学战斗结束。到战斗前的剧情较长，分段推进，每段各算超时
            yield return Pump(() => game.State.scene == "rooftop", "到达天台");
            yield return Press(Key.F5); // the checkpoint is the start of 天台: Naming loads it to name her twice
            yield return Naming();
            if (finished) yield break;
            yield return Pump(() => game.State.scene == "cache_sea", "进入缓存海");
            yield return Pump(() => Maze.Exploring, "进入缓存海·浅层探索");
            yield return Shallows();
            if (finished) yield break;
            yield return OldSaves();
            if (finished) yield break;
            yield return Pump(() => game.InBattle, "进入教学战斗");
            yield return Pump(() => !game.InBattle, "教学战斗结束");
            var deepSeek = game.State.members.Find(m => m.id == "DeepSeek");
            var player = game.State.members.Find(m => m.id == GameState.PlayerId);
            Check(deepSeek != null && deepSeek.level >= 2, $"DeepSeek 升到 Lv2（实际 Lv{deepSeek?.level}）");
            Check(player != null && player.level >= 2, $"主角升到 Lv2（实际 Lv{player?.level}）");
            Check(game.State.inventory.Any(s => s.id == "white_rice" && s.count > 0), "获得白米饭");
            Check(game.StoryString("$deepseek_calls_player") == "鱼片", "DeepSeek 称呼主角为「鱼片」");

            yield return Pump(() => Maze.Exploring, "进入缓存海·深层探索");
            yield return Deep();
            if (finished) yield break;

            // 深层探索后到章节结束
            yield return Pump(() => StampShown("完"), "序章结束");
            Check(game.State.coops.Any(c => c.character == "DeepSeek" && c.rank == 1), "隐者 Coop Rank 1");
            Check(game.State.abilities.Contains("deep_dive"), "获得能力「深海潜行」");
            Check(game.State.deadlines.Any(d => d.id == "deepseek_evaluation" && d.month == 5 && d.day == 6), "评测日设为 5/6");
            Check(game.State.contacts.Contains("ChatGPT"), "通讯录里有 ChatGPT");
            Check(Seen("cg-dive") && Seen("cg-whale") && Seen("cg-council"),
                $"三张过场 CG 都放出了动态版（潜入 {Seen("cg-dive")}，深渊之鲸 {Seen("cg-whale")}，学生会室 {Seen("cg-council")}）");
            Check(game.Story.Cg == "council_window", $"学生会室的 CG 留到序章结束（实际 {game.Story.Cg ?? "无"}）");
            var tracks = new[] { "title", "story", "sea", "battle", "shadow", "awakening", "results", "deep" };
            Check(tracks.All(heard.Contains), $"八首配乐都放过（没放的：{string.Join("、", tracks.Where(t => !heard.Contains(t)).DefaultIfEmpty("无"))}）");
            Check(heardAt.TryGetValue("shadow", out var shadowAt) && shadowAt == "story:cache_sea", $"影子的曲子在灯塔脚下的剧情里响起（实际 {heardAtOr("shadow")}）");
            Check(heardAt.TryGetValue("awakening", out var wakeAt) && wakeAt == "story:cache_sea", $"觉醒的曲子在剧情里先响、接进教学战斗（实际 {heardAtOr("awakening")}）");
            Check(heardAt.TryGetValue("deep", out var deepAt) && deepAt == "dungeon", $"深层的曲子在深层探索里响起（实际 {heardAtOr("deep")}）");
            Check(heardAt.TryGetValue("results", out var resultsAt) && resultsAt == "battle", $"结算的曲子在战斗结算时响起（实际 {heardAtOr("results")}）");
            Check(Said($"还想被叫{Nickname}"), "直面影子时说还想被叫这个称呼");
            Check(Said($"中午你给我起的「{Nickname}」") && Said("记仇的那种记"), "战后说会记着这个称呼（记仇）");

            // 回到标题，看读档界面
            yield return Pump(() => !game.InStory && Visible(ui.Q(className: "sp-title")), "回到标题");
            yield return Until(() => TitleReady, "标题菜单出现");
            yield return Snap("title-continue", "回到标题（「继续」可用）");
            Check(SaveSystem.MostRecent() != null, "有可继续的存档");
            yield return Press(Key.DownArrow);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            yield return Until(() => game.SaveLoad.IsOpen, "读档界面打开");
            yield return Wait(1.1f);
            yield return Snap("load", "读档界面");
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen, "读档界面关闭");

            // 战斗失败的转场：E2E 不会输，从标题直接走 BattleScreen.Defeat 用的同一条路
            yield return Until(() => TitleReady, "标题菜单出现");
            game.ShowTitle(Wipe.Lost);
            yield return Until(() => game.Transitions.Busy, "掉线转场开始");
            yield return Until(() => TitleReady, "掉线转场后回到标题");

            // 转场能跳过：开始后按确定，剩下的三倍速播完、停顿取消。机器忙时帧率会掉，
            // 所以先完整播一遍关机转场，在同样的负载下比较用时。都按转场自己的时钟 Time.unscaledTime 算：
            // 窗口的帧在游戏之外被拖慢时（见「转场用时与帧时间」），墙上时间比它走得快，
            // 按墙上时间等 0.3 秒再按，可能还在转场开头不收按键的 0.2 秒里
            var t0 = Time.unscaledTime;
            game.ShowTitle(Wipe.Shutdown);
            yield return Until(() => !game.Transitions.Busy, "完整的关机转场结束");
            var full = Time.unscaledTime - t0;
            yield return Until(() => TitleReady, "关机转场后回到标题");
            t0 = Time.unscaledTime;
            game.ShowTitle(Wipe.Shutdown);
            yield return Until(() => Time.unscaledTime - t0 > 0.3f, "关机转场播了 0.3 秒");
            yield return Press(Key.Enter);
            yield return Until(() => !game.Transitions.Busy, "跳过的转场结束");
            var hurried = Time.unscaledTime - t0;
            Check(game.Transitions.Hurried, "关机转场收到了确定键");
            Check(hurried < full * 0.75f, $"按确定后转场加速（游戏时间：关机转场 {hurried:0.00} 秒，不跳过 {full:0.00} 秒）");
            yield return Until(() => TitleReady, "跳过转场后回到标题");
        }

        IEnumerator SideTrips()
        {
            yield return Press(Key.Tab);
            yield return Until(() => game.Phone.IsOpen, "手机打开");
            yield return Wait(0.6f);
            yield return Snap("phone-contacts", "手机·通讯录（新联系人）");
            yield return Press(Key.Enter);
            yield return Until(() => game.Profile.IsOpen, "ChatGPT 资料打开");
            yield return Wait(0.5f);
            var head = ui.Q(className: "sp-profile").Q(className: "sp-touch__head");
            if (head != null) Click(head);
            yield return Wait(0.5f);
            yield return Snap("profile", "ChatGPT 资料（点头）");
            yield return Press(Key.Escape);
            yield return Until(() => !game.Profile.IsOpen, "资料关闭");
            yield return Press(Key.LeftArrow); // 日程
            yield return Wait(0.2f);
            yield return Snap("phone-calendar", "手机·日程");
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen, "手机关闭");

            yield return Press(Key.Escape);
            yield return Until(() => game.Menu.IsOpen, "暂停菜单打开");
            yield return Idle(); // the watcher films the menu opening
            yield return Wait(0.2f);
            yield return Snap("menu", "暂停菜单");
            // 从菜单进回看再退回来。上移两格（循环）落在「回看」，不受「存档」是否可用影响。
            yield return Press(Key.UpArrow);
            yield return Press(Key.UpArrow);
            yield return Press(Key.Enter);
            yield return Until(() => game.Backlog.IsOpen, "菜单进入回看");
            yield return Idle(); // the watcher films the dive
            yield return Wait(0.3f);
            yield return Snap("menu-page", "回看（从菜单进入）");
            yield return Press(Key.Escape);
            yield return Until(() => !game.Backlog.IsOpen && game.Menu.IsOpen, "回看关闭，回到菜单");
            yield return Idle(); // films the pull-back
            yield return Until(() => !game.Menu.Switching, "菜单重新落定");
            // 回看 → 上四格到「存档」（快速存档之前已有检查点，存档可用）
            for (var i = 0; i < 4; i++) yield return Press(Key.UpArrow);
            yield return Press(Key.Enter);
            yield return Until(() => game.SaveLoad.IsOpen, "菜单进入存档");
            yield return Wait(0.8f);
            yield return Snap("menu-save", "存档（从菜单进入）");
            yield return Press(Key.Escape);
            yield return Until(() => !game.SaveLoad.IsOpen && game.Menu.IsOpen, "存档关闭，回到菜单");
            yield return Until(() => !game.Menu.Switching, "菜单再次落定");
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen, "暂停菜单关闭");

            yield return Press(Key.L);
            yield return Until(() => game.Backlog.IsOpen, "回看打开");
            yield return Wait(0.6f);
            yield return Snap("backlog", "回看");
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen, "回看关闭");

            yield return Press(Key.F5);
            yield return Wait(0.3f);
            yield return Snap("quicksave", "快速存档提示");
            Check(SaveSystem.Read(SaveSystem.QuickSlot).Data != null, "快速存档已写入");

            // The corner icons up close with AUTO on (lit green, the icon turns ink); off again well before
            // auto-advance would move on (1.5 s).
            yield return Press(Key.Q);
            yield return Wait(0.25f);
            var keys = ui.Query(className: "sp-quick").ToList();
            Check(keys.Count == 9 && keys[0].ClassListContains("is-on"), $"快捷栏是 9 个图标键，按 Q 后 AUTO 点亮（实际 {keys.Count} 个）");
            yield return Clip("quickbar", "右上角图标（AUTO 开启）", 1, 0.1f,
                new Rect(1200f / 1920f, 0f, 720f / 1920f, 140f / 1080f), 720, 140, 1);
            yield return Press(Key.Q);
            Check(keys.Count > 0 && !keys[0].ClassListContains("is-on"), "再按 Q 关闭自动");
        }

        // ------------------------------------------------------------------ the latent-space maze

        DungeonScreen Maze => game.Dungeon;
        DungeonWorld World => game.Dungeon.World;

        // The expression cells DeepSeek's 3D face shows in the sea (eyes, mouth, blush; Dungeon/SeaFigure.cs).
        Vector4 CompanionFace() => World?.Companion?.FaceCells ?? new Vector4(-9f, -9f, -9f, 0f);

        // 缓存海·浅层: DeepSeek follows but can't fight yet, so the phone's screenshot fights alone. Jump the
        // dozing one at the reef's mouth from behind, west into the coral (its beacon, its patrol), north
        // through the graveyard (its patrol, the draft), the beacon behind the lighthouse, the canyon's
        // patrol, then the maintenance room: eleven pushes put both cache blocks in their sockets, the gate
        // opens, the terminal drops the firewalls, and back through them to the lighthouse.
        IEnumerator Shallows()
        {
            var w = World;
            Check(w.Def.Id == "cache_sea_shallows", $"浅层地图（实际 {w.Def.Id}）");
            Check(Visible(ui.Q(className: "sp-dungeon__map")), "右上角地图显示");
            Check(w.Companion != null, "DeepSeek 跟在主角身边");
            Check(w.Companion?.Root.Find("DeepSeekUniform") != null, "觉醒前的 DeepSeek 是 VRoid 校服模型（鲸鱼兜帽）");
            Check(Maze.Zone?.Name == "气泡礁", $"从气泡礁出发（实际 {Maze.Zone?.Name}）");
            SeaProp BeaconIn(string zone) => w.Props.Find(p => p.Kind == PropKind.Beacon && w.Def.ZoneAt(p.Cell)?.Name == zone);

            Check(w.Pearls.Count == 5 && ui.Q<Label>(className: "sp-dungeon__pearls").text.EndsWith("0/5"), "浅层 5 颗数据珍珠，左上角显示 0/5");
            yield return Pearl(0, "起点旁", "dungeon-pearl");
            if (finished) yield break;
            yield return Jump(Noise('1'), "dungeon-first", "从背后接近礁石出口发呆的噪声体（提示「先制攻击！」）");
            if (finished) yield break;
            Check(Maze.Battles == 1 && Maze.Preemptive == 1, $"第一场战斗是先制攻击（{Maze.Battles} 场，先制 {Maze.Preemptive}）");
            Check(game.Battle.Earned == 39, $"先制奖励：EXP 30 → {game.Battle.Earned}");
            Check(game.State.ItemCount("ice_soda") == 2, $"第一场战斗先制，掉落翻倍：冰镇汽水 ×{game.State.ItemCount("ice_soda")}");
            Check(game.State.party.Count == 1, $"觉醒前主角一个人战斗（队伍 {game.State.party.Count} 人）");

            var coralBeacon = BeaconIn("表情包珊瑚林");
            yield return Walk(coralBeacon.Cell, "沿手机南侧往西，进珊瑚林走到信标", 2.2f);
            yield return Lurk(() => Maze.Exploring && coralBeacon.Used, "珊瑚林的信标点亮");
            yield return Fight(Noise('2'), "和珊瑚林的噪声体战斗");
            Check(Maze.SodaTold, "第一战之后走近巡逻的噪声体，DeepSeek 在右上角说了汽水是冰的");
            yield return Pearl(1, "珊瑚林西南角");
            yield return Fight(Noise('3'), "往北进删帖墓地，和墓地的噪声体战斗", ThrowSoda);
            var me = game.State.Member(game.State.party[0]);
            Check(true, $"墓地的战斗后主角 HP {me.hp}/{SystemPrompt.Battle.CombatStats.ForCharacter(me.id, me.level).MaxHp}");
            yield return Pearl(2, "墓地西北角");

            var draft = w.Props.Find(p => p.Kind == PropKind.Fragment && w.Def.FragmentTitles[p.Index] == "未发送的草稿");
            yield return Walk(draft.Cell, "走到碎片「未发送的草稿」", 2f);
            yield return Press(Key.Enter);
            yield return Pump(() => Maze.Exploring && draft.Used, "读完碎片");
            Check(game.StoryBool("$sea_read_47s"), "读过「四十七秒」的草稿（$sea_read_47s）");
            Check(Seen("dungeon-face"), "DeepSeek 撅嘴的台词让她的 3D 脸撅嘴、脸红（#face:pout）");
            var face = CompanionFace();
            Check(face.y == SeaFigure.None && face.z == SeaFigure.None, $"对话结束后 DeepSeek 的表情复原（嘴 {face.y}，脸红 {face.z}）");

            var ridgeBeacon = BeaconIn("灯塔背面");
            yield return Walk(ridgeBeacon.Cell, "穿过墓地，到灯塔背面的信标", 2.2f);
            yield return Lurk(() => Maze.Exploring && ridgeBeacon.Used, "灯塔背面的信标点亮");
            yield return Pearl(3, "灯塔背面东头");
            yield return Fight(Noise('4'), "下到机柜峡谷，和峡谷的噪声体战斗", SpareTheCaller);
            if (finished) yield break;
            Check(game.Battle.CalledIn >= 1, $"转发型在战斗里召唤了标题党型（{game.Battle.CalledIn} 只）");
            var sodas = game.State.ItemCount("ice_soda");
            var tokens = game.State.tokens;
            yield return Pearl(4, "峡谷支路");
            if (finished) yield break;
            Check(game.State.PearlsIn(w.Def.Id) == 5 && ui.Q<Label>(className: "sp-dungeon__pearls").text.EndsWith("5/5"), "浅层的数据珍珠全部捡齐（5/5）");
            Check(game.State.ItemCount("ice_soda") == sodas + 2 && game.State.tokens == tokens + 200, "捡齐奖励：冰镇汽水 ×2、200 token");

            // The maintenance room: the right-hand block down and round into the left socket, which takes
            // it only from the east; the left-hand block across into the right one.
            var pushes = new[]
            {
                (new Vector2Int(26, 12), Vector2Int.left), (new Vector2Int(24, 11), Vector2Int.up), (new Vector2Int(24, 12), Vector2Int.up),
                (new Vector2Int(25, 14), Vector2Int.left), (new Vector2Int(24, 14), Vector2Int.left),
                (new Vector2Int(20, 11), Vector2Int.up), (new Vector2Int(19, 13), Vector2Int.right), (new Vector2Int(20, 13), Vector2Int.right),
                (new Vector2Int(21, 13), Vector2Int.right), (new Vector2Int(22, 13), Vector2Int.right), (new Vector2Int(24, 12), Vector2Int.up),
            };
            Check(!w.Map.GateOpen && w.Puzzles.Sockets.Count == 2 && w.Puzzles.Blocks.Count == 2, "维护间：两块缓存块、两个插槽，门关着");
            for (var i = 0; i < pushes.Length && !finished; i++)
            {
                yield return Push(pushes[i].Item1, pushes[i].Item2, $"第 {i + 1} 推");
                if (i == 0 && !finished)
                {
                    // Taken back with Backspace, then pushed again.
                    var from = pushes[0].Item1 + pushes[0].Item2;
                    Check(w.Puzzles.CanUndo && Visible(ui.Q(className: "sp-dungeon__keys")) && ui.Q<Label>(className: "sp-dungeon__keys-text").text.EndsWith("Backspace 撤回"), "推过之后，按键提示里有「Backspace 撤回」");
                    yield return Press(Key.Backspace);
                    yield return Wait(0.45f);
                    Check(Maze.Undos == 1 && w.Map.Blocks.Contains(from) && !w.Map.Blocks.Contains(from + pushes[0].Item2), $"Backspace 撤回第 1 推，缓存块回到 {from}");
                    yield return Push(pushes[0].Item1, pushes[0].Item2, "撤回后重推第 1 推");
                }
                if (i == 3) yield return Snap("dungeon-push", "推缓存块（右边一块绕到左边插槽前）");
            }
            if (finished) yield break;
            Check(w.Puzzles.Solved && w.Map.GateOpen && Maze.Pushes == pushes.Length + 1, $"{Maze.Pushes} 推（含撤回后重推的 1 推）后两块缓存块都在插槽里，门打开");
            Check(BeaconIn("维护间").Used, "维护间的信标点亮");

            var terminal = w.Props.Find(p => p.Kind == PropKind.Terminal);
            yield return Walk(terminal.Cell, "穿过打开的门，走到维护终端", 2f);
            yield return Press(Key.Enter);
            yield return Pump(() => Maze.Exploring && w.Map.FirewallOpen, "接入终端");
            Check(w.Map.FirewallOpen, "防火墙全部解除");

            yield return Walk(w.Map.Find('G')[0], "穿过两道防火墙走到灯塔", 0f, () => !game.InDungeon);
            Check(Maze.Battles >= 4, $"浅层战斗 {Maze.Battles} 场（先制 {Maze.Preemptive}，被偷袭 {Maze.Ambushed}）");
            Check(Maze.ZonesVisited >= 7, $"浅层走过 {Maze.ZonesVisited} 个区域");
        }

        // 缓存海·深层: with DeepSeek fighting. Jump the dozing one at the server room's door from behind, east
        // through the long-context corridor (its fragment, the delay patch at the end), into the white-door
        // hall, where four turns of plates walk the lighthouse's light round to the hidden exit.
        IEnumerator Deep()
        {
            var w = World;
            Check(w.Def.Id == "cache_sea_deep", $"深层地图（实际 {w.Def.Id}）");
            Check(Visible(ui.Q(className: "sp-dungeon__meter")) && Visible(ui.Q(className: "sp-dungeon__party")), "维护进度与队伍状态显示");
            Check(w.Companion != null, "DeepSeek 跟在身边");
            Check(w.Companion?.Root.Find("DeepSeek") != null, "觉醒后的 DeepSeek 穿战斗服");
            Check(w.Puzzles.HasLight && !w.Puzzles.Lit && !w.GoalOpen, "出口一开始看不见（光照在白门上）");
            yield return Pearl(0, "裂缝起点旁");
            if (finished) yield break;
            Check(ui.Q<Label>(className: "sp-dungeon__pearls").text.EndsWith("1/5"), "深层的数据珍珠单独计数（1/5）");
            yield return Jump(Noise('1'), "dungeon-sneak", "背后接近发呆的噪声体（提示「先制攻击！」）");
            if (finished) yield break;
            Check(Maze.Preemptive == 1, "先制攻击");
            Check(game.Battle.Earned == 52, $"先制奖励：EXP 40 → {game.Battle.Earned}");
            Check(game.Battle.LiesDoubted >= 1, "辣条打在标题党型的耐性上，假弱点名牌变成「？？？」");

            // The server room's two patrols walk the rows either side of the aisle. Standing on the lower one's lane,
            // it finds the party and calls the other over, and the two are fought as one battle.
            if (Noise('2').Active && Noise('3').Active)
            {
                yield return Walk(new Vector2Int(14, 13), "站到机房过道上，等巡逻的噪声体发现", 0.6f);
                if (finished) yield break;
                if (Noise('2').Active && Noise('3').Active)
                {
                    yield return Lurk(() => !Maze.Exploring || Noise('3').Alerted || Noise('2').Alerted, "巡逻的噪声体发现了主角");
                    if (finished) yield break;
                    var spotter = Noise('2').Alerted && !Noise('3').Alerted ? Noise('2') : Noise('3');
                    var other = spotter == Noise('3') ? Noise('2') : Noise('3');
                    Check(other.Alerted, "被发现的噪声体叫来了另一只");
                    yield return Fight(spotter, "迎击两只一起追来的噪声体", () => ShotInBattle("battle-joined", "两只噪声体合成一场战斗"));
                    if (finished) yield break;
                    Check(game.Battle.FoeKinds.Count >= 3, $"两只噪声体合成一场战斗（{game.Battle.FoeKinds.Count} 只）");
                    Check(!spotter.Active && !other.Active, "两只噪声体都从迷宫里消失");
                }
            }
            var exposed = game.Battle.Exposures;

            // The 冷知识型 patrolling the corridor (beside a 标题党型 telling the same 「弱点：火」) is met on the way to the
            // fragment and the chest; the battle-coldfact watcher pictures it when its battle's menu is up.
            var help = w.Props.Find(p => p.Kind == PropKind.Fragment && w.Def.FragmentTitles[p.Index] == "从上面漂下来的话");
            yield return Walk(help.Cell, "往东进长上下文回廊，走到碎片「从上面漂下来的话」", 2f);
            yield return Pump(() => Maze.Exploring && w.EnemyInReach(2.6f) == null, "碎片旁没有噪声体");
            yield return Press(Key.Enter);
            yield return Pump(() => Maze.Exploring && help.Used, "读完碎片");

            var patch = w.Props.Find(p => p.Kind == PropKind.Chest && w.Def.Chests[p.Index].Delay > 0f);
            yield return Walk(patch.Cell, "沿回廊走到尽头的宝箱", 2f);
            // With a noise body in reach, Enter jumps it instead of opening the chest: fight it first.
            yield return Pump(() => Maze.Exploring && w.EnemyInReach(2.6f) == null, "宝箱旁没有噪声体");
            var before = Maze.Meter;
            yield return Press(Key.Enter);
            yield return Pump(() => Maze.Exploring && patch.Used, "打开宝箱");
            yield return Wait(0.45f); // the camera's 0.25 s turn back from the chest well over
            yield return Snap("dungeon-chest", "打开宝箱（延时补丁）");
            Check(Maze.ChestsOpened == 1 && Maze.Meter < before, $"延时补丁让维护进度下降（{before:0.0}% → {Maze.Meter:0.0}%）");
            if (!Noise('5').Active)
            {
                Check(game.Battle.FoeKinds.Contains("coldfact"), "回廊的战斗里有冷知识型");
                Check(game.Battle.Exposures == exposed, "标题党型的假弱点没有在第二回合自动碎掉");
            }

            yield return Walk(new Vector2Int(19, 4), "从东边进白门大厅", 1.2f);
            if (Noise('6').Active) yield return Fight(Noise('6'), "和大厅里巡逻的噪声体战斗");
            yield return Walk(new Vector2Int(19, 5), "退到大厅东侧看光路", 1.2f);
            yield return Face(w.Map.World(new Vector2Int(14, 4)));
            yield return Snap("dungeon-beam", "白门大厅：光照在白门上");
            // Plates (14,6) | → /, (17,6) \ → /, (17,3) / → \, (11,3) | → \ (one turn back with Backspace instead of
            // three on): north, east, north, west, north.
            var turns = new[] { (new Vector2Int(14, 6), new Vector2Int(14, 5), 1), (new Vector2Int(17, 6), new Vector2Int(18, 6), 2),
                                (new Vector2Int(17, 3), new Vector2Int(18, 3), 2), (new Vector2Int(11, 3), new Vector2Int(10, 3), -1) };
            foreach (var (plate, stand, times) in turns)
            {
                yield return Turn(plate, stand, times);
                if (finished) yield break;
            }
            Check(w.Puzzles.Lit && w.GoalOpen && Maze.Turns == 6, $"转了 {Maze.Turns} 次（最后一块逆时针转 1 次），光照到出口，出口出现");
            // The exit's cutaway and DeepSeek's call-out come only once the light has run round (LightFound): the controls
            // are still free between the last turn and the cut. Enter only while they are taken (Lurk), or it would turn
            // the plate beside the player again.
            yield return Lurk(() => Maze.Exploring && Maze.Nav.Said == "出口出现了！", "出口出现的插入镜头和提示");
            yield return Walk(new Vector2Int(9, 6), "退到大厅西南角看光路", 1.2f);
            yield return Face(w.Map.World(new Vector2Int(14, 3)));
            yield return Snap("dungeon-light", "光绕过大厅照到出口");

            yield return Walk(w.Map.Find('G')[0], "走进出口的光柱（白门在旁边）", 0f, () => !game.InDungeon);
            Check(Seen("dungeon-gate"), "在维护结束前走到出口（白门在旁边）");
            Check(Seen("dungeon-mirror"), "白门照出战斗形态的主角");
            Check(true, $"到出口时维护进度 {Maze.Meter:0}%，80% 警告{(Maze.Warned ? "已" : "未")}触发，弹回 {Maze.Bounces} 次");
            Check(Maze.Battles >= 2, $"深层战斗 {Maze.Battles} 场");
            Check(Maze.ZonesVisited >= 4, $"深层走过 {Maze.ZonesVisited} 个区域");
        }

        SeaNoise Noise(char mark) => World.Enemies.Find(e => e.Spawn.Mark == mark);

        // The commands the tutorial leaves out, in the first preemptive battle DeepSeek fights in: 防御 on the player's turn and
        // DeepSeek eating the 白米饭 the tutorial dropped herself on hers (the watcher films both poses); anyone else
        // whose turn comes up meanwhile attacks. The rest is fought through with the first command.
        IEnumerator GuardAndEat()
        {
            bool guarded = false, ate = false;
            var t0 = Now;
            var quiet = Now; // since a command was last given, or Enter pressed to move things on
            while (!finished && game.InBattle && !(guarded && ate))
            {
                if (Now - t0 > StepTimeout)
                {
                    Fail("先制战斗里等不到主角和 DeepSeek 的回合");
                    yield return Snap("timeout", "超时：防御和道具");
                    Finish();
                    yield break;
                }
                if (hold > 0 || !Visible(ui.Q(className: "sp-battle__menu")))
                {
                    if (hold == 0 && Now - quiet > 3f)
                    {
                        yield return Press(Key.Enter);
                        quiet = Now;
                    }
                    else yield return null;
                    continue;
                }
                var turn = ui.Q<Label>(className: "sp-battle__banner-text").text;
                var attack = turn == "我的回合" ? "攻击（截图）" : "攻击";
                var want = !guarded && turn == "我的回合" ? "防御" : !ate && turn == "DeepSeek 的回合" ? "道具" : attack;
                for (var i = 0; i < 6 && !CommandSelected(want); i++) yield return Press(Key.DownArrow);
                if (!CommandSelected(want))
                {
                    Fail($"{turn}的指令里选不到「{want}」");
                    want = attack;
                    for (var i = 0; i < 6 && !CommandSelected(want); i++) yield return Press(Key.UpArrow);
                }
                yield return Press(Key.Enter);
                if (want == "防御") guarded = true;
                if (want == "道具")
                {
                    yield return Wait(0.3f);
                    Check(Visible(ui.Q(className: "sp-battle__items")), "道具列表打开");
                    if (!ate) yield return Snap("battle-items", "道具列表在左栏，当前队员露在它右边");
                    for (var i = 0; i < 6 && !ItemSelected("白米饭"); i++) yield return Press(Key.DownArrow);
                    Check(ItemSelected("白米饭"), "道具列表里选中白米饭");
                    yield return Press(Key.Enter);
                    yield return Wait(0.3f);
                    yield return Press(Key.DownArrow); // for DeepSeek herself, who eats it (+80)
                    yield return Press(Key.Enter);
                    ate = true;
                }
                quiet = Now;
                yield return Wait(0.4f);
            }
            Check(guarded, "先制战斗里主角选了防御");
            Check(ate, "先制战斗里 DeepSeek 吃了白米饭");
        }

        // The canyon's 转发型 calls a 标题党型 in on its first turn — unless the screenshot crits the 标题党型 beside
        // it away, One More takes most of the 转发型 and the preemptive round finishes it before it has had a turn.
        // So until it has called, the player shoots only the 标题党型 and guards once that one is gone: the check
        // after the battle doesn't hang on that luck, and the fight is the one fought straight through (guarding
        // from the first turn left the 标题党型 standing and the player alone against three).
        IEnumerator SpareTheCaller()
        {
            var called = game.Battle.CalledIn;
            var t0 = Now;
            var quiet = Now;
            while (!finished && game.InBattle && game.Battle.CalledIn == called)
            {
                if (Now - t0 > StepTimeout)
                {
                    Fail("峡谷的战斗里等不到转发型召唤");
                    yield return Snap("timeout", "超时：等转发型召唤");
                    Finish();
                    yield break;
                }
                if (hold > 0 || !Visible(ui.Q(className: "sp-battle__menu")))
                {
                    if (hold == 0 && Now - quiet > 3f)
                    {
                        yield return Press(Key.Enter);
                        quiet = Now;
                    }
                    else yield return null;
                    continue;
                }
                var shoot = game.Battle.FoeStands("clickbait");
                var want = shoot ? "攻击（截图）" : "防御";
                for (var i = 0; i < 6 && !CommandSelected(want); i++) yield return Press(shoot ? Key.UpArrow : Key.DownArrow);
                yield return Press(Key.Enter);
                if (shoot)
                {
                    yield return Wait(0.3f);
                    yield return Press(Key.Enter); // the cursor starts on the first one standing, the 标题党型
                }
                quiet = Now;
                yield return Wait(0.4f);
            }
        }

        // Moves whatever is on screen along: a talk or a results screen with Enter; a battle's command menu with the
        // party's one tactic once DeepSeek fights, after Advance picks it. Before her the shallows are the screenshot alone.
        IEnumerator Advance()
        {
            if (game.InBattle && Visible(ui.Q(className: "sp-battle__menu"))) yield return SmartCommand();
            else yield return Press(Key.Enter);
        }

        // 白米饭 for whoever is under 40% HP, 深海潜流 from DeepSeek while two or more noise bodies stand, else the attack.
        IEnumerator SmartCommand()
        {
            var turn = ui.Q<Label>(className: "sp-battle__banner-text").text;
            var mine = turn == "我的回合";
            var hers = turn == "DeepSeek 的回合";
            var id = mine ? game.State.party[0] : "DeepSeek";
            var v = game.Battle.Vital(id);
            if ((mine || hers) && v.MaxHp > 0 && v.Hp * 100 < v.MaxHp * 40 && game.State.ItemCount("white_rice") > 0)
            {
                for (var i = 0; i < 6 && !CommandSelected("道具"); i++) yield return Press(Key.DownArrow);
                if (CommandSelected("道具"))
                {
                    yield return Press(Key.Enter);
                    yield return Wait(0.3f);
                    for (var i = 0; i < 6 && !ItemSelected("白米饭"); i++) yield return Press(Key.DownArrow);
                    yield return Press(Key.Enter);
                    yield return Wait(0.3f);
                    if (hers) yield return Press(Key.DownArrow); // the party list starts at the player
                    yield return Press(Key.Enter);
                    yield return Wait(0.4f);
                    yield break;
                }
            }
            if (hers && game.Battle.FoesStanding >= 2 && v.Sp >= 8)
            {
                for (var i = 0; i < 6 && !CommandSelected("技能"); i++) yield return Press(Key.DownArrow);
                if (CommandSelected("技能"))
                {
                    yield return Press(Key.Enter);
                    yield return Wait(0.3f);
                    for (var i = 0; i < 6 && !SkillSelected("深海潜流"); i++) yield return Press(Key.DownArrow);
                    yield return Press(Key.Enter);
                    yield return Wait(0.3f);
                    yield return Press(Key.Enter);
                    yield return Wait(0.4f);
                    yield break;
                }
            }
            for (var i = 0; i < 6 && !CommandSelected("攻击") && !CommandSelected("攻击（截图）"); i++) yield return Press(Key.UpArrow);
            yield return Press(Key.Enter);
            yield return Wait(0.3f);
        }

        bool SkillSelected(string name) =>
            ui.Q(className: "sp-battle__skills").Query(className: "sp-skillrow").ToList().Any(r => r.ClassListContains("is-selected") && r.Q<Label>(className: "sp-skillrow__name")?.text == name);

        bool ItemSelected(string name) =>
            ui.Q(className: "sp-battle__items").Query(className: "sp-skillrow").ToList().Any(r => r.ClassListContains("is-selected") && r.Q<Label>(className: "sp-skillrow__name")?.text == name);

        // The player's first turn in the graveyard's battle: the 冰镇汽水 the first battle dropped, thrown at a
        // 标题党型. The cursor's forecast stays blank over its fake 「弱点：火」, the hit is on its true weakness,
        // and that gives the fake tag away on every plate.
        // A picture of the field once the first command menu is up; the fight then goes on with the first command.
        IEnumerator ShotInBattle(string shot, string title)
        {
            var t0 = Now;
            while (!finished && game.InBattle && !Visible(ui.Q(className: "sp-battle__menu")) && Now - t0 < StepTimeout) yield return null;
            if (!game.InBattle) yield break;
            yield return Wait(0.4f);
            yield return Snap(shot, title);
        }

        IEnumerator ThrowSoda()
        {
            var t0 = Now;
            var quiet = Now;
            while (!finished && game.InBattle && Now - t0 < StepTimeout)
            {
                if (hold > 0 || !Visible(ui.Q(className: "sp-battle__menu")))
                {
                    if (hold == 0 && Now - quiet > 3f)
                    {
                        yield return Press(Key.Enter);
                        quiet = Now;
                    }
                    else yield return null;
                    continue;
                }
                for (var i = 0; i < 6 && !CommandSelected("道具"); i++) yield return Press(Key.DownArrow);
                if (!CommandSelected("道具"))
                {
                    Fail("指令里选不到「道具」");
                    yield break;
                }
                yield return Press(Key.Enter);
                yield return Wait(0.3f);
                for (var i = 0; i < 6 && !ItemSelected("冰镇汽水"); i++) yield return Press(Key.DownArrow);
                Check(ItemSelected("冰镇汽水"), "道具列表里有冰镇汽水");
                yield return Press(Key.Enter);
                yield return Wait(0.5f);
                var forecast = ui.Query<Label>(className: "sp-enemy__forecast").ToList().Where(Visible).Select(l => l.text).FirstOrDefault();
                Check(forecast == null, $"假弱点还没识破时，瞄准标题党型不显示 WEAK（{forecast ?? "空"}）");
                yield return Snap("battle-aim-item", "用冰镇汽水瞄准标题党型");
                var sodasBefore = game.State.ItemCount("ice_soda");
                yield return Press(Key.Enter);
                var thrown = Now;
                while (!finished && game.InBattle && game.State.ItemCount("ice_soda") == sodasBefore && Now - thrown < 10f) yield return null;
                Check(game.State.ItemCount("ice_soda") == sodasBefore - 1, $"扔出了冰镇汽水（{sodasBefore} → {game.State.ItemCount("ice_soda")}）");
                var exposed = Now;
                while (!finished && game.InBattle && Now - exposed < 15f && !ui.Query<Label>(className: "sp-enemy__weak-text").ToList().Any(l => Visible(l) && l.text.StartsWith("弱点：冰"))) yield return null;
                // A throw that misses (5% of them) shows nothing of the weakness, and outside the tutorial no second turn
                // gives the tag away either: while there is a soda left, the next one is thrown at the next turn.
                var shown = ui.Query<Label>(className: "sp-enemy__weak-text").ToList().Any(l => l.text.StartsWith("弱点：冰"));
                if (!shown && game.InBattle && game.State.ItemCount("ice_soda") > 0)
                {
                    Debug.Log("[E2E] 冰镇汽水没扔中（MISS），下一回合再扔一瓶");
                    quiet = Now;
                    continue;
                }
                Check(shown, "标题党型的假弱点碎掉，露出「弱点：冰」");
                yield break;
            }
        }

        // The player's next turn in the battle GuardAndEat played: the 辣条 the shallows' pair dropped, thrown
        // at the noise body (the watcher films it flying alight and bursting into flame). DeepSeek attacks if
        // her turn comes first.
        IEnumerator ThrowStrip()
        {
            Check(game.State.ItemCount("spicy_strip") > 0, $"背包里有浅层掉落的辣条（{game.State.ItemCount("spicy_strip")}）");
            if (game.State.ItemCount("spicy_strip") == 0) yield break;
            var t0 = Now;
            var quiet = Now;
            while (!finished && game.InBattle && Now - t0 < StepTimeout)
            {
                if (hold > 0 || !Visible(ui.Q(className: "sp-battle__menu")))
                {
                    if (hold == 0 && Now - quiet > 3f)
                    {
                        yield return Press(Key.Enter);
                        quiet = Now;
                    }
                    else yield return null;
                    continue;
                }
                var mine = ui.Q<Label>(className: "sp-battle__banner-text").text == "我的回合";
                var want = mine ? "道具" : "攻击";
                for (var i = 0; i < 6 && !CommandSelected(want); i++) yield return Press(Key.DownArrow);
                if (!CommandSelected(want))
                {
                    Fail($"指令里选不到「{want}」");
                    yield break;
                }
                yield return Press(Key.Enter);
                quiet = Now;
                if (!mine)
                {
                    yield return Wait(0.4f);
                    continue;
                }
                yield return Wait(0.3f);
                for (var i = 0; i < 6 && !ItemSelected("辣条"); i++) yield return Press(Key.DownArrow);
                Check(ItemSelected("辣条"), "道具列表里选中辣条");
                // Relative: there are two when every shallows battle was preemptive and doubled the drop.
                var stripsBefore = game.State.ItemCount("spicy_strip");
                yield return Press(Key.Enter);
                yield return Wait(0.5f);
                yield return Press(Key.Enter);
                var thrown = Now;
                while (!finished && game.InBattle && game.State.ItemCount("spicy_strip") == stripsBefore && Now - thrown < 10f) yield return null;
                Check(game.State.ItemCount("spicy_strip") == stripsBefore - 1, $"扔出了辣条（{stripsBefore} → {game.State.ItemCount("spicy_strip")}）");
                yield break;
            }
            Check(false, "先制战斗结束前轮到主角扔辣条");
        }

        bool CommandSelected(string label) =>
            ui.Query(className: "sp-command").ToList().Any(c => c.ClassListContains("is-selected") && c.Q<Label>(className: "sp-command__text")?.text == label);

        // Creeps up on a dozing one from behind and jumps it: a preemptive battle, fought through.
        IEnumerator Jump(SeaNoise dozing, string shot, string title)
        {
            var w = World;
            Check(dozing.State == NoiseState.Doze && Vector3.Dot(dozing.Forward, w.PlayerPosition - dozing.Position) < 0f, "发呆的噪声体背对起点");
            // Let go 2.85 m out: braking (a third of a metre from a run) carries the avatar to about 2.5 m, inside the 2.6 m reach and outside
            // the 1.8 m where even a dozing one notices. The keys only steer in eight directions, so coming
            // in at a slant it can stop just short; then it creeps on in single-frame taps.
            var t0 = Now;
            while (!finished && Vector3.Distance(w.PlayerPosition, dozing.Position) > 2.85f && Now - t0 < StepTimeout)
            {
                if (!Maze.Exploring)
                {
                    Release();
                    yield return Advance();
                    continue;
                }
                var path = w.Map.Path(w.PlayerCell, w.Map.CellAt(dozing.Position));
                Steer(path.Count > 2 ? w.Map.World(path[1]) : dozing.Position, false);
                yield return null;
            }
            Release();
            yield return Wait(0.6f);
            t0 = Now;
            while (!finished && w.EnemyInReach(2.6f) != dozing && Vector3.Distance(w.PlayerPosition, dozing.Position) > 2.1f && Now - t0 < 4f)
            {
                Steer(dozing.Position, false);
                yield return null;
                Release();
                yield return Wait(0.15f);
            }
            Check(dozing.State == NoiseState.Doze && w.EnemyInReach(2.6f) == dozing,
                $"走到背后没被发现（{dozing.State}，距离 {Vector3.Distance(w.PlayerPosition, dozing.Position):0.00} 米）");
            yield return Snap(shot, title);
            yield return Press(Key.Enter);
            yield return Pump(() => game.InBattle, "先制攻击进入战斗");
            // Both, once DeepSeek fights, and then the 辣条 the shallows' pair dropped.
            if (game.State.party.Count > 1 && !deepTaught)
            {
                deepTaught = true;
                yield return GuardAndEat();
                yield return ThrowStrip();
            }
            yield return Pump(() => !game.InBattle && Maze.Exploring, "先制战斗结束，回到迷宫");
            Check(!dozing.Active, "打倒的噪声体从迷宫里消失");
        }

        // Goes after a patrolling one and attacks it as soon as it is in reach; if it notices first and
        // runs into the avatar, that starts the battle too. Fought through with the first command.
        IEnumerator Fight(SeaNoise enemy, string what, Func<IEnumerator> inBattle = null)
        {
            var driven = 0f;
            var last = Now;
            while (!finished && (enemy.Active || !Maze.Exploring))
            {
                if (StampShown("掉线……"))
                {
                    Release();
                    Fail("战斗失败（掉线），回到标题");
                    yield return Snap("lost", "战斗失败");
                    Finish();
                    yield break;
                }
                if (hold == 0 && Maze.Exploring) driven += Now - last;
                last = Now;
                if (driven > StepTimeout)
                {
                    Release();
                    Fail($"「{what}」超时（{StepTimeout:0} 秒）");
                    yield return Snap("timeout", $"超时：{what}");
                    Finish();
                    yield break;
                }
                if (!Maze.Exploring)
                {
                    Release();
                    if (inBattle != null && game.InBattle)
                    {
                        var step = inBattle;
                        inBattle = null;
                        yield return step();
                        continue;
                    }
                    if (hold == 0) yield return Advance();
                    else yield return null;
                    continue;
                }
                var w = World;
                if (w.EnemyInReach(2.6f) == enemy)
                {
                    Release();
                    yield return Press(Key.Enter);
                    continue;
                }
                var path = w.Map.Path(w.PlayerCell, w.Map.CellAt(enemy.Position));
                var far = Vector3.Distance(w.PlayerPosition, enemy.Position);
                Steer(path.Count > 2 ? w.Map.World(path[1]) : enemy.Position, far > 10f);
                yield return null;
            }
            Release();
            if (!finished) passes.Add($"{Now:0.0}s　{what}");
        }

        // Walks into the area's ith 数据珍珠 (with a picture of it from a few metres first, when shot is given).
        IEnumerator Pearl(int i, string where, string shot = null)
        {
            var w = World;
            var pearl = w.Pearls[i];
            var taken = Maze.PearlsTaken;
            if (shot != null)
            {
                yield return Walk(pearl.Cell, $"走近{where}的数据珍珠", 5f);
                Release();
                yield return Face(pearl.Position);
                yield return Snap(shot, $"数据珍珠（{where}）");
            }
            yield return Walk(pearl.Cell, $"捡{where}的数据珍珠", 0.5f, () => pearl.Taken);
            Release();
            Check(pearl.Taken && Maze.PearlsTaken == taken + 1, $"捡到{where}的数据珍珠（这次潜入 {Maze.PearlsTaken} 颗）");
        }

        // Stands in `from`, leans on the block in `direction` and pushes it a cell on.
        IEnumerator Push(Vector2Int from, Vector2Int direction, string what)
        {
            var w = World;
            var at = from + direction;
            if (!w.Map.Blocks.Contains(at))
            {
                Fail($"{what}：{at} 没有缓存块（缓存块在 {string.Join(" ", w.Map.Blocks)}）");
                Finish();
                yield break;
            }
            yield return Walk(from, $"{what}：走到 {from}", 1.2f);
            yield return Lean(from, w.Map.World(at), 2.9f, what);
            if (finished) yield break;
            var block = w.BlockInReach(out var d);
            Check(block != null && d == direction, $"{what}：站在 {from} 推得到 {at} 的缓存块（方向 {d}）");
            var pushes = Maze.Pushes;
            yield return Press(Key.Enter);
            yield return Wait(0.45f);
            Check(Maze.Pushes == pushes + 1 && w.Map.Blocks.Contains(at + direction), $"{what}：缓存块 {at} → {at + direction}");
        }

        // Stands by a light plate and turns it `times` eighths of a turn: clockwise with Enter, or back with
        // Backspace for negative times.
        IEnumerator Turn(Vector2Int plate, Vector2Int stand, int times)
        {
            var w = World;
            var what = $"转动 {plate} 的反射板";
            yield return Walk(stand, $"{what}：走到 {stand}", 1.2f);
            yield return Lean(stand, w.Map.World(plate), 3f, what);
            if (finished) yield break;
            for (var i = 0; i < Mathf.Abs(times) && !finished; i++)
            {
                if (!Maze.Exploring) yield return Pump(() => Maze.Exploring, "出口出现的对话");
                var turns = Maze.Turns;
                if (times < 0) Check(ui.Q<Label>(className: "sp-dungeon__keys-text").text.EndsWith("Backspace 反转"), "站在反射板旁，按键提示里有「Backspace 反转」");
                yield return Press(times < 0 ? Key.Backspace : Key.Enter);
                yield return Wait(0.45f);
                Check(Maze.Turns == turns + 1, $"{what}（第 {i + 1} 次）");
            }
        }

        // Walks on from the middle of `cell` toward `target` until within `metres` of it, staying in the
        // cell: the reach of blocks and plates is shorter than a cell.
        IEnumerator Lean(Vector2Int cell, Vector3 target, float metres, string what)
        {
            var w = World;
            var t0 = Now;
            while (!finished && (w.PlayerCell != cell || Vector3.Distance(Flat(w.PlayerPosition), Flat(target)) > metres))
            {
                if (Now - t0 > 6f)
                {
                    Release();
                    Fail($"{what}：靠不到 {cell} 的那一侧（在 {w.PlayerCell}，距离 {Vector3.Distance(Flat(w.PlayerPosition), Flat(target)):0.00} 米）");
                    yield return Snap("timeout", $"靠不过去：{what}");
                    Finish();
                    yield break;
                }
                if (!Maze.Exploring)
                {
                    Release();
                    yield return Press(Key.Enter);
                    continue;
                }
                Steer(w.PlayerCell != cell ? w.Map.World(cell) : target, false);
                yield return null;
            }
            Release();
            yield return Wait(0.25f);
        }

        // Swings the camera with Q/E until it looks at `target`, for a screenshot of what is there.
        IEnumerator Face(Vector3 target)
        {
            var t0 = Now;
            while (!finished && Now - t0 < 4f)
            {
                var w = World;
                var yaw = w.CameraYaw * Mathf.Deg2Rad;
                var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var off = Vector3.SignedAngle(forward, Flat(target - w.PlayerPosition), Vector3.up);
                if (Mathf.Abs(off) < 10f) break;
                keyboard.MakeCurrent();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(off > 0f ? Key.E : Key.Q));
                yield return null;
            }
            Release();
            yield return Wait(0.4f);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Walks the shortest path of cells to `goal` until within `near` metres of its centre (or `done`).
        // Lines and battles on the way are pressed through like Pump, and the time they hold the controls
        // does not count toward the timeout.
        IEnumerator Walk(Vector2Int goal, string what, float near, Func<bool> done = null)
        {
            var driven = 0f;
            var last = Now;
            while (!finished)
            {
                if (StampShown("掉线……")) // lost: the title's Enter would load the last checkpoint and replay
                {
                    Release();
                    Fail("战斗失败（掉线），回到标题");
                    yield return Snap("lost", "战斗失败");
                    Finish();
                    yield break;
                }
                if (hold == 0 && Maze.Exploring) driven += Now - last;
                last = Now;
                if (done != null && done()) break;
                if (!game.InDungeon && done == null) break;
                if (driven > StepTimeout)
                {
                    Release();
                    Fail($"「{what}」超时（{StepTimeout:0} 秒）");
                    yield return Snap("timeout", $"超时：{what}");
                    Finish();
                    yield break;
                }
                if (!Maze.Exploring)
                {
                    Release();
                    if (hold == 0) yield return Advance();
                    else yield return null;
                    continue;
                }
                var w = World;
                var to = w.Map.World(goal) - w.PlayerPosition;
                to.y = 0f;
                if (to.magnitude < near) break;
                var path = w.Map.Path(w.PlayerCell, goal);
                if (path.Count == 0)
                {
                    Release();
                    Fail($"「{what}」找不到路：{w.PlayerCell} → {goal}");
                    yield break;
                }
                Steer(path.Count > 1 ? w.Map.World(path[1]) : w.Map.World(goal), true);
                yield return null;
            }
            Release();
            if (!finished) passes.Add($"{Now:0.0}s　{what}");
        }

        // Holds the movement keys toward a point, relative to where the camera looks; Q/E swing the camera
        // round when the way is off to the side, so the screenshots look where the avatar runs.
        void Steer(Vector3 target, bool sprint)
        {
            var w = World;
            var to = target - w.PlayerPosition;
            to.y = 0f;
            var keys = new List<Key>();
            if (to.magnitude > 0.05f)
            {
                var d = to.normalized;
                var yaw = w.CameraYaw * Mathf.Deg2Rad;
                var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
                var f = Vector3.Dot(d, forward);
                var r = Vector3.Dot(d, right);
                if (f > 0.38f) keys.Add(Key.W);
                else if (f < -0.38f) keys.Add(Key.S);
                if (r > 0.38f) keys.Add(Key.D);
                else if (r < -0.38f) keys.Add(Key.A);
                var off = Vector3.SignedAngle(forward, d, Vector3.up);
                if (off > 50f) keys.Add(Key.E);
                else if (off < -50f) keys.Add(Key.Q);
                if (sprint) keys.Add(Key.LeftShift);
            }
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
        }

        void Release()
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }

        void DefineCheckpoints()
        {
            // Films first: the watcher takes the first checkpoint that is due, and a transition doesn't wait.
            var wipes = game.Transitions;
            Film("wipe-boot", "转场：启动（雪花屏里解码出标题）", () => wipes.Busy && wipes.Style == Wipe.Boot, 12, 0.09f);
            Film("wipe-newgame", "转场：开始游戏（斜切，旧画面撕裂、电路板封面）", () => wipes.Busy && wipes.Style == Wipe.Slash, 10, 0.1f);
            Film("wipe-location", "转场：换场景（地点卡，目的地从马赛克里解码）", () => wipes.Busy && wipes.Style == Wipe.Location, 14, 0.12f);
            Film("wipe-battle", "转场：进入战斗（缩放模糊、碎玻璃和光带）", () => wipes.Busy && wipes.Style == Wipe.Battle, 14, 0.12f);
            Film("wipe-dive", "转场：潜入潜空间（像素块碎开、数据海隧道、DIVE）", () => wipes.Busy && wipes.Style == Wipe.Dive, 16, 0.12f);
            Film("wipe-surface", "转场：离开潜空间（绿屏上卷、终端日志、SURFACE）", () => wipes.Busy && wipes.Style == Wipe.Surface, 15, 0.12f);
            Film("wipe-shutdown", "转场：序章结束回标题（CRT 关机、解码）", () => wipes.Busy && wipes.Style == Wipe.Shutdown, 16, 0.12f);
            Film("wipe-lost", "转场：战斗失败回标题（SIGNAL LOST、CRT 关机、解码）", () => wipes.Busy && wipes.Style == Wipe.Lost, 24, 0.1f);
            Film("wipe-return", "转场：战斗结束回剧情", () => Seen("battle-results") && wipes.Busy && wipes.Style == Wipe.Slash, 10, 0.1f);
            Film("menu-open", "暂停菜单展开", () => game.Menu.IsOpen, 10, 0.06f);
            Film("menu-dive", "菜单进入回看（俯冲、扫屏）", () => game.Menu.Switching, 12, 0.05f);
            Film("menu-surface", "回看退回菜单", () => Seen("menu-page") && game.Menu.Switching, 12, 0.05f);
            Film("settings-open", "设置打开（CRT 开机）", () => game.SettingsModal.IsOpen, 10, 0.04f);
            Film("newgame-open", "登记界面打开（背后的画面色差撕裂）", () => game.NewGame.IsOpen, 8, 0.05f);
            Film("save-open", "存档打开（快门）", () => game.SaveLoad.IsOpen, 10, 0.045f);
            Film("results-open", "战斗结算演出", () => Exists("sp-results"), 12, 0.15f);

            var box = ui.Q(className: "sp-box");
            Watch("calendar", "日期卡", () => Exists("fx-calendar"), 0.35f);
            Watch("narration", "旁白", () => Visible(box) && box.ClassListContains("is-narration"), 0.3f);
            Watch("speech", "ChatGPT 说话", () => SpeakerIs("ChatGPT"), 0.3f);
            Watch("options", "选项", () => ui.Query(className: "sp-option").ToList().Count >= 2, 0.5f);
            Watch("toast", "系统提示", () => Visible(ui.Q(className: "sp-toast")), 0.3f);
            Watch("inner", "内心独白", () => Visible(box) && box.ClassListContains("is-inner"), 0.3f);
            Watch("thinking", "DeepSeek 思考框", () => Visible(ui.Q(className: "sp-think")) && ui.Q<Label>(className: "sp-think__text").text.Length > 0, 0.3f);
            // Early in the line, while a Live2D portrait still has its mouth moving.
            Watch("deepseek-speech", "DeepSeek 说话", () => SpeakerIs("DeepSeek"), 0.12f);
            Watch("whisper", "小声说话", () => Visible(box) && box.ClassListContains("is-whisper"), 0.3f);
            Watch("phone-screen", "手机屏幕（维护时刻）", () => Visible(ui.Q(className: "sp-phone-screen")), 0.4f);
            // The cutscene pictures (UI/Story/StoryFx.cs ShowCg), each filmed from a few frames into its clip to its hold.
            Film("cg-dive", "过场 CG：潜入（房间碎成方块，手伸向手机）", () => game.Story.Cg == "dive" && game.Story.CgFrame >= 4, 8, 0.9f, null, 480, 270, 4);
            Film("glitch-swap", "房间被删除（方块之间的房间色差撕裂、变成马赛克）", () => Exists("fx-blocks"), 8, 0.18f);
            Watch("shadow", "影子 DeepSeek", () => Visible(box) && box.ClassListContains("is-shadow"), 0.3f);
            Film("cg-whale", "过场 CG：觉醒「深渊之鲸」（影子化成光点，光之鲸升起）", () => game.Story.Cg == "abyss_whale" && game.Story.CgFrame >= 4, 8, 0.9f, null, 480, 270, 4);
            Watch("effort-max", "思考强度 Max", () => StampShown("强度 Max"), 0.25f);
            Watch("dungeon-explore", "潜空间探索：缓存海·浅层", () => Maze.Exploring && World.Def.Id == "cache_sea_shallows", 1.2f);
            Watch("dungeon-talk", "探索中的对话框", () => Maze.Talking && Visible(ui.Q(className: "sp-dungeon__talk")), 0.4f);
            // Her first #face:pout (「鱼片，好厉害。……我只会躲。」 after the first win): half-shut eyes, a pout and a
            // blush on her 3D face. The camera is behind her then, so this checks her material, not the picture.
            Watch("dungeon-face", "潜空间里 DeepSeek 的 3D 脸跟着台词换表情（#face:pout）", () => Maze.Talking
                && CompanionFace() == new Vector4(SeaFigure.EyesHalf, SeaFigure.MouthPout, SeaFigure.Blush, 0f), 0.2f);
            Watch("dungeon-prompt", "调查提示", () => Maze.Exploring && Visible(ui.Q(className: "sp-dungeon__prompt")), 0.3f);
            Watch("dungeon-alert", "噪声体发现主角（!）", () => Maze.Exploring && World.Enemies.Any(e => e.Alerted), 0.25f, false);
            Watch("dungeon-ambush", "被噪声体偷袭", () => StampShown("被偷袭！"), 0.15f, false);
            Watch("dungeon-zone", "进入新区域（区域名横幅）", () => Visible(ui.Q(className: "sp-dungeon__zone")) && ui.Q(className: "sp-dungeon__zone").resolvedStyle.opacity > 0.95f, 0.15f);
            Watch("dungeon-zone-maintenance", "维护间（缓存块与插槽）", () => Maze.Talking && Maze.Zone?.Name == "维护间", 0.4f);
            // Called out by DeepSeek from the top right (UI/Dungeon/NavCall.cs) while the player keeps going.
            Watch("dungeon-gate-open", "缓存块推进插槽，DeepSeek 在右上角播报「门开了！」，不打断探索", () => Maze.Nav.Said == "门开了！" && Maze.Exploring, 0.2f);
            Watch("dungeon-light-found", "光照到出口，DeepSeek 在右上角播报「出口出现了！」", () => Maze.Nav.Said == "出口出现了！" && Maze.Exploring, 0.2f);
            Watch("dungeon-deep", "潜空间探索：缓存海·深层（维护进度、队伍）", () => Maze.Exploring && World.Def.Id == "cache_sea_deep", 1.2f);
            Watch("dungeon-preemptive", "先制攻击", () => StampShown("先制攻击！"), 0.15f);
            Watch("dungeon-gate", "出口旁的白色的门", () => Maze.Talking && ui.Q<Label>(className: "sp-dungeon__talk-text").text.Contains("有什么可以帮") /* the rest is wrapped in <nobr> */, 0.3f);
            Watch("dungeon-mirror", "白门变成镜子，镜中是战斗形态的主角", () => Maze.Talking && ui.Q<Label>(className: "sp-dungeon__talk-text").text.Contains("又不太像") /* the 我。 after it is glued in a <nobr> */, 0.3f);
            Watch("awakening", "觉醒「深渊之鲸」", () => ui.Q(className: "fx-awaken__hero") is VisualElement h && h.resolvedStyle.opacity > 0.95f, 0.2f);
            // The field is 3D (Dungeon/BattleStage.cs); the name plates are pinned over the noise bodies on it.
            Watch("battle", "教学战斗开始（3D 战场，名牌钉在噪声体上）", () => game.InBattle && UnityEngine.Object.FindAnyObjectByType<BattleStage>() != null
                && ui.Query(className: "sp-enemy").ToList().Any(e => e.resolvedStyle.visibility == Visibility.Visible), 1.2f);
            Watch("battle-hint", "战斗提示", () => Visible(ui.Q(className: "sp-battle__tutorial")), 0.2f);
            Watch("battle-command", "指令菜单", () => Visible(ui.Q(className: "sp-battle__menu")), 0.3f);
            Watch("battle-target", "选择目标", () => ui.Query(className: "sp-enemy__cursor").ToList().Any(Visible), 0.15f);
            Watch("battle-screenshot", "截图攻击", () => Exists("sp-viewfinder"), 0.15f, false);
            // The 3D effects on the field (Dungeon/BattleEffects.cs), each caught while it is up.
            Watch("battle-slash", "DeepSeek 冲上去斩击（刀光、火花）", () => game.InBattle && !Seen("battle-prompt") && OnStage("Slash"), 0.06f, false);
            // Each skill's and attack's own look (design/skills.md 战斗特效), filmed while it plays.
            Film("battle-capture", "截图：取景框收紧在噪声体上，连同打下的像素飞回手机，缩略图从画面中央缩到左下角", () => OnStage("Snap"), 12, 0.12f);
            // The 截图's other two ways of holding the phone and the 饭勺's four ways in (BattleStage.Shoot, Dash),
            // each taken in turn.
            Film("battle-shot-reach", "截图：单手把手机伸到臂长，侧着拍，缩略图跟着歪", () => Exists("sp-viewfinder") && Stage?.Held == SeaFigure.PhoneHold.Reach, 10, 0.12f);
            Film("battle-shot-low", "截图：单膝跪地，手机放低往上仰拍", () => Exists("sp-viewfinder") && Stage?.Held == SeaFigure.PhoneHold.Low, 10, 0.12f);
            Film("battle-spoon-slash", "饭勺·横斩：冲上去横着斩，勺尖拖出光带", () => OnStage("Spoon Slash"), 15, 0.11f); // the run in and all three cuts
            Film("battle-spoon-sweep", "饭勺·回旋扫：从另一侧冲上去，单脚转身低扫一圈，海床上扩开水环", () => OnStage("Spoon Sweep"), 10, 0.1f);
            Film("battle-spoon-rush", "饭勺·鱼跃连击：贴地鱼跃翻滚过去，起身左右两下", () => OnStage("Spoon Rush"), 12, 0.1f);
            Film("battle-spoon-leap", "饭勺·跃劈：跳起落到噪声体面前砸下，海床冲起水柱", () => OnStage("Spoon Leap"), 10, 0.09f);
            Film("battle-current", "寒流：冰晶寒流从掌心横穿画面、镜头横移跟随，噪声体结冰碎裂", () => OnStage("Stream"), 15, 0.09f); // the ice bursts about 0.95 s in
            Film("battle-maelstrom", "深海潜流：冰蓝漩涡卷起噪声体、镜头后拉，全体冻结碎裂", () => OnStage("Vortex"), 14, 0.12f);
            Watch("battle-impact", "打中弱点：放射光线和冲击环", () => OnStage("Impact"), 0.05f, false);
            Film("battle-popups", "标题党型「震惊！」：弹窗广告砸向队员", () => OnStage("Popup"), 8, 0.08f);
            Film("battle-forward", "转发型「转发保平安」：分叉箭头升空分裂、落向全队", () => OnStage("Forward"), 10, 0.08f);
            Film("battle-cards", "冷知识型「你知道吗？」：卡片旋转着甩向队员", () => OnStage("Card"), 8, 0.08f);
            Watch("battle-coldfact", "冷知识型：一叠洗牌的卡片，名牌上的「弱点：火」是真的，旁边的标题党型也这么写",
                () => game.InBattle && game.Battle.FoeKinds.Contains("coldfact") && Visible(ui.Q(className: "sp-battle__menu")), 0.4f);
            Film("battle-toss", "道具：冰镇汽水翻滚着飞过去、炸开泡沫和冰霜", () => OnStage("Can"), 10, 0.08f);
            Film("battle-blaze", "道具：辣条着火飞过去、炸成火球", () => OnStage("Strip"), 10, 0.08f);
            Watch("battle-skill", "技能列表在左栏，当前队员露在它右边", () => Visible(ui.Q(className: "sp-battle__skills")), 0.2f);
            Watch("battle-cutin", "技能特写", () => Exists("sp-cutin"), 0.2f, false);
            Watch("battle-weak", "对抗样本", () => StampShown("对抗样本！"), 0.15f, false);
            Watch("battle-onemore", "One More", () => StampShown("ONE MORE!"), 0.25f);
            // The poses struck in place (Editor/StancePoser.cs), filmed from the skill bar on: DeepSeek's
            // 深度思考 in the tutorial, the player's 防御 and DeepSeek eating the 白米饭 in the first
            // preemptive battle (GuardAndEat).
            Film("battle-pose-think", "深度思考：两指按太阳穴、低头闭眼，思考字样绕着头飘起，脚下留下蓄力光阵", () => SkillBarSays("深度思考"), 8, 0.15f, null, 640, 360, 4);
            Film("battle-pose-guard", "防御：压低身体、前臂挡在脸前，面前立起六边形光盾", () => SkillBarSays("防御"), 8, 0.11f, null, 640, 360, 4);
            Film("battle-pose-item", "道具：举起白米饭", () => SkillBarSays("白米饭"), 8, 0.15f, null, 640, 360, 4);
            Watch("battle-think", "深度思考", () => Visible(ui.Q(className: "sp-battle__think")), 0.25f);
            Watch("battle-down", "全员倒地", () => StampShown("全员倒地！"), 0.2f);
            Watch("battle-prompt", "集成确认", () => Visible(ui.Q(className: "sp-battle__prompt")), 0.2f);
            Film("battle-allout-dash", "集成：三人轮流冲刺斩", () => OnStage("Streak"), 10, 0.13f);
            Watch("battle-allout", "集成：光之鲸掠过战场", () => OnStage("Whale"), 0.75f);
            Watch("battle-finish", "收尾特写", () => Exists("sp-allout__finish"), 0.4f);
            Watch("battle-results", "战斗结算", () => Exists("sp-results"), 2.6f);
            // The party cards' faces acting (UI/Battle/BattleViews.cs CardFace), filmed over the card row only.
            // Last in the list, so the shots of the same moments above go first; the win is short of the 1.2 s
            // before the results cover the cards.
            var cards = new Rect(0.4f, 0.74f, 0.6f, 0.23f);
            Film("battle-face-attack", "队伍卡片表情：攻击", () => CardActing("is-attack"), 10, 0.1f, cards, 960, 207, 2);
            Film("battle-face-hit", "队伍卡片表情：受击", () => CardActing("is-hit"), 10, 0.1f, cards, 960, 207, 2);
            Film("battle-face-victory", "队伍卡片表情：胜利", () => CardActing("is-victory"), 6, 0.15f, cards, 960, 207, 2);
            Watch("coop", "Coop 卡片", () => Exists("fx-coop"), 0.6f);
            Film("cg-council", "过场 CG：学生会室的窗前（ChatGPT 看着评测文件）", () => game.Story.Cg == "council_window" && game.Story.CgFrame >= 4, 8, 0.9f, null, 480, 270, 4);
            Watch("file", "评测文件", () => Visible(ui.Q(className: "sp-file")), 0.4f);
            Watch("deadline", "评测倒计时", () => Visible(ui.Q(className: "sp-deadline")), 0.4f);
            Watch("chapter-end", "序章结束", () => StampShown("完"), 0.3f);
        }

        // ------------------------------------------------------------------ helpers

        void Watch(string id, string title, Func<bool> reached, float settle, bool required = true) =>
            checkpoints.Add(new Checkpoint { Id = id, Title = title, Reached = reached, Settle = settle, Required = required });

        void Film(string id, string title, Func<bool> reached, int frames, float interval, Rect? region = null, int width = 384, int height = 216, int columns = 5) =>
            checkpoints.Add(new Checkpoint
            {
                Id = id, Title = title + "（胶片）", Reached = reached, Frames = frames, Interval = interval,
                Region = region ?? new Rect(0f, 0f, 1f, 1f), FrameWidth = width, FrameHeight = height, Columns = columns,
            });

        bool Seen(string id) => checkpoints.Any(c => c.Id == id && c.At >= 0);

        // Something of this name is up on the 3D battle field (Dungeon/BattleStage.cs, BattleEffects.cs).
        static bool OnStage(string name) => Stage is BattleStage stage && stage.GetComponentsInChildren<Transform>().Any(t => t.name == name);

        static BattleStage Stage => UnityEngine.Object.FindAnyObjectByType<BattleStage>();

        // Lets the watcher notice what just happened, then waits until it has finished capturing.
        IEnumerator Idle()
        {
            yield return null;
            yield return null;
            while (hold > 0) yield return null;
        }

        IEnumerator WatchLoop()
        {
            while (!finished)
            {
                Checkpoint hit = null;
                foreach (var c in checkpoints)
                {
                    if (c.At >= 0 || c.Reached == null) continue;
                    bool reached;
                    try { reached = c.Reached(); }
                    catch (Exception) { reached = false; }
                    if (!reached) continue;
                    hit = c;
                    break;
                }
                if (hit == null)
                {
                    yield return null;
                    continue;
                }
                hit.At = Now;
                hold++;
                yield return Wait(hit.Settle);
                yield return Capture(hit);
                hold--;
            }
        }

        // Presses Enter until the condition holds; watcher screenshots pause the presses, and the time
        // they hold doesn't count toward the step's timeout (the tutorial battle stops for a dozen).
        IEnumerator Pump(Func<bool> until, string what)
        {
            var driven = 0f;
            var last = Now;
            while (!finished && !until())
            {
                if (StampShown("掉线……")) // lost: the title's Enter would load the last checkpoint and replay
                {
                    Release();
                    Fail("战斗失败（掉线），回到标题");
                    yield return Snap("lost", "战斗失败");
                    Finish();
                    yield break;
                }
                if (hold == 0) driven += Now - last;
                last = Now;
                if (driven > StepTimeout)
                {
                    Fail($"等待「{what}」超时（{StepTimeout:0} 秒）");
                    yield return Snap("timeout", $"超时：{what}");
                    Finish();
                    yield break;
                }
                if (hold == 0) yield return Advance();
                else yield return null;
                yield return Wait(0.06f);
            }
            if (!finished) passes.Add($"{Now:0.0}s　{what}");
        }

        // What the run types on the rooftop before the name it keeps, what each is stored as, and a line of
        // her reaction: her own name (longer than a nickname may be, any capitals: still owed), the names she
        // weighed herself, the community's, a plain one and a sore one.
        static readonly (string Typed, string Stored, string Line, bool Sore)[] Tried =
        {
            ("deepseek", "DeepSeek", "先欠着", false),
            ("D老师", "D老师", "你偷看我的思考过程了", false),
            ("小d", "小D", "暂时通过", false),
            ("小鲸鱼", "小鲸鱼", "只限你叫", false),
            ("鲸鱼娘", "鲸鱼娘", "在教室里要小声一点", false),
            ("鲸老师", "鲸老师", "查不到出处", false),
            ("小胖鲸", "小胖鲸", "要赔我一盒白米饭", true),
        };

        // 天台：DeepSeek 想不出称呼，让玩家起。别的角色的名字不能用；Tried 里的称呼逐个起一遍，每次读回天台开头的
        // 快速存档重来；最后起「大肥鱼」，她抗议但照样认，接下来的台词用上这个称呼。
        IEnumerator Naming()
        {
            yield return Pump(() => game.Nickname.IsOpen, "起称呼的输入框打开");
            if (finished) yield break;
            yield return Wait(0.5f);
            var dialog = ui.Q(className: "sp-nickname");
            var field = dialog.Q<TextField>();
            field.value = "Claude";
            yield return Press(Key.Enter);
            yield return Wait(0.3f);
            var refusal = dialog.Q<Label>(className: "sp-newgame__error").text;
            Check(refusal.Contains("已经被占用") && game.Nickname.IsOpen && !game.Confirm.IsOpen, $"别的角色的名字不能用作称呼（{refusal}）");
            yield return Snap("nickname-refused", "起称呼：别人的名字不能用");
            Check(field.maxLength >= "DeepSeek".Length, $"输入框放得下「DeepSeek」（上限 {field.maxLength} 个字）");

            for (var i = 0; i < Tried.Length && !finished; i++)
            {
                var (typed, stored, line, sore) = Tried[i];
                yield return Name(typed, null, i == 0);
                Check(game.StoryString("$player_calls_deepseek") == stored, $"输入「{typed}」，称呼记为「{stored}」");
                Check(game.StoryBool("$player_calls_deepseek_sore") == sore, $"「{stored}」{(sore ? "戳到痛处" : "没有戳到痛处")}");
                yield return Pump(() => Said(line), $"「{stored}」的反应：{line}");
                var owed = stored == "DeepSeek";
                var last = owed ? "称呼没有变化" : sore ? "今天的份已经超了" : "今天的份用完了";
                yield return Pump(() => Said(last), $"「{stored}」这一段说完：{last}");
                if (owed) yield return ProfileSays("还欠着");

                yield return Idle();
                yield return Press(Key.F9);
                yield return Until(() => !Said(line), "读回天台开头的快速存档");
                yield return Pump(() => game.Nickname.IsOpen, "读档后重新问称呼");
                if (finished) yield break;
                Check(game.StoryString("$player_calls_deepseek") == "", "读档后上一次起的称呼不留");
                yield return Wait(0.5f);
            }

            yield return Name(Nickname, "nickname", false);
            Check(game.StoryString("$player_calls_deepseek") == Nickname, $"主角称呼 DeepSeek 为「{Nickname}」");
            Check(game.StoryBool("$player_calls_deepseek_sore"), "带「肥」字的称呼没有被退回，记为戳到痛处");
            yield return Pump(() => Said("那次是我自己看错了图"), "「大肥鱼」的专属反应");
            yield return Pump(() => Said($"称呼变成了「{Nickname}」"), "系统提示称呼变更");
            yield return Pump(() => Said("今天的份已经超了"), "她抗议但认了这个称呼");
            yield return ProfileSays($"「{Nickname}」");
        }

        // Fills in the open nickname dialog and confirms. shot: the id of the screenshots, or null for none.
        // hesitate: answers 再想想 first, which has to leave the dialog open and nothing decided.
        IEnumerator Name(string name, string shot, bool hesitate)
        {
            ui.Q(className: "sp-nickname").Q<TextField>().value = name;
            yield return Wait(0.2f);
            if (shot != null) yield return Snap(shot, "起称呼");
            for (var i = 0; i < 4 && !game.Confirm.IsOpen; i++) yield return Press(Key.Enter);
            yield return Until(() => game.Confirm.IsOpen, "称呼确认");
            if (hesitate)
            {
                yield return Wait(0.3f);
                yield return Press(Key.RightArrow);
                yield return Press(Key.Enter);
                yield return Until(() => !game.Confirm.IsOpen, "选了「再想想」");
                Check(game.Nickname.IsOpen && game.StoryString("$player_calls_deepseek") == "", "「再想想」后输入框还在，称呼没定");
                for (var i = 0; i < 4 && !game.Confirm.IsOpen; i++) yield return Press(Key.Enter);
                yield return Until(() => game.Confirm.IsOpen, "再次确认称呼");
            }
            yield return Wait(0.45f);
            if (shot != null) yield return Snap(shot + "-confirm", "称呼确认");
            yield return Press(Key.Enter);
            yield return Until(() => !game.ModalOpen, $"称呼定下（{name}）");
        }

        // DeepSeek's phone profile shows `value` (what the player calls her) among its fields.
        IEnumerator ProfileSays(string value)
        {
            game.Profile.Open("DeepSeek");
            yield return Until(() => game.Profile.IsOpen, "DeepSeek 资料打开");
            yield return Wait(0.4f);
            var shown = ui.Query<Label>(className: "sp-profile__value").ToList().Select(l => l.text).ToList();
            Check(shown.Contains(value), $"资料页「你对她的称呼」是 {value}（实际 {string.Join("、", shown)}）");
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen, "资料关闭");
        }

        // 旧存档：改天台之前的存档没有 $player_calls_deepseek，只记了叫没叫「D老师」（$called_d_teacher）。把灯塔节点
        // 开头的检查点改成那时的样子读进来，叫过的恢复成「D老师」，没叫的算欠着，直面影子的台词各不落空；
        // 最后读回原样的检查点，接着往下走。
        IEnumerator OldSaves()
        {
            yield return Pump(() => game.StoryRunner?.CurrentNode == "Prologue_Lighthouse" && !game.InDungeon, "到灯塔脚下");
            if (finished) yield break;
            var now = game.StoryRunner.Checkpoint;
            Check(now.strings.Any(v => v.name == "$player_calls_deepseek" && v.value == Nickname), "起的称呼写进了检查点");
            foreach (var (called, name, line) in new[] { (true, "D老师", "还想被叫D老师"), (false, "DeepSeek", "还想听鱼片欠我的那个称呼") })
            {
                var old = new SaveData
                {
                    node = now.node, playSeconds = now.playSeconds, state = now.state.Clone(), floats = now.floats.ToList(),
                    strings = now.strings.Where(v => !v.name.StartsWith("$player_calls_")).ToList(),
                    bools = now.bools.Where(v => !v.name.StartsWith("$player_calls_")).ToList(),
                };
                if (called) old.bools.Add(new BoolVar { name = "$called_d_teacher", value = true });
                yield return Load(old, $"读旧存档（{(called ? "叫过 D老师" : "没叫过")}）");
                if (finished) yield break;
                Check(game.StoryString("$player_calls_deepseek") == name, $"旧存档的称呼补成「{name}」（实际「{game.StoryString("$player_calls_deepseek")}」）");
                yield return Pump(() => Said(line), $"旧存档直面影子：{line}");
            }
            yield return Load(now, "读回原样的检查点");
            Check(game.StoryString("$player_calls_deepseek") == Nickname, $"新存档读回来称呼还是「{Nickname}」");
        }

        IEnumerator Load(SaveData save, string what)
        {
            yield return Idle();
            var before = game.StoryRunner;
            game.LoadGame(save);
            yield return Until(() => game.StoryRunner != before && game.StoryRunner?.CurrentNode == save.node && !game.Transitions.Busy, what);
        }

        string heardAtOr(string track) => heardAt.TryGetValue(track, out var where) ? where : "没放";

        bool Said(string text) => game.Story.Backlog.Any(b => b.Text.Contains(text));

        // Stands still in the maze until `condition`, reading on through whatever talks meanwhile.
        IEnumerator Lurk(Func<bool> condition, string what)
        {
            Release();
            var t0 = Now;
            while (!finished && !condition())
            {
                if (Now - t0 > StepTimeout)
                {
                    Fail($"等待「{what}」超时（{StepTimeout:0} 秒）");
                    yield return Snap("timeout", $"超时：{what}");
                    Finish();
                    yield break;
                }
                if (!Maze.Exploring && hold == 0) yield return Advance();
                else yield return null;
            }
            if (!finished) passes.Add($"{Now:0.0}s　{what}");
        }

        IEnumerator Until(Func<bool> condition, string what)
        {
            var t0 = Now;
            while (!finished && !condition())
            {
                if (Now - t0 > StepTimeout)
                {
                    Fail($"等待「{what}」超时（{StepTimeout:0} 秒）");
                    yield return Snap("timeout", $"超时：{what}");
                    Finish();
                    yield break;
                }
                yield return null;
            }
            if (!finished) passes.Add($"{Now:0.0}s　{what}");
        }

        // One key press through the Input System: down for two frames, then up.
        IEnumerator Press(Key key)
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return Wait(0.1f);
        }

        static IEnumerator Wait(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator Snap(string id, string title)
        {
            var c = new Checkpoint { Id = id, Title = title, At = Now };
            checkpoints.Add(c);
            yield return Capture(c);
        }

        IEnumerator Clip(string id, string title, int frames, float interval, Rect region, int width, int height, int columns)
        {
            var c = new Checkpoint
            {
                Id = id, Title = title, At = Now, Frames = frames, Interval = interval,
                Region = region, FrameWidth = width, FrameHeight = height, Columns = columns,
            };
            checkpoints.Add(c);
            yield return Capture(c);
        }

        IEnumerator Capture(Checkpoint c)
        {
            c.Shot = $"{++shotCount:00}-{c.Id}.png";
            var path = Path.Combine(outDir, c.Shot);
            if (c.Frames > 0)
            {
                yield return Film(c, path);
                yield break;
            }
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        // One sheet, five 384×216 frames per row, read left to right. Nearest-pixel downscale: enough to
        // see where every panel is.
        IEnumerator Film(Checkpoint c, string path)
        {
            int W = c.FrameWidth, H = c.FrameHeight, Columns = c.Columns;
            var rows = (c.Frames + Columns - 1) / Columns;
            var sheet = new Texture2D(W * Columns, H * rows, TextureFormat.RGB24, false);
            sheet.SetPixels32(new Color32[W * Columns * H * rows]);
            var cell = new Color32[W * H];
            for (var i = 0; i < c.Frames; i++)
            {
                var next = Time.realtimeSinceStartup + c.Interval;
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                var src = shot.GetPixels32();
                int sw = shot.width, sh = shot.height;
                Destroy(shot);
                // Screenshot rows run bottom-up too: the region's bottom edge is row sh * (1 - yMax).
                var r = c.Region;
                for (var y = 0; y < H; y++)
                {
                    var row = Mathf.Min(sh - 1, (int)(sh * (1f - r.yMax) + y * sh * r.height / H)) * sw;
                    for (var x = 0; x < W; x++) cell[y * W + x] = src[row + Mathf.Min(sw - 1, (int)(sw * r.x + x * sw * r.width / W))];
                }
                // Texture rows run bottom-up: the first frame lands top-left.
                sheet.SetPixels32(i % Columns * W, (rows - 1 - i / Columns) * H, W, H, cell);
                while (Time.realtimeSinceStartup < next) yield return null;
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        static void Click(VisualElement target)
        {
            using (var e = ClickEvent.GetPooled())
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        // Not the menu's opacity: the eased fade passes 0.99 a few frames before the menu takes input.
        bool TitleReady => game.Title.Ready && !game.ModalOpen && !game.Transitions.Busy;

        bool Exists(string className) => ui.Q(className: className) != null;

        bool CardActing(string act) => ui.Query(className: "sp-partycard__face").ToList().Any(f => f.ClassListContains(act));

        bool SkillBarSays(string name) => ui.Query<Label>(className: "sp-skillbar__name").ToList().Any(l => l.text == name);

        bool StampShown(string text) => ui.Query<Label>(className: "fx-stamp__text").ToList().Any(l => l.text == text);

        bool SpeakerIs(string name)
        {
            var box = ui.Q(className: "sp-box");
            var label = ui.Q<Label>(className: "sp-nameplate__name");
            return Visible(box) && Visible(label) && label.text == name;
        }

        static bool Visible(VisualElement e)
        {
            if (e == null || e.panel == null) return false;
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        void Check(bool ok, string what)
        {
            if (ok) passes.Add($"{Now:0.0}s　{what}");
            else Fail(what);
        }

        void Fail(string what) => failures.Add($"{Now:0.0}s　{what}");

        void OnLog(string message, string stack, LogType type)
        {
            var line = $"{Now:0.0}s [{type}] {message}";
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) logErrors.Add(line + "\n" + stack);
            else if (type == LogType.Warning) logWarnings.Add(line);
        }

        // ------------------------------------------------------------------ report

        void Finish()
        {
            if (finished) return;
            finished = true;
            Application.logMessageReceived -= OnLog;
            var missed = checkpoints.Where(c => c.Reached != null && c.At < 0).ToList();
            var ok = failures.Count == 0 && logErrors.Count == 0 && missed.All(c => !c.Required);

            var md = new StringBuilder();
            md.AppendLine("# 序章端到端验证");
            md.AppendLine();
            md.AppendLine($"- 结果：**{(ok ? "通过" : "未通过")}**");
            md.AppendLine($"- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}，用时 {Now:0.0} 秒");
            md.AppendLine($"- 环境：Unity {Application.unityVersion}，{SystemInfo.operatingSystem}，{SystemInfo.graphicsDeviceName}，窗口 {Screen.width}×{Screen.height}");
            md.AppendLine("- 方式：虚拟键盘经 Input System 按键，从标题画面玩到序章结束；登记姓名用填值代替输入法");
            md.AppendLine();
            md.AppendLine("## 检查点");
            md.AppendLine();
            md.AppendLine("| 时间 | 检查点 | 截图 |");
            md.AppendLine("| --- | --- | --- |");
            foreach (var c in checkpoints.Where(c => c.At >= 0).OrderBy(c => c.At))
                md.AppendLine($"| {c.At:0.0}s | {c.Title} | {(c.Shot != null ? $"[{c.Shot}]({c.Shot})" : "—")} |");
            if (missed.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("未到达：" + string.Join("、", missed.Select(c => c.Title + (c.Required ? "（必需）" : "（瞬时画面，可缺）"))));
            }
            md.AppendLine();
            md.AppendLine("## 状态检查与流程步骤");
            md.AppendLine();
            foreach (var p in passes) md.AppendLine($"- ✅ {p}");
            foreach (var f in failures) md.AppendLine($"- ❌ {f}");
            md.AppendLine();
            md.AppendLine("## 转场用时与帧时间");
            md.AppendLine();
            md.AppendLine("「游戏时间」是各帧 Time.unscaledDeltaTime 之和，转场按它播放；墙上时间远长于它，是帧在游戏之外被拖慢了（例如窗口被挡住），" +
                          "游戏自己仍按每帧 1/60 秒走。转场把超过 0.1 秒的一帧只算 0.1 秒，「超出」是各帧超过 0.1 秒的部分之和，这段时间按确定也快不了。");
            md.AppendLine();
            md.AppendLine("| 开始 | 转场 | 墙上时间 | 游戏时间 | 帧数 | 每帧墙上时间 | 最慢帧（游戏） | 超出 | 按确定 |");
            md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var w in wipeTimings)
                md.AppendLine($"| {w.At:0.0}s | {w.Style} | {w.Seconds:0.00}s | {w.Game:0.00}s | {w.Frames} | {(w.Frames > 0 ? w.Seconds / w.Frames * 1000f : 0f):0} ms | " +
                              $"{w.Slowest * 1000f:0} ms | {w.Over:0.00}s | {(w.Hurried ? "是" : "")} |");
            md.AppendLine();
            md.AppendLine("## 战斗和潜入前后运行时生成的网格、材质和贴图");
            md.AppendLine();
            md.AppendLine("| 开始 | 场合 | 网格 前 → 后 | 材质 前 → 后 | 贴图 前 → 后 |");
            md.AppendLine("| --- | --- | --- | --- | --- |");
            static string After(int n) => n < 0 ? "—" : n.ToString();
            foreach (var l in leftovers)
                md.AppendLine($"| {l.At:0.0}s | {l.What} | {l.Meshes} → {After(l.MeshesAfter)} | {l.Materials} → {After(l.MaterialsAfter)} | {l.Textures} → {After(l.TexturesAfter)} |");
            md.AppendLine();
            md.AppendLine($"## 日志：{logErrors.Count} 个错误，{logWarnings.Count} 个警告");
            md.AppendLine();
            if (logErrors.Count + logWarnings.Count > 0)
            {
                md.AppendLine("```");
                foreach (var e in logErrors.Take(30)) md.AppendLine(e.TrimEnd());
                foreach (var w in logWarnings.Take(30)) md.AppendLine(w);
                md.AppendLine("```");
            }
            File.WriteAllText(Path.Combine(outDir, "report.md"), md.ToString());
            Application.Quit(ok ? 0 : 1);
        }
    }
}
#endif
