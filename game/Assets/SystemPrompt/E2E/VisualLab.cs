#if UNITY_EDITOR || SP_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using SystemPrompt.Battle;
using SystemPrompt.Dungeon;
using SystemPrompt.UI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using BElement = SystemPrompt.Battle.Element;
using BAffinity = SystemPrompt.Battle.Affinity;

namespace SystemPrompt.E2E
{
    // The visual lab (game/Tools/lab.sh): stages the battle actions and the latent-space maze and films them, for
    // whoever works on character motion, effects and scenery to see what they changed. Every scenario is a sheet of
    // 480×270 frames four to a row, taken at a fixed interval of game time, or a still at the window's size; index.md
    // lists what each shows, the interval and what was logged while it ran.
    // The game animates on unscaled time (Time.captureFramerate doesn't hold that still), so the lab runs it in real
    // time at a steady 60 frames a second and takes each film frame on the first frame at or past its time: the
    // frames land within half a frame (8 ms) of their times, and a run differs from the last only by that much.
    // The game state lives in memory only: lab.sh points HOME at an empty folder for the run, and the lab refuses to
    // start when the game's data folder isn't inside it, so no save or setting on disk is read or written.
    // Battles go through GameRoot.RunBattle and the keyboard; what the real flow never shows (冷知识型's 你知道吗？,
    // a lone lunge, a weakness or critical hit on cue, a fainting member) is staged on the field directly.
    // Only starts with `-lab <output dir>` (and `-labOnly id,prefix,...`); release builds don't contain it.
    public sealed class VisualLab : MonoBehaviour
    {
        const int Fps = 30;          // film intervals are whole 30ths of a second
        const int Pace = 60;         // the frame rate the game runs at while the lab films
        const int FrameW = 480, FrameH = 270, Columns = 4, PerSheet = 16;
        const float WallLimit = 1200f;
        const string PlayerName = "测试员";

        // ------------------------------------------------------------------ bookkeeping

        sealed class Scenario
        {
            public string Id, Group, Shows;
            public bool Selected, Failed;
            public int OpenCount;
            public readonly List<string> Outputs = new List<string>();
            public readonly List<string> Errors = new List<string>(), Warnings = new List<string>(), Notes = new List<string>();
            public bool Kept;            // a film of it is in and on its way to disk
            public bool Captured => Kept || Outputs.Count > 0;
        }

        sealed class CamPose
        {
            public Vector3 At, Look;
            public float Fov;
        }

        sealed class View
        {
            public string Tag, What;
            public bool IsScreen;        // the screen as the player sees it, interface and all
            public Camera Cam;           // otherwise this camera renders the 3D scene alone (gone: the frame stays blank)
            public Func<CamPose> Pose;   // null with a camera: the camera where it stands
            public readonly List<Color32[]> Frames = new List<Color32[]>();
        }

        sealed class Film
        {
            public Scenario Sc;
            public string Name;
            public int Step, Count, Taken, PauseAt = -1;
            public float T0 = -1f;       // game time of the first frame
            public float Late, Hitch;    // the furthest a frame came after its time; the longest frame while it rolled
            public int Pending;          // frames still on their way back from the GPU
            public bool Paused, Done, Capture, Broken;
            public List<View> Views;
            public Func<bool> Valid;     // checked when the last frame is in: false throws the take away
            public string Invalid;       // why a take was thrown away
            public Action OnDone;
            public bool Rejected;
            public float Interval => Step / (float)Fps;
        }

        sealed class Still
        {
            public Scenario Sc;
            public string Name, What;
            public Camera Cam;
            public Func<CamPose> Pose;
            public bool Done;
        }

        sealed class Job
        {
            public bool Done;
            public Exception Error;
        }

        sealed class LabFailure : Exception
        {
            public LabFailure(string message) : base(message) { }
        }

        // A battle run through the menus: what each member does on their turns, in order, and what the lab does
        // when a noise body's skill bar comes up, at the all-out prompt, at the win and on the results.
        sealed class Fight
        {
            public readonly Dictionary<string, List<Func<IEnumerator>>> Steps = new Dictionary<string, List<Func<IEnumerator>>>();
            public Func<string, IEnumerator> Fallback;
            public readonly Dictionary<string, Action> OnBar = new Dictionary<string, Action>();
            public Func<IEnumerator> OnPrompt, OnResults;
            public Action OnVictory;
            public Func<bool> Finished;
        }

        // Where a layer of the maze is filmed in motion: straight runs head east from Lane, the side camera south.
        sealed class Ground
        {
            public string P;
            public Vector2Int Lane, TurnFrom, Rest;
            public Vector3 CircleCentre, CircleCam; // the circle's middle; where its side camera stands, from the middle
            public float Radius;
        }

        const BindingFlags Any =BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        string outDir;
        string[] only;
        GameRoot game;
        VisualElement ui;
        Keyboard kb;
        Gamepad pad;
        float dzMin = 0.125f, dzMax = 0.925f;
        float started;
        bool finished;
        bool flipScreen, flipCam;
        string currentGroup = "启动";
        int pendingWrites;
        readonly List<string> writeErrors = new List<string>();
        readonly List<Scenario> scenarios = new List<Scenario>();
        readonly Dictionary<string, Scenario> byId = new Dictionary<string, Scenario>();
        readonly List<(string Id, Func<IEnumerator> Body)> groups = new List<(string, Func<IEnumerator>)>();
        readonly List<string> runErrors = new List<string>();
        readonly List<string> groupLog = new List<string>();
        readonly List<string> timings = new List<string>();
        readonly List<string> unknownTokens = new List<string>();
        readonly List<Film> films = new List<Film>();
        readonly List<Still> stills = new List<Still>();
        readonly List<Film> writing = new List<Film>();
        readonly List<RenderTexture> smallRTs = new List<RenderTexture>();
        RenderTexture screenRT, midRT, camRT, fullRT, fullFlat;
        bool panelClock;
        float clockMeasured;

        // Private members of the game the lab drives: looked up once at the start, a clear failure if one is gone.
        FieldInfo fBattleMode, fActor, fEnemies, fParty, fStage, fTargets, fTargetIndex, fStrikes, fShots;
        MethodInfo mApplyHit, mEnemyBar;
        FieldInfo fDungeonMode, fMeter, fVisited, fPitch, fCameraDistance;
        PropertyInfo pPlayerPosition;
        MethodInfo mUpdateCamera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-lab");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("Visual Lab");
            DontDestroyOnLoad(go);
            var lab = go.AddComponent<VisualLab>();
            lab.outDir = args[i + 1];
            var j = Array.IndexOf(args, "-labOnly");
            if (j >= 0 && j + 1 < args.Length)
                lab.only = args[j + 1].Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        IEnumerator Start()
        {
            started = Time.realtimeSinceStartup;
            Directory.CreateDirectory(outDir);
            Application.logMessageReceived += OnLog;
            // A steady 60 frames a second in real time (the game's clock): see the note at the top.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Pace;
            AudioListener.volume = 0f;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            dzMin = InputSystem.settings.defaultDeadzoneMin;
            dzMax = InputSystem.settings.defaultDeadzoneMax;
            kb = InputSystem.AddDevice<Keyboard>("Lab Keyboard");
            pad = InputSystem.AddDevice<Gamepad>("Lab Gamepad");
            // The desk's mouse, keyboard and pads stay out of it.
            foreach (var device in InputSystem.devices.ToList())
                if (device is UnityEngine.InputSystem.Pointer || (device is Keyboard && device != kb) || (device is Gamepad && device != pad))
                    InputSystem.DisableDevice(device);
            pad.MakeCurrent();
            kb.MakeCurrent();
            // Where texture rows start at the top (Metal), the captured screen reads back upside down; a camera's render
            // into a texture comes back the right way up.
            flipScreen = SystemInfo.graphicsUVStartsAtTop;
            flipCam = false;
            panelClock = HookPanelClock();
            Define();
            StartCoroutine(Recorder());

            while (GameRoot.Instance == null) yield return null;
            game = GameRoot.Instance;
            if (!Sandboxed(out var why))
            {
                runErrors.Add(why);
                Debug.Log("[lab] " + why);
                // Nothing of the game's may write into the real data folder on the way out.
                Destroy(game.gameObject);
                yield return null;
                WriteIndex();
                Application.Quit(3);
                yield break;
            }
            ui = game.GetComponent<UIDocument>().rootVisualElement;
            try { Bind(); }
            catch (Exception e)
            {
                runErrors.Add("找不到实验室要用的游戏成员：" + e.Message);
                WriteIndex();
                Application.Quit(2);
                yield break;
            }
            while (game.Transitions.Busy) yield return null;
            yield return Frames(10);

            // The pace: 60 frames should take a second; a slower machine still films at the right times, choppier.
            var t0 = Time.unscaledTime;
            yield return Frames(Pace);
            clockMeasured = Time.unscaledTime - t0;
            if (clockMeasured > 1.2f) groupLog.Add($"警告：{Pace} 帧用了 {clockMeasured:0.00} 秒，机器跟不上 {Pace} 帧/秒，胶片的帧会在时间点之后最多一帧才拍");
            if (!panelClock) groupLog.Add("警告：界面（UI Toolkit）时钟没能接到游戏时间上，界面里按时间表走的小动画（噪声体名牌的抽动等）每次会不一样");

            foreach (var (id, body) in groups)
            {
                if (!scenarios.Any(s => s.Group == id && s.Selected)) continue;
                currentGroup = id;
                var wall = Time.realtimeSinceStartup;
                var gameTime = Time.unscaledTime;
                Debug.Log($"[lab] group {id}");
                yield return Guarded(id, body(), Recover);
                yield return Flush();
                timings.Add($"{id}：{Time.realtimeSinceStartup - wall:0.0} 秒（游戏时间 {Time.unscaledTime - gameTime:0.0} 秒）");
                if (Time.realtimeSinceStartup - started > WallLimit)
                {
                    runErrors.Add($"整体超过 {WallLimit:0} 秒，后面的组没有跑");
                    break;
                }
            }
            currentGroup = "收尾";
            Release();
            var waitWrites = Time.realtimeSinceStartup + 60f;
            while ((writing.Count > 0 || Volatile.Read(ref pendingWrites) > 0) && Time.realtimeSinceStartup < waitWrites) yield return null;
            finished = true;
            File.WriteAllText(Path.Combine(outDir, "poses.tsv"), poses.ToString());
            var ok = WriteIndex();
            Debug.Log($"[lab] done in {Time.realtimeSinceStartup - started:0.0} s, {(ok ? "ok" : "FAILED")}");
            Application.Quit(ok ? 0 : 1);
        }

        // ------------------------------------------------------------------ poses

        // Every humanoid figure's pose in numbers, each frame, into poses.tsv: where it stands, which way its root,
        // hips and chest point (degrees about the up axis, 0 along +z), how far its head is turned off its chest, and
        // how high its ankles are over its root. Read afterwards against the film it was taken in (the tag): which way
        // a figure faces as against the way it moves or what it stands before, whether its feet float or sink.
        readonly StringBuilder poses = new StringBuilder("t\ttag\tfigure\tx\ty\tz\troot\thips\tchest\tleftFoot\trightFoot\thipsY\tlean\tside\theadY\tleftHand\trightHand\tleftArm\trightArm\thead\tleftReach\trightReach\tshoulderY\tleftFootX\tleftFootZ\trightFootX\trightFootZ\n");
        readonly List<Animator> posed = new List<Animator>();
        string poseTag = "";
        float poseScan;

        void LateUpdate()
        {
            if (finished) return;
            if (Time.unscaledTime >= poseScan)
            {
                poseScan = Time.unscaledTime + 0.5f;
                posed.Clear();
                foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
                    if (a.isHuman && a.gameObject.activeInHierarchy) posed.Add(a);
            }
            foreach (var a in posed)
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                var root = a.transform;
                Transform hips = a.GetBoneTransform(HumanBodyBones.Hips),
                    ll = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rl = a.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                    la = a.GetBoneTransform(HumanBodyBones.LeftUpperArm), ra = a.GetBoneTransform(HumanBodyBones.RightUpperArm),
                    lf = a.GetBoneTransform(HumanBodyBones.LeftFoot), rf = a.GetBoneTransform(HumanBodyBones.RightFoot);
                if (hips == null || ll == null || rl == null || la == null || ra == null || lf == null || rf == null) continue;
                var at = root.position;
                poses.Append(Time.unscaledTime.ToString("0.000")).Append('\t').Append(poseTag).Append('\t').Append(Named(root)).Append('\t')
                    .Append(at.x.ToString("0.000")).Append('\t').Append(at.y.ToString("0.000")).Append('\t').Append(at.z.ToString("0.000")).Append('\t')
                    .Append(Heading(root.forward).ToString("0.0")).Append('\t')
                    .Append(Heading(Vector3.Cross(rl.position - ll.position, Vector3.up)).ToString("0.0")).Append('\t')
                    .Append(Heading(Vector3.Cross(ra.position - la.position, Vector3.up)).ToString("0.0")).Append('\t')
                    .Append((lf.position.y - at.y).ToString("0.000")).Append('\t').Append((rf.position.y - at.y).ToString("0.000")).Append('\t')
                    .Append((hips.position.y - at.y).ToString("0.000")).Append('\t');
                // The trunk's lean from upright, hips to neck: degrees forward along the root's facing, and to its right;
                // the head's and hands' heights over the root.
                Transform neck = a.GetBoneTransform(HumanBodyBones.Neck) ?? a.GetBoneTransform(HumanBodyBones.Head),
                    head = a.GetBoneTransform(HumanBodyBones.Head),
                    lh = a.GetBoneTransform(HumanBodyBones.LeftHand), rh = a.GetBoneTransform(HumanBodyBones.RightHand);
                var trunk = neck.position - hips.position;
                poses.Append((Mathf.Atan2(Vector3.Dot(trunk, root.forward), trunk.y) * Mathf.Rad2Deg).ToString("0.0")).Append('\t')
                    .Append((Mathf.Atan2(Vector3.Dot(trunk, root.right), trunk.y) * Mathf.Rad2Deg).ToString("0.0")).Append('\t')
                    .Append((head.position.y - at.y).ToString("0.000")).Append('\t')
                    .Append((lh.position.y - at.y).ToString("0.000")).Append('\t').Append((rh.position.y - at.y).ToString("0.000")).Append('\t')
                    // Which way each arm is held out, shoulder to hand, and which way the head faces (across its ears... it
                    // has none: across the eyes if the rig has them, else the neck's heading is the chest's).
                    .Append(Heading(lh.position - la.position).ToString("0.0")).Append('\t').Append(Heading(rh.position - ra.position).ToString("0.0")).Append('\t');
                Transform le = a.GetBoneTransform(HumanBodyBones.LeftEye), re = a.GetBoneTransform(HumanBodyBones.RightEye);
                poses.Append(le != null && re != null ? Heading(Vector3.Cross(re.position - le.position, Vector3.up)).ToString("0.0") : "").Append('\t')
                    // How far each hand is held out ahead of the hips, along the root's facing, and the shoulders' height.
                    .Append(Vector3.Dot(lh.position - hips.position, root.forward).ToString("0.000")).Append('\t')
                    .Append(Vector3.Dot(rh.position - hips.position, root.forward).ToString("0.000")).Append('\t')
                    .Append(((la.position.y + ra.position.y) * 0.5f - at.y).ToString("0.000")).Append('\t')
                    // Where each ankle is over the floor, in the world: a planted foot's should not move.
                    .Append(lf.position.x.ToString("0.000")).Append('\t').Append(lf.position.z.ToString("0.000")).Append('\t')
                    .Append(rf.position.x.ToString("0.000")).Append('\t').Append(rf.position.z.ToString("0.000")).Append('\n');
            }
        }

        static float Heading(Vector3 way) => Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg;

        // The figure's name with its parents', up to three, so the player and DeepSeek on the field or in the maze
        // can be told apart.
        static string Named(Transform t)
        {
            var name = t.name;
            for (var i = 0; i < 2 && t.parent != null; i++)
            {
                t = t.parent;
                name = t.name + "/" + name;
            }
            return name;
        }

        // ------------------------------------------------------------------ scenarios

        void Def(string group, string id, string shows)
        {
            var s = new Scenario { Id = id, Group = group, Shows = shows };
            s.Selected = only == null || only.Any(t => Matches(s, t));
            scenarios.Add(s);
            byId[id] = s;
        }

        // A -labOnly token picks the scenarios whose id starts with it, or a whole group by its name (battle-main).
        static bool Matches(Scenario s, string token) => s.Id.StartsWith(token, StringComparison.Ordinal) || s.Group == token;

        Scenario S(string id) => byId[id];

        void Define()
        {
            const string main = "battle-main", finale = "battle-finale", female = "battle-female";
            Def(main, "battle-opening", "开场：擦除转场、队伍与三只噪声体（标题党型、转发型、标题党型）入场，到第一个指令");
            Def(main, "battle-stance-player", "男主角战斗待机 3 秒：指令镜头（游戏画面）+ 左侧检查镜头");
            Def(main, "battle-stance-deepseek", "DeepSeek（觉醒）战斗待机 3 秒：指令镜头（游戏画面）+ 右侧检查镜头");
            Def(main, "battle-shot-eye", "截图·双手举到眼前（三种里的第 1 种）：取景框、快门、命中");
            Def(main, "battle-guard", "防御：防御姿势、护盾和技能条");
            Def(main, "battle-think", "深度思考：思考姿势、蓄力光环、思考气泡");
            Def(main, "battle-spoon-slash", "饭勺·横斩（四种里的第 1 种）：冲上去、挥勺、命中、回位");
            Def(main, "battle-shot-reach", "截图·单手伸臂（第 2 种）");
            Def(main, "battle-current-charged", "寒流（深度思考蓄力后 ×2.5）打标题党型：切入、冰流、冻结、WEAK、倒地");
            Def(main, "battle-spoon-sweep", "饭勺·回旋扫（第 2 种，ONE MORE 后）");
            Def(main, "battle-shot-low", "截图·单膝跪低位（第 3 种）");
            Def(main, "battle-current", "寒流（普通）打另一只标题党型");
            Def(main, "battle-spoon-rush", "饭勺·翻滚突刺（第 3 种）");
            Def(main, "battle-item-rice", "道具·白米饭：男主角给 DeepSeek，回复数字");
            Def(main, "battle-spoon-leap", "饭勺·跃劈（第 4 种）");
            Def(main, "battle-soda", "投掷冰镇汽水打转发型（弱点，倒地）");
            Def(main, "battle-strip", "投掷辣条打标题党型（它耐火）");
            Def(main, "battle-bait", "队员受击：标题党型「震惊！」逼近镜头、扑向队员、弹窗砸中、受击后仰");
            Def(main, "battle-forward", "转发型「转发保平安」：仰拍、箭雨落在全队、全员受击");
            Def(main, "battle-guard-hit", "防御中受击：男主角举着防御时挨「转发保平安」");
            Def(main, "battle-hitstop", "命中顿帧（1/30 秒一帧）：饭勺·横斩打中的前后，接触时姿势停住几帧，数字、受击音和镜头收缩落在同一帧");
            Def(main, "battle-hitstop-shot", "命中顿帧（1/30 秒一帧）：截图·双手举到眼前，快门那一帧的停顿和数字");
            Def(main, "battle-perf", "帧时间：开场后第一个指令镜头，解除 60 帧上限跑 240 帧，记平均、中位和 95% 帧时间，再关掉影子和轮廓光（SeaAtmos.off）跑一次（静帧 + 备注）");
            Def(main, "battle-face-clickbait", "标题党型的脸（正面特写、不带界面）：平静、被选中、发怒（张嘴喊「震惊！」）、受击、求饶、眩晕、睡着，各半秒");
            Def(main, "battle-face-repost", "转发型的脸（正面特写、不带界面）：同上七种神情");
            Def(finale, "battle-vanish", "敌人被删除：寒流打死残血的标题党型，定格闪白、炸出像素和「已删除」弹窗，方块飞散落到海床上变暗消失");
            Def(finale, "battle-summon", "转发型「转发到十个群」召来新的标题党型");
            Def(finale, "battle-maelstrom", "深海潜流（全体）：切入、漏斗水幕、漩涡卷起所有噪声体、全员倒地");
            Def(finale, "battle-allout", "全员倒地 → 按下 ▶发动 → 集成，到收尾");
            Def(finale, "battle-victory", "胜利：队伍欢呼、镜头上移、结算界面进入");
            Def(female, "battle-female", "女主角：待机 → 防御 →（下一回合）对自己用白米饭；游戏画面 + 左侧检查镜头");
            Def(female, "battle-lunge", "噪声体蓄势（后退、压低、广告窗全部弹开）再扑击（Brace + Lunge，单独演示，侧面看全程）");
            Def(female, "battle-lecture", "冷知识型「你知道吗？」：逼近、知识卡片砸中 DeepSeek（正常流程里敌人不会出这招，直接调用）");
            Def(female, "battle-weak", "弱点命中：WEAK、冰色冲击、倒地、假弱点标签碎裂（直接调用 ApplyHit）");
            Def(female, "battle-crit", "暴击：CRITICAL、冲击、倒地（直接调用 ApplyHit）");
            Def(female, "battle-getup", "倒地的标题党型起身：头上的转圈方块炸成像素、跳起拉长、落地压扁、左右晃两下（0.05 秒一帧）");
            Def(female, "battle-calm", "减少闪烁打开、画面震动关掉时的弱点命中：3D 闪光和星芒变暗、场上的光压低、不震、镜头收缩变小，格子换色变慢变淡（直接调用 ApplyHit，拍完恢复）");
            Def(female, "battle-debunk", "冷知识型被打死：定格闪白、「已辟谣」弹窗、卡片像纸一样飘散（直接调用 ApplyHit）");
            Def(female, "battle-knock", "队员被击倒：女主角防御中被「震惊！」暴击打倒在海床上，光盾留着、躺着，轮到她时光盾收起、起身（直接调用；侧面看全程）");
            Def(female, "battle-faint", "队员倒下：DeepSeek 防御中被「震惊！」暴击打倒，躺着又挨一次被打到 0 HP，一直躺着不站起来（直接调用）");
            Def(female, "battle-kill", "最后一击：只剩一只标题党型（血量设成 1），截图把它打死：顿住、切到低位的杀招机位慢慢推近、慢速崩解，接胜利（走菜单，2/30 秒一帧）");
            Def(female, "battle-face-coldfact", "冷知识型的脸（正面特写、不带界面）：平静、被选中、发怒、受击、求饶、眩晕、睡着，各半秒");

            foreach (var p in new[] { "sea", "deep" })
            {
                var layer = p == "sea" ? "浅层" : "深层";
                var follower = p == "sea" ? "日常装 DeepSeek" : "战斗形态 DeepSeek（带尾巴）";
                Def(p, p + "-zones", $"{layer}每个区域一张：传送到位、镜头和同伴停稳后的实际游戏画面（静帧，带界面）");
                Def(p, p + "-wide", $"{layer}远景：抬高的自由镜头（静帧，不带界面）");
                Def(p, p + "-idle", $"{layer}站立待机 3 秒：游戏镜头 + 南侧 3.5 米、髋高的跟拍侧面镜头");
                Def(p, p + "-walk", $"{layer}慢走：起步 → 匀速 → 停下（左摇杆推到约 1/3）");
                Def(p, p + "-run", $"{layer}跑：起步 → 匀速 → 停下");
                Def(p, p + "-sprint", $"{layer}冲刺（按住冲刺）：起步 → 匀速 → 停下");
                Def(p, p + "-model", $"{layer}人物模型近看（整窗静帧）：主角和{follower}站着时的全身五个方向、脸、后脑、上半身、双手、腰以下和脚，以及跑动中的侧面、身后和斜前方");
                Def(p, p + "-gait", $"{layer}步态近景：慢走、跑、冲刺各一段匀速时的胶片（1/15 秒一帧，1.6 秒），2.6 米外跟拍主角的侧面、身后和斜前方，以及{follower}的侧面和身后");
                Def(p, p + "-turn", $"{layer}跑动中 180° 掉头");
                Def(p, p + "-halt", $"{layer}全速跑时操作被收走（如同区域节点触发）：刹住滑停半米左右，不是一帧变站立（1/30 秒一帧）");
                Def(p, p + "-circle", $"{layer}跑圈（半径 2.5 米），侧面镜头固定在圈外跟着转");
                Def(p, p + "-follow", $"{layer}同伴跟随：{follower}跟着跑和停，侧面镜头对着两人中点、6 米外");
                Def(p, p + "-look", $"{layer}站着 8 秒：两人隔一阵转头互相看一眼（0.5 秒一帧），镜头在两人前方 3.5 米对着脸");
                Def(p, p + "-tail", $"{layer}{follower}的尾巴：站着 4 秒自己慢慢摆（0.25 秒一帧），再摇一下（-wag，1/15 秒一帧），镜头在她斜后方");
                Def(p, p + "-steps", $"{layer}海床上的脚印：冲刺中松手急停、再起跑（1/15 秒一帧），落脚的数据涟漪和泥沙、冲刺的气泡尾迹、急停踢起的一圈泥沙；侧面低机位 5 米外");
            }
            Def("sea", "sea-pearl", "捡数据珍珠：跑过去、珍珠被收走、提示");
            Def("sea", "sea-beacon", "信标：休息前静帧、走近点亮的胶片、休息后静帧");
            Def("sea", "sea-chest", "宝箱打开：盖子先往下压一下再弹开过头，缝里的光一亮再暗，金色光束、光点和三张卡片往上飞，镜头转向宝箱，放开操作后转回");
            Def("sea", "sea-bump", "推不动：缓存块推到北边机柜前再推一次，顶一下、灯闪红，再回原色；之后撤回一步");
            Def("sea", "sea-block", "缓存块：最后一推滑进插槽（前十推直接传送着推完）：轮廓收起、落座、底座闪白、海床光环、电缆上的光点跑向闸门");
            Def("sea", "sea-gate", "三块缓存块都进插槽后闸门打开：游戏画面（门在画面顶上、隔着机柜，切插入镜头）+ 门口的固定镜头");
            Def("sea", "sea-firewall", "防火墙插入镜头：终端对话里 <<reveal firewall>> 的两段（维护间西墙、主航道北头；遮幅、下行光束、溶解、海床光环，第二段灯塔在墙后入画）");
            Def("sea", "sea-terminal", "终端：按下后屏幕滚日志、上行光束（游戏画面 + 终端东侧的固定镜头 + 正对屏幕的近景）");
            Def("sea", "sea-lamp", "主角脚下的海床光：站着时随呼吸胀缩（相隔半个呼吸的两张俯拍），被 4 号追着冲刺时放大、偏红（游戏画面 + 俯拍）");
            Def("sea", "sea-leave", "离开浅层：站上灯塔脚下的终点后，镜头升到主角背后仰拍，灯塔的光转下来照住两人（游戏画面 + 广场西南高处）");
            Def("sea", "sea-noise-rest", "浅层每只噪声体平时的样子：1 号打瞌睡，2–4 号巡逻（每只一张，噪声体前方的检查镜头）");
            Def("sea", "sea-noise1-notice", "1 号标题党型（打瞌睡）察觉 → 追赶：玩家站在它面前 4 米，被发现后冲刺跑开");
            Def("sea", "sea-noise1-strike", "从背后偷袭 1 号：按下确认后的先制攻击（主角举手机拍下、闪光；它被拍得弹起定住；制服 DeepSeek 蹲下躲）和战斗转场（1/15 秒一帧，画面 + 侧面）");
            Def("sea", "sea-noise4-notice", "4 号转发型（巡逻）察觉 → 追赶");
            Def("sea", "sea-noise4-strike", "从背后偷袭 4 号（巡逻中）");
            Def("sea", "sea-noise4-counter", "4 号追来时迎面按确认（迎击「遭遇！」）：主角举手机拍下，它被闪光打退（1/15 秒一帧，画面 + 侧面）");
            Def("sea", "sea-noise4-ambush", "背对追来的 4 号被它撞上（被偷袭）：受击、红色边缘、镜头转开避让、它扑上来（1/15 秒一帧，画面 + 侧面）");
            Def("sea", "sea-lookup", "抬头看灯塔：起点朝北，默认俯仰 20° 和抬到最高的 −12°（机位最低到 6°，再往上只抬视线，抬到主角的头留在画面底边为止）");
            Def("sea", "sea-wall", "镜头被墙挤近再放开：主航道西侧，人背靠南边的机柜朝北，慢跑走开八米到空地、停一下、再退回墙前（0.1 秒一帧，游戏画面）：开阔和贴墙两种取景之间、镜头一点点收近时都不跳");
            Def("sea", "sea-near", "镜头贴近布景：气泡礁朝南，镜头在北边一格宽的走道正中和贴着东边，贴着镜头的布景变成网点");
            Def("sea", "sea-sweep", "灯塔扫光：人在主航道朝北，等一道光束快转到人这边时开拍（0.2 秒一帧），光带扫过地面和机柜（游戏画面 + 西南高处俯瞰）");
            Def("sea", "sea-sights", "布景近景（游戏画面，人站在走得到的地方）：手机地标、手机南沿、珊瑚林入口、墓地、峡谷墙脚、峡谷抬头看垂缆、广场东南角");
            Def("sea", "sea-sway", "珊瑚和海草在水里摇：珊瑚林里的固定近景（0.5 秒一帧）");
            foreach (var p in new[] { "sea", "deep" })
            {
                var layer = p == "sea" ? "浅层" : "深层";
                Def(p, p + "-perf", $"{layer}帧时间：起点的游戏镜头和高处俯瞰，解除 60 帧上限各跑 240 帧，记平均、中位和 95% 帧时间；两处都再关掉区域空气、影子、轮廓光和局部光（SeaAtmos.off）各跑一次（静帧 + 备注）");
                Def(p, p + "-airless", $"{layer}区域空气关掉和打开（SeaAtmos）：同一处的游戏镜头（只有 3D 画面），关掉时应和加空气之前的画面一致");
                Def(p, p + "-shade", $"{layer}人物的轮廓光和海床上的影子（SeaShade）：背后近景看后脑和肩膀的一圈背光、衣褶和头发里没有青点，侧面低机位看脚下的影子（只有 3D 画面）");
                Def(p, p + "-border", p == "sea"
                    ? "跑过区域交界：主航道沿第 15 行往西跑进表情包珊瑚林，水色和雾慢慢过渡、不跳（0.4 秒一帧，游戏画面）"
                    : "跑过区域交界：回收站沿第 4 行往东跑进白门大厅，水色变亮、第一次进入时涌亮一下（0.4 秒一帧，游戏画面）");
                Def(p, p + "-battle-zones", p == "sea"
                    ? "同一场战斗分别在气泡礁和删帖墓地开打：战场继承遇敌区域的水色和调色（第一个指令镜头，静帧）"
                    : "同一场战斗分别在裂缝和白门大厅开打：战场继承遇敌区域的水色和调色（第一个指令镜头，静帧）");
            }
            Def("deep", "deep-pearl", "裂缝起点旁的数据珍珠：一格宽的走道里镜头贴近，跑过去、珍珠飞进主角胸口（游戏画面 + 珍珠北边的固定镜头）");
            Def("deep", "deep-rack", "沉没机房的机柜：人朝北站在机柜前一米半，柜脚压暗、主角的灯在柜面上照出一块分段的高光");
            Def("deep", "deep-noise-rest", "深层每只噪声体平时的样子：1 号打瞌睡，其余巡逻（每只一张）");
            Def("deep", "deep-noise1-notice", "深层 1 号标题党型（打瞌睡）察觉 → 追赶");
            Def("deep", "deep-noise1-strike", "从背后偷袭深层 1 号");
            Def("deep", "deep-noise2-notice", "深层 2 号转发型（巡逻）察觉 → 追赶");
            Def("deep", "deep-noise2-strike", "从背后偷袭深层 2 号");
            Def("deep", "deep-noise2-counter", "深层 2 号追来时迎面按确认（迎击）：战斗服 DeepSeek 举饭勺跃劈过去");
            Def("deep", "deep-noise2-ambush", "背对追来的深层 2 号被它撞上（被偷袭）：两人都受击");
            Def("deep", "deep-noise5-notice", "深层 5 号冷知识型（巡逻）察觉 → 追赶");
            Def("deep", "deep-noise5-strike", "从背后偷袭深层 5 号");
            Def("deep", "deep-plate", "反射板：第一次转动，光路跟着改");
            Def("deep", "deep-timeout", "超时黑场：先单独放 2.5 秒（雾变黑、远处机柜先没入、气泡往上倒流），再从 99.9% 走一步真的超时：冷色闪、拖回起点、雾散开");
            Def("deep", "deep-calm", "减弱闪光（开着）：点亮信标的事件光减半，超时回弹的冷色全屏闪峰值 0.25");
            Def("deep", "deep-exit", "最后一转（Backspace 反转）：光一段段推进到终点，插入镜头里出口从海床升起（游戏画面 + 看终点的固定镜头）");
            Def("deep", "deep-mirror-hurry", "白门前催过的 <<mirror walk>>：走了 0.4 秒按确认，剩下的三倍速走完（1/20 秒一帧），DeepSeek 照样走到自己的位置，不跳；记她一帧最多走多远");
            Def("deep", "deep-mirror", "白门镜面：<<mirror walk>> 走到门前（雾随镜头转过来散掉，DeepSeek 和主角一起走到），<<mirror on>> 镜面从中间波纹显现（胶片）、停稳后的静帧，<<mirror off>> 扫描带扫过、镜中人换回家居服（胶片）；记 DeepSeek 一帧最多走多远");
            Def("deep", "deep-leave", "离开深层：白门台词之后两人中间升起光柱、火花往上冲、镜头上仰（镜子的机位）");

            groups.Add((main, BattleMain));
            groups.Add((finale, BattleFinale));
            groups.Add((female, BattleFemale));
            groups.Add(("sea", Sea));
            groups.Add(("deep", Deep));
            Def("girl", "girl-gait", "女主角的步态近景（浅层）：站立、慢走、跑、冲刺，镜头同 sea-gait");
            Def("girl", "girl-model", "女主角的模型近看（浅层，整窗静帧），镜头同 sea-model");
            groups.Add(("girl", Girl));

            if (only != null)
                foreach (var t in only)
                    if (!scenarios.Any(s => Matches(s, t))) unknownTokens.Add(t);
        }

        void Begin(Scenario sc) => sc.OpenCount++;

        void End(Scenario sc) => sc.OpenCount = Mathf.Max(0, sc.OpenCount - 1);

        void Fail(Scenario sc, string message)
        {
            sc.Failed = true;
            sc.Errors.Add("实验室：" + message);
            Debug.Log($"[lab] FAIL {sc.Id}: {message}");
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            var error = type != LogType.Warning;
            var line = message.Trim();
            if (type == LogType.Exception && !string.IsNullOrEmpty(stack))
                line += "（" + string.Join(" ← ", stack.Split('\n').Where(l => l.Trim().Length > 0).Take(3).Select(l => l.Trim())) + "）";
            var open = scenarios.Where(s => s.OpenCount > 0).ToList();
            if (open.Count == 0) groupLog.Add($"{(error ? "错误" : "警告")}（{currentGroup}，不在哪个场景的拍摄中）：{line}");
            foreach (var s in open) (error ? s.Errors : s.Warnings).Add(line);
            if (error && open.Count == 0) runErrors.Add($"{currentGroup}：{line}");
        }

        // ------------------------------------------------------------------ running

        // Steps through a coroutine and the ones it yields, so an exception ends this one only: the open scenarios
        // fail, `recover` puts the game back where the next can start, and the run goes on.
        IEnumerator Guarded(string what, IEnumerator body, Func<IEnumerator> recover)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            Exception error = null;
            while (stack.Count > 0)
            {
                object current = null;
                var moved = false;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) current = stack.Peek().Current;
                }
                catch (Exception e)
                {
                    error = e;
                }
                if (error != null) break;
                if (!moved)
                {
                    stack.Pop();
                    continue;
                }
                if (current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return current;
            }
            if (error == null) yield break;
            Crash(what, error);
            Release();
            if (recover != null) yield return Guarded(what + "（恢复）", recover(), null);
        }

        void Crash(string what, Exception e)
        {
            var message = e is LabFailure || e is TimeoutException ? e.Message : $"{e.GetType().Name}: {e.Message}";
            var open = scenarios.Where(s => s.OpenCount > 0).ToList();
            foreach (var s in open)
            {
                Fail(s, $"{what} 中断：{message}");
                s.OpenCount = 0;
            }
            if (open.Count == 0) runErrors.Add($"{what} 中断：{message}");
            Debug.Log($"[lab] {what} crashed: {e}");
        }

        // Back to a screen the next group can start from: out of the battle and the maze, transitions done.
        IEnumerator Recover()
        {
            Release();
            if (game.InBattle)
            {
                game.Battle.Abort();
                yield return Until(() => !game.InBattle, 30f, "恢复：退出战斗");
            }
            if (game.InDungeon)
            {
                game.Dungeon.Abort();
                yield return Until(() => !game.InDungeon, 30f, "恢复：离开迷宫");
            }
            yield return Until(() => !game.Transitions.Busy, 30f, "恢复：转场结束");
            yield return Frames(5);
        }

        // Films still rolling when a group ends finish (a paused one is written as far as it got).
        IEnumerator Flush()
        {
            foreach (var f in films.Where(f => f.Paused).ToList())
            {
                f.Sc.Notes.Add($"{f.Name}：暂停后没有恢复，只写出了前 {f.Taken} 帧");
                Finish(f);
            }
            var end = Time.unscaledTime + 15f;
            while (films.Count > 0 && Time.unscaledTime < end) yield return null;
            foreach (var f in films.ToList())
            {
                Fail(f.Sc, $"{f.Name} 没拍完（{f.Taken}/{f.Count} 帧）");
                Finish(f);
            }
            foreach (var s in scenarios.Where(s => s.Group == currentGroup))
            {
                if (s.Selected && !s.Captured && !s.Failed) Fail(s, "这一组跑完了也没有拍到");
                s.OpenCount = 0;
            }
        }

        Job Run(Awaitable a)
        {
            var job = new Job();
            Go(a, job);
            return job;
        }

        static async void Go(Awaitable a, Job job)
        {
            try { await a; }
            catch (Exception e) { job.Error = e; }
            finally { job.Done = true; }
        }

        // Someone's steps, frame by frame, for a jump (a figure put somewhere instead of walking there): the longest
        // single-frame move and how fast that was. NoteSteps stops it and puts it on the scenario.
        sealed class StepLog
        {
            public Transform Who;
            public Vector3 Last;
            public float Longest, Speed, Start, When;
            public bool Stop;
        }

        StepLog WatchSteps(Transform who)
        {
            var log = new StepLog { Who = who, Last = who.position, Start = Time.unscaledTime };
            StartCoroutine(Trace(log));
            return log;
        }

        static IEnumerator Trace(StepLog log)
        {
            while (!log.Stop && log.Who != null)
            {
                yield return null;
                var p = log.Who.position;
                var d = new Vector2(p.x - log.Last.x, p.z - log.Last.z).magnitude;
                log.Last = p;
                if (d <= log.Longest) continue;
                log.Longest = d;
                log.Speed = d / Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                log.When = Time.unscaledTime - log.Start;
            }
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // Over half a metre in one frame and faster than anyone runs, even hurried: a jump.
        void NoteSteps(Scenario sc, StepLog log, string what)
        {
            log.Stop = true;
            sc.Notes.Add($"{what}：DeepSeek 一帧最多走 {log.Longest:0.00} 米（{log.Speed:0.0} 米/秒，第 {log.When:0.00} 秒）");
            if (log.Longest > 0.5f && log.Speed > 25f) Fail(sc, $"{what}：DeepSeek 一帧跳了 {log.Longest:0.00} 米");
        }

        IEnumerator Await(Awaitable a, float seconds, string what)
        {
            var job = Run(a);
            yield return Until(() => job.Done, seconds, what);
            if (job.Error != null) throw new LabFailure($"{what}：{job.Error.GetType().Name}: {job.Error.Message}");
        }

        static IEnumerator Wait(float seconds)
        {
            var end = Time.unscaledTime + seconds - 0.001f;
            while (Time.unscaledTime < end) yield return null;
        }

        static IEnumerator Frames(int n)
        {
            for (var i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Until(Func<bool> condition, float seconds, string what)
        {
            var end = Time.unscaledTime + seconds;
            while (!condition())
            {
                if (Time.unscaledTime > end) throw new TimeoutException($"等了 {seconds:0} 秒（游戏时间）也没等到：{what}");
                yield return null;
            }
        }

        // ------------------------------------------------------------------ input

        IEnumerator Press(Key key)
        {
            kb.MakeCurrent();
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            yield return Wait(0.1f);
        }

        // The left stick at `move` (x right, y forward from the camera, 0..1 after the dead zone) and sprint held.
        void Stick(Vector2 move, bool sprint)
        {
            var m = Mathf.Clamp01(move.magnitude);
            var raw = m < 0.001f ? Vector2.zero : move.normalized * (m > 0.999f ? 1f : dzMin + m * (dzMax - dzMin));
            pad.MakeCurrent();
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = raw, rightTrigger = sprint ? 1f : 0f });
        }

        void Release()
        {
            if (pad != null) InputSystem.QueueStateEvent(pad, new GamepadState());
            if (kb != null) InputSystem.QueueStateEvent(kb, new KeyboardState());
        }

        // The stick for heading `dir` in the world, from where the maze's camera looks.
        Vector2 StickFor(Vector3 dir, float magnitude)
        {
            var yaw = World.CameraYaw * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            dir.y = 0f;
            dir.Normalize();
            return new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, forward)) * magnitude;
        }

        // ------------------------------------------------------------------ filming

        Film StartFilm(Scenario sc, string name, float interval, int count, params View[] views)
        {
            var f = new Film
            {
                Sc = sc, Name = name ?? sc.Id, Step = Mathf.Max(1, Mathf.RoundToInt(interval * Fps)), Count = count,
                Views = views.ToList(), Capture = sc.Selected,
            };
            foreach (var v in f.Views) v.Frames.Clear();
            poseTag = f.Name;
            films.Add(f);
            Begin(sc);
            return f;
        }

        IEnumerator WaitFilm(Film f) => Until(() => f.Done, f.Count * f.Interval + 5f, "拍完 " + f.Name);

        void Resume(Film f)
        {
            f.Paused = false;
            f.T0 = Time.unscaledTime - f.Taken * f.Interval;
        }

        IEnumerator Shoot(Scenario sc, string name, string what, Camera cam = null, Func<CamPose> pose = null)
        {
            poseTag = name; // the pose log's rows from here are this still's, not the last film's
            var s = new Still { Sc = sc, Name = name, What = what, Cam = cam, Pose = pose };
            Begin(sc);
            if (!sc.Selected)
            {
                End(sc);
                yield break;
            }
            stills.Add(s);
            yield return Until(() => s.Done, 5f, "静帧 " + name);
            End(sc);
        }

        static View ScreenView(string what = "游戏画面") => new View { What = what, IsScreen = true };

        // The battle camera as it is, without the interface on top (the portraits and plates hide part of the field).
        Func<View> Bare(string what = "同一镜头、不带界面") => () => CamView("3d", what, Stage.Camera, null);

        // From the field's side, square to the line between a noise body and a member, far enough to see both.
        Func<View> FieldSide(int foe, int member, string what) => () =>
        {
            var st = Stage;
            var cam = st.Camera;
            return CamView("side", what, cam, () =>
            {
                var m = st.MemberPoint(member);
                var e = st.FoePoint(foe);
                var floor = m.y - 1.15f;
                var mid = (m + e) * 0.5f;
                var along = e - m;
                along.y = 0f;
                var across = Vector3.Cross(Vector3.up, along.normalized);
                if (Vector3.Dot(across, st.transform.right) < 0f) across = -across;
                mid.y = floor + 1.0f;
                return new CamPose { At = mid + across * (along.magnitude * 0.75f + 2.5f) + Vector3.up * 0.6f, Look = mid, Fov = 45f };
            });
        };

        static View CamView(string tag, string what, Camera cam, Func<CamPose> pose) => new View { Tag = tag, What = what, Cam = cam, Pose = pose };

        IEnumerator Recorder()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                try
                {
                    CaptureFrame();
                    foreach (var f in writing.Where(f => f.Pending == 0).ToList())
                    {
                        writing.Remove(f);
                        if (f.Broken) Fail(f.Sc, f.Name + "：有帧从显卡读回失败");
                        else Write(f);
                    }
                }
                catch (Exception e)
                {
                    runErrors.Add("拍摄出错：" + e.Message);
                    Debug.Log("[lab] recorder: " + e);
                    foreach (var f in films.ToList())
                    {
                        Fail(f.Sc, "拍摄出错：" + e.Message);
                        f.Capture = false;
                        Finish(f);
                    }
                    foreach (var s in stills) s.Done = true;
                    stills.Clear();
                }
            }
        }

        // At the end of every frame: each rolling film whose next time has come takes its views (the screen once
        // for all of them, each camera view rendered from its own place), read back from the GPU without a stall.
        void CaptureFrame()
        {
            var now = Time.unscaledTime;
            var slot = 0;
            var screenGrabbed = false;
            foreach (var f in films.ToList())
            {
                if (f.Done || f.Paused) continue;
                if (f.T0 < 0f) f.T0 = now;
                else f.Hitch = Mathf.Max(f.Hitch, Time.unscaledDeltaTime);
                var due = f.T0 + f.Taken * f.Interval;
                if (now < due - 0.5f / Pace) continue;
                f.Late = Mathf.Max(f.Late, now - due);
                if (f.Capture)
                    foreach (var v in f.Views)
                    {
                        RenderTexture src;
                        bool flip;
                        if (v.IsScreen)
                        {
                            if (!screenGrabbed) GrabScreen();
                            screenGrabbed = true;
                            src = midRT;
                            flip = flipScreen;
                        }
                        else if (v.Cam == null)
                        {
                            while (v.Frames.Count <= f.Taken) v.Frames.Add(null);
                            continue;
                        }
                        else
                        {
                            Ensure(ref camRT, FrameW * 2, FrameH * 2, 24, true);
                            RenderPosed(v.Cam, v.Pose?.Invoke(), camRT);
                            src = camRT;
                            flip = flipCam;
                        }
                        Readback(f, v, f.Taken, src, Small(slot++), flip);
                    }
                f.Taken++;
                if (f.Taken >= f.Count) Finish(f);
                else if (f.PauseAt > 0 && f.Taken == f.PauseAt) f.Paused = true;
            }
            foreach (var s in stills.ToList())
            {
                stills.Remove(s);
                var file = s.Name + ".png";
                if (s.Cam == null)
                {
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    int w = tex.width, h = tex.height;
                    var px = tex.GetPixels32();
                    Destroy(tex);
                    s.Sc.Outputs.Add($"`{file}` — {s.What}（静帧 {w}×{h}，带界面）");
                    Encode(px, w, h, file);
                    s.Done = true;
                }
                else
                {
                    int w = Screen.width, h = Screen.height;
                    Ensure(ref fullRT, w, h, 24, true);
                    Ensure(ref fullFlat, w, h, 0, false);
                    RenderPosed(s.Cam, s.Pose?.Invoke(), fullRT);
                    Graphics.Blit(fullRT, fullFlat);
                    s.Sc.Outputs.Add($"`{file}` — {s.What}（静帧 {w}×{h}，只有 3D 画面）");
                    var still = s;
                    AsyncGPUReadback.Request(fullFlat, 0, TextureFormat.RGBA32, r =>
                    {
                        if (r.hasError) Fail(still.Sc, file + " 从显卡读回失败");
                        else
                        {
                            var px = r.GetData<Color32>().ToArray();
                            if (flipCam) FlipRows(px, w, h);
                            Encode(px, w, h, file);
                        }
                        still.Done = true;
                    });
                }
            }
        }

        void Readback(Film f, View v, int index, RenderTexture src, RenderTexture small, bool flip)
        {
            Graphics.Blit(src, small);
            while (v.Frames.Count <= index) v.Frames.Add(null);
            f.Pending++;
            AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32, r =>
            {
                f.Pending--;
                if (r.hasError)
                {
                    f.Broken = true;
                    return;
                }
                var px = r.GetData<Color32>().ToArray();
                if (flip) FlipRows(px, FrameW, FrameH);
                v.Frames[index] = px;
            });
        }

        static void FlipRows(Color32[] px, int w, int h)
        {
            var row = new Color32[w];
            for (int top = 0, bottom = h - 1; top < bottom; top++, bottom--)
            {
                Array.Copy(px, top * w, row, 0, w);
                Array.Copy(px, bottom * w, px, top * w, w);
                Array.Copy(row, 0, px, bottom * w, w);
            }
        }

        void Finish(Film f)
        {
            if (f.Done) return;
            f.Done = true;
            films.Remove(f);
            var keep = f.Capture && f.Taken > 0;
            if (keep && f.Valid != null && !f.Valid())
            {
                keep = false;
                f.Rejected = true;
                f.Sc.Notes.Add($"有一次取景作废重拍：{f.Invalid}");
            }
            if (keep)
            {
                f.Sc.Kept = true;
                writing.Add(f);
                if (f.Late > 1.5f / Pace || f.Hitch > 2.5f / Pace)
                    f.Sc.Notes.Add($"{f.Name}：有帧卡顿，帧最多比它的时间点晚 {f.Late * 1000f:0} 毫秒，最长的一帧 {f.Hitch * 1000f:0} 毫秒");
            }
            try { f.OnDone?.Invoke(); }
            catch (Exception e) { Fail(f.Sc, "拍完后的处理出错：" + e.Message); }
            End(f.Sc);
        }

        void Write(Film f)
        {
            foreach (var v in f.Views)
            {
                var frames = v.Frames.ToList();
                var parts = (frames.Count + PerSheet - 1) / PerSheet;
                for (var p = 0; p < parts; p++)
                {
                    var chunk = frames.Skip(p * PerSheet).Take(PerSheet).ToList();
                    var file = f.Name + (v.Tag != null ? "-" + v.Tag : "") + (parts > 1 ? "-" + (char)('a' + p) : "") + ".png";
                    var first = p * PerSheet;
                    var last = first + chunk.Count - 1;
                    f.Sc.Outputs.Add($"`{file}` — {v.What}：第 {first + 1}–{last + 1} 帧，每帧间隔 {f.Interval:0.000} 秒（t = {first * f.Interval:0.00}–{last * f.Interval:0.00} 秒）");
                    var rows = (chunk.Count + Columns - 1) / Columns;
                    Interlocked.Increment(ref pendingWrites);
                    var path = Path.Combine(outDir, file);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            int W = FrameW * Columns, H = FrameH * rows;
                            var px = new Color32[W * H];
                            for (var i = 0; i < px.Length; i++) px[i] = new Color32(12, 12, 16, 255);
                            for (var i = 0; i < chunk.Count; i++)
                            {
                                var src = chunk[i];
                                if (src == null) continue;
                                int ox = i % Columns * FrameW, oy = (rows - 1 - i / Columns) * FrameH;
                                for (var y = 0; y < FrameH; y++) Array.Copy(src, y * FrameW, px, (oy + y) * W + ox, FrameW);
                            }
                            for (var i = 0; i < px.Length; i++) px[i].a = 255;
                            File.WriteAllBytes(path, ImageConversion.EncodeArrayToPNG(px, GraphicsFormat.R8G8B8A8_SRGB, (uint)W, (uint)H));
                        }
                        catch (Exception e)
                        {
                            lock (writeErrors) writeErrors.Add($"{file}：{e.Message}");
                        }
                        finally
                        {
                            Interlocked.Decrement(ref pendingWrites);
                        }
                    });
                }
                v.Frames.Clear();
            }
        }

        void Encode(Color32[] px, int w, int h, string file)
        {
            Interlocked.Increment(ref pendingWrites);
            var path = Path.Combine(outDir, file);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    for (var i = 0; i < px.Length; i++) px[i].a = 255;
                    File.WriteAllBytes(path, ImageConversion.EncodeArrayToPNG(px, GraphicsFormat.R8G8B8A8_SRGB, (uint)w, (uint)h));
                }
                catch (Exception e)
                {
                    lock (writeErrors) writeErrors.Add($"{file}：{e.Message}");
                }
                finally
                {
                    Interlocked.Decrement(ref pendingWrites);
                }
            });
        }

        static void Ensure(ref RenderTexture rt, int w, int h, int depth, bool msaa)
        {
            if (rt != null && rt.width == w && rt.height == h) return;
            if (rt != null) rt.Release();
            rt = new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32) { antiAliasing = msaa ? Mathf.Max(1, QualitySettings.antiAliasing) : 1 };
            rt.Create();
        }

        // One small target per view taken in a frame, so a readback never sees the next view's pixels.
        RenderTexture Small(int slot)
        {
            while (smallRTs.Count <= slot)
            {
                var rt = new RenderTexture(FrameW, FrameH, 0, RenderTextureFormat.ARGB32);
                rt.Create();
                smallRTs.Add(rt);
            }
            return smallRTs[slot];
        }

        // The screen as shown, interface included, halved first so the last step down to 480×270 is a clean 2×.
        void GrabScreen()
        {
            Ensure(ref screenRT, Screen.width, Screen.height, 0, false);
            Ensure(ref midRT, FrameW * 2, FrameH * 2, 0, false);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(screenRT);
            Graphics.Blit(screenRT, midRT);
        }

        // One frame of the camera from another place, put back as it was.
        static void RenderPosed(Camera cam, CamPose pose, RenderTexture target)
        {
            var t = cam.transform;
            var position = t.position;
            var rotation = t.rotation;
            var fov = cam.fieldOfView;
            var before = cam.targetTexture;
            try
            {
                if (pose != null)
                {
                    var look = pose.Look - pose.At;
                    t.SetPositionAndRotation(pose.At, look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look) : rotation);
                    if (pose.Fov > 0f) cam.fieldOfView = pose.Fov;
                }
                cam.targetTexture = target;
                cam.Render();
            }
            finally
            {
                cam.targetTexture = before;
                t.SetPositionAndRotation(position, rotation);
                cam.fieldOfView = fov;
            }
        }

        // ------------------------------------------------------------------ the game's insides

        static FieldInfo Field(Type t, string name, BindingFlags flags = Any) =>
            t.GetField(name, flags) ?? throw new MissingMemberException(t.Name, name);

        static MethodInfo Method(Type t, string name, params Type[] args) =>
            t.GetMethod(name, Any, null, args, null) ?? throw new MissingMemberException(t.Name, name);

        void Bind()
        {
            var bs = typeof(BattleScreen);
            fBattleMode = Field(bs, "mode");
            fActor = Field(bs, "actor");
            fEnemies = Field(bs, "enemies");
            fParty = Field(bs, "party");
            fStage = Field(bs, "stage");
            fTargets = Field(bs, "targets");
            fTargetIndex = Field(bs, "targetIndex");
            mApplyHit = Method(bs, "ApplyHit", typeof(Combatant), typeof(Hit), typeof(bool), typeof(bool));
            mEnemyBar = Method(bs, "EnemyBar", typeof(EnemyView), typeof(string));
            fStrikes = Field(typeof(BattleStage), "strikes", BindingFlags.Static | BindingFlags.NonPublic);
            fShots = Field(typeof(BattleStage), "shots", BindingFlags.Static | BindingFlags.NonPublic);
            var ds = typeof(DungeonScreen);
            fDungeonMode = Field(ds, "mode");
            fMeter = Field(ds, "meter");
            fVisited = Field(ds, "visited");
            fPitch = Field(typeof(DungeonWorld), "pitch");
            fCameraDistance = Field(typeof(DungeonWorld), "cameraDistance");
            pPlayerPosition = typeof(DungeonWorld).GetProperty("PlayerPosition", Any) ?? throw new MissingMemberException("DungeonWorld", "PlayerPosition");
            mUpdateCamera = Method(typeof(DungeonWorld), "UpdateCamera", typeof(float), typeof(bool));
        }

        // UI Toolkit's timers and transitions run on their own clock (the real one); the lab puts them on game
        // time, so the plates' twitches and the styles' transitions come out the same every run.
        static long LabMs() => (long)(Time.unscaledTimeAsDouble * 1000.0);

        static bool HookPanelClock()
        {
            try
            {
                var panel = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel");
                var p = panel?.GetProperty("TimeSinceStartup", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var set = p?.GetSetMethod(true);
                if (set == null) return false;
                var d = Delegate.CreateDelegate(p.PropertyType, typeof(VisualLab).GetMethod(nameof(LabMs), BindingFlags.Static | BindingFlags.NonPublic));
                set.Invoke(null, new object[] { d });
                return true;
            }
            catch (Exception e)
            {
                Debug.Log("[lab] panel clock: " + e.Message);
                return false;
            }
        }

        bool Sandboxed(out string why)
        {
            var home = Environment.GetEnvironmentVariable("SP_LAB_HOME");
            var data = Application.persistentDataPath;
            static string Norm(string p)
            {
                p = p.Replace('\\', '/').TrimEnd('/');
                return p.StartsWith("/private/", StringComparison.Ordinal) ? p.Substring("/private".Length) : p;
            }
            if (string.IsNullOrEmpty(home))
            {
                why = "没有 SP_LAB_HOME：实验室只能从 game/Tools/lab.sh 启动（它把 HOME 指到一个临时文件夹，存档和设置不会碰到真的）";
                return false;
            }
            if (!Norm(data).StartsWith(Norm(home) + "/", StringComparison.Ordinal))
            {
                why = $"游戏的数据文件夹 {data} 不在临时 HOME（{home}）里，为了不碰真的存档和设置，没有开始";
                return false;
            }
            why = null;
            return true;
        }

        BattleScreen Bs => game.Battle;
        string BattleMode => fBattleMode.GetValue(Bs).ToString();
        BattleStage Stage => (BattleStage)fStage.GetValue(Bs);
        List<EnemyView> Enemies => (List<EnemyView>)fEnemies.GetValue(Bs);
        List<PartyCard> PartyCards => (List<PartyCard>)fParty.GetValue(Bs);
        int Actor => (int)fActor.GetValue(Bs);
        List<Combatant> Targets => (List<Combatant>)fTargets.GetValue(Bs);
        int TargetIndex => (int)fTargetIndex.GetValue(Bs);
        Combatant Member(string id) => PartyCards.First(c => c.Unit.Id == id).Unit;
        Awaitable ApplyHit(Combatant c, Hit hit) => (Awaitable)mApplyHit.Invoke(Bs, new object[] { c, hit, false, false });
        Awaitable EnemyBar(int foe, string name) => (Awaitable)mEnemyBar.Invoke(Bs, new object[] { Enemies[foe], name });
        int PartyHp => PartyCards.Sum(c => c.Unit.Hp);

        DungeonWorld World => game.Dungeon.World;
        void SetMode(string m) => fDungeonMode.SetValue(game.Dungeon, Enum.Parse(fDungeonMode.FieldType, m));
        Vector3 Cell(int x, int y) => World.Map.World(new Vector2Int(x, y));
        SeaNoise Noise(char mark) => World.Enemies.First(e => e.Spawn.Mark == mark);
        static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // ------------------------------------------------------------------ the party

        void Prepare(string gender, bool deepSeek, bool awakened, params (string Id, int Count)[] items)
        {
            var s = game.State;
            s.playerName = PlayerName;
            s.playerGender = gender;
            s.party.Clear();
            s.members.Clear();
            s.forms.Clear();
            s.inventory.Clear();
            s.pearls.Clear();
            s.abilities.Clear();
            s.party.Add(GameState.PlayerId);
            if (deepSeek) s.party.Add("DeepSeek");
            foreach (var id in s.party)
            {
                var m = s.Member(id);
                var stats = CombatStats.ForCharacter(id, 3);
                m.level = 3;
                m.exp = CombatStats.ExpForLevel(3);
                m.hp = stats.MaxHp;
                m.sp = stats.MaxSp;
            }
            if (awakened) s.Awaken("DeepSeek", "battle");
            foreach (var (id, count) in items) s.AddItem(id, count);
        }

        static EnemyDef Foe(string kind, int hp) => kind switch
        {
            "repost" => new EnemyDef { Kind = "repost", Name = "转发型", Hp = hp, Weak = BElement.Ice, Resist = BElement.Wind },
            "coldfact" => new EnemyDef { Kind = "coldfact", Name = "冷知识型", Hp = hp, Weak = BElement.Fire, Resist = BElement.Ice },
            _ => new EnemyDef { Kind = "clickbait", Name = "标题党型", Hp = hp, Weak = BElement.Ice, Resist = BElement.Fire, FakeWeakness = "火" },
        };

        static BattleDef Lab(string id, params EnemyDef[] foes) =>
            new BattleDef { Id = id, Enemies = foes, Exp = 0, Drops = new (string, int)[0], EnemyDamageScale = 0.25f };

        // The 截图 and the 饭勺 go through their ways in turn, counted across battles: each battle group starts both
        // from the first.
        void ResetStyles()
        {
            fStrikes.SetValue(null, 0);
            fShots.SetValue(null, 0);
        }

        // ------------------------------------------------------------------ battle: menus

        bool CommandSelected(string label) =>
            ui.Query(className: "sp-command").ToList().Any(c => c.ClassListContains("is-selected") && c.Q<Label>(className: "sp-command__text")?.text == label);

        string RowSelected(string box) =>
            ui.Q(className: box)?.Query(className: "sp-skillrow").ToList().FirstOrDefault(r => r.ClassListContains("is-selected"))?.Q<Label>(className: "sp-skillrow__name")?.text;

        IEnumerator InMode(string mode, string what) => Until(() => game.InBattle && BattleMode == mode, 10f, what);

        IEnumerator PickCommand(string label)
        {
            yield return InMode("Command", "指令菜单");
            for (var i = 0; i < 6 && !CommandSelected(label); i++) yield return Press(Key.DownArrow);
            for (var i = 0; i < 6 && !CommandSelected(label); i++) yield return Press(Key.UpArrow);
            if (!CommandSelected(label)) throw new LabFailure($"指令菜单里选不到「{label}」");
            yield return Press(Key.Enter);
        }

        IEnumerator PickRow(string box, string mode, string name)
        {
            yield return InMode(mode, name + " 的列表");
            for (var i = 0; i < 8 && RowSelected(box) != name; i++) yield return Press(Key.DownArrow);
            if (RowSelected(box) != name) throw new LabFailure($"列表里选不到「{name}」");
        }

        IEnumerator PickTarget(Combatant who)
        {
            yield return InMode("Target", "选目标");
            for (var i = 0; i < 8 && Targets[TargetIndex] != who; i++) yield return Press(Key.RightArrow);
            if (Targets[TargetIndex] != who) throw new LabFailure($"选不到目标 {who.Name}");
        }

        string ActorId => PartyCards[Actor].Unit.Id;

        // With a member wherever they go, off their side and a little behind (ahead of them the noise bodies would
        // stand in the lens), a little above their chest: a run across the field and the way back, seen all the way.
        Func<View> Beside(int foe, int member, string what) => () =>
        {
            var st = Stage;
            Vector3? off = null;
            return CamView("side", what, st.Camera, () =>
            {
                var m = st.MemberPoint(member);
                if (off == null)
                {
                    var along = st.FoePoint(foe) - m;
                    along.y = 0f;
                    along.Normalize();
                    var side = Vector3.Cross(Vector3.up, along);
                    if (Vector3.Dot(side, st.transform.right) < 0f) side = -side;
                    off = side * 3.4f - along * 1.6f + Vector3.up * 0.5f;
                }
                return new CamPose { At = m + off.Value, Look = m + Vector3.down * 0.2f, Fov = 42f };
            });
        };

        IEnumerator Attack(int foe, Scenario sc, float interval, int count, params Func<View>[] more)
        {
            var label = ActorId == GameState.PlayerId ? "攻击（截图）" : "攻击";
            yield return PickCommand(label);
            var target = Enemies[foe].Unit;
            yield return PickTarget(target);
            Landed(Roll(sc, interval, count, more), target);
            yield return Press(Key.Enter);
        }

        IEnumerator Skill(string name, int foe, Scenario sc, float interval, int count, Func<bool> valid = null, string invalid = null)
        {
            yield return PickCommand("技能");
            yield return PickRow("sp-battle__skills", "Skill", name);
            Combatant target = null;
            if (foe >= 0)
            {
                yield return Press(Key.Enter);
                target = Enemies[foe].Unit;
                yield return PickTarget(target);
            }
            var f = Roll(sc, interval, count);
            if (f != null && valid != null)
            {
                f.Valid = valid;
                f.Invalid = invalid;
            }
            else if (target != null) Landed(f, target);
            yield return Press(Key.Enter);
        }

        // A skill cast on the charge of 深度思考: should a MISS have spent it, the retake thinks again first (that
        // turn unfilmed) and casts on the next.
        IEnumerator Charged(string name, int foe, Scenario sc, float interval, int count)
        {
            if (Member(ActorId).Charged)
            {
                yield return Skill(name, foe, sc, interval, count);
                yield break;
            }
            retake?.Invoke();
            yield return Command("深度思考", null, 0f, 0);
        }

        IEnumerator Item(string name, Combatant target, Scenario sc, float interval, int count, bool hurts = false)
        {
            yield return PickCommand("道具");
            yield return PickRow("sp-battle__items", "Item", name);
            yield return Press(Key.Enter);
            yield return PickTarget(target);
            var f = Roll(sc, interval, count);
            if (hurts) Landed(f, target);
            yield return Press(Key.Enter);
        }

        // An attack, and a second film of its blow landing close up in time: a 30th of a second a frame from `delay`
        // after `start` first holds (the 饭勺's trail made as she sets off, the viewfinder up), the moment of contact
        // some way in. The attack's own film retakes a MISS, which brings this one round again with it.
        IEnumerator Close(IEnumerator attack, Scenario sc, Func<bool> start, float delay, Combatant target)
        {
            yield return attack;
            if (!Wanted(sc) || Rolling(sc)) yield break;
            yield return Until(start, 10f, sc.Id + " 的起点");
            yield return Wait(delay);
            Landed(StartFilm(sc, null, 1f / Fps, 24, ScreenView(), Bare()()), target);
        }

        bool OnStage(string name) => Stage != null && Stage.GetComponentsInChildren<Transform>().Any(t => t.name == name);

        // An attack's take is kept only when it took HP off: a MISS is filmed again on the actor's next turn.
        static void Landed(Film f, Combatant target)
        {
            if (f == null) return;
            var hp = target.Hp;
            f.Valid = () => target.Hp < hp;
            f.Invalid = $"打 {target.Name} 没打中（MISS）";
        }

        IEnumerator Command(string label, Scenario sc, float interval, int count)
        {
            yield return InMode("Command", "指令菜单");
            for (var i = 0; i < 6 && !CommandSelected(label); i++) yield return Press(Key.DownArrow);
            for (var i = 0; i < 6 && !CommandSelected(label); i++) yield return Press(Key.UpArrow);
            if (!CommandSelected(label)) throw new LabFailure($"指令菜单里选不到「{label}」");
            Roll(sc, interval, count);
            yield return Press(Key.Enter);
        }

        IEnumerator QuickGuard() => Command("防御", null, 0f, 0);

        Film Roll(Scenario sc, float interval, int count, params Func<View>[] more)
        {
            if (sc == null) return null;
            var views = new List<View> { ScreenView() };
            views.AddRange(more.Select(m => m()));
            var f = StartFilm(sc, null, interval, count, views.ToArray());
            var again = retake;
            f.OnDone = () =>
            {
                if (f.Rejected) again?.Invoke();
            };
            return f;
        }

        // Puts the step that is running back at the front of its actor's list, the way of 截图 or 饭勺 it took
        // wound back so the retake shows the same one (set by Drive while a step runs).
        Action retake;

        IEnumerator Redo(string actor, int strikes, int shots, Func<IEnumerator> step)
        {
            if (actor == GameState.PlayerId) fShots.SetValue(null, shots);
            else fStrikes.SetValue(null, strikes);
            yield return step();
        }

        // Side-on to a member where they stand on the field (side -1 their left, 1 their right), at hip height.
        CamPose BattleSide(int member, float side)
        {
            var st = Stage;
            var feet = st.MemberPoint(member) - Vector3.up * 1.15f;
            var at = feet + st.transform.right * (3.0f * side) + st.transform.forward * 0.4f + Vector3.up * 1.0f;
            return new CamPose { At = at, Look = feet + Vector3.up * 0.9f, Fov = 38f };
        }

        // Down on a member lying on the seabed, from above their feet and to one side: the whole body in the frame.
        CamPose LyingView(int member, float side)
        {
            var st = Stage;
            var hips = st.MemberHips(member);
            hips.y = st.transform.position.y;
            var at = hips + st.transform.right * (1.5f * side) + st.transform.forward * 1.3f + Vector3.up * 2.1f;
            return new CamPose { At = at, Look = hips + Vector3.up * 0.15f, Fov = 46f };
        }

        Func<View> SideView(int member, float side, string what)
        {
            return () =>
            {
                var cam = Stage.Camera;
                return CamView("side", what, cam, () => BattleSide(member, side));
            };
        }

        IEnumerator Stance(Scenario sc, int member, float side)
        {
            yield return InMode("Command", "指令菜单");
            yield return Wait(0.6f); // the camera settles over the shoulder
            var f = StartFilm(sc, null, 0.2f, 16, ScreenView("指令镜头（游戏画面）"), SideView(member, side, "侧面检查镜头")());
            yield return WaitFilm(f);
            // The stance from close by, the size of the window: the whole figure from ahead and to the side it holds
            // its hands out on, the hands and what they hold, and from behind as the battle's cameras see it.
            var st = Stage;
            CamPose From(Vector3 way, float metres, float height, float fov) => new CamPose
            {
                At = st.MemberPoint(member) + way.normalized * metres + Vector3.up * (height - 1.15f), Look = st.MemberPoint(member) + Vector3.up * (height - 1.15f), Fov = fov,
            };
            // The hands and the face where the figure has them (the stance leans, and DeepSeek is a head shorter).
            CamPose Onto(Vector3 point, Vector3 way, float metres, float fov) => new CamPose { At = point + way.normalized * metres, Look = point, Fov = fov };
            Vector3 Hands() => (st.MemberBone(member, HumanBodyBones.LeftHand) + st.MemberBone(member, HumanBodyBones.RightHand)) * 0.5f;
            Vector3 ahead = st.transform.forward, out_ = st.transform.right * side;
            foreach (var (name, what, pose) in new (string, string, Func<CamPose>)[]
            {
                ("close-front", "战斗待机：前方偏侧全身", () => From(ahead + out_ * 0.7f, 2.4f, 0.9f, 42f)),
                ("close-hands", "战斗待机：双手和手里的东西", () => Onto(Hands(), ahead + out_ * 0.9f + Vector3.up * 0.2f, 1.0f, 34f)),
                ("close-face", "战斗待机：脸", () => Onto(st.MemberBone(member, HumanBodyBones.Head) + Vector3.up * 0.06f, ahead + out_ * 0.4f, 0.8f, 30f)),
                ("close-back", "战斗待机：身后偏侧全身", () => From(-ahead + out_ * 0.6f, 2.4f, 0.9f, 42f)),
            })
                yield return Shoot(sc, $"{sc.Id}-{name}", what, st.Camera, pose);
        }

        IEnumerator Decline()
        {
            yield return Press(Key.RightArrow);
            yield return Press(Key.Enter);
        }

        bool Rolling(Scenario sc) => films.Any(f => f.Sc == sc);

        bool Wanted(Scenario sc) => sc.Selected && !sc.Captured && !sc.Failed;

        bool GroupDone(string group) => films.Count == 0 && scenarios.Where(s => s.Group == group).All(s => !Wanted(s));

        // Runs the battle's turns through the menus until the fight is won, lost or `Finished` says to stop.
        IEnumerator Drive(Fight fight, Job battle)
        {
            var seen = new HashSet<VisualElement>();
            var victory = false;
            var steps = 0;
            while (!battle.Done)
            {
                if (game.InBattle)
                {
                    var fx = ui.Q(className: "sp-battle__fx");
                    if (fx != null)
                        foreach (var bar in fx.Query(className: "sp-skillbar").ToList())
                        {
                            if (!bar.ClassListContains("is-enemy") || !seen.Add(bar)) continue;
                            var name = bar.Q<Label>(className: "sp-skillbar__name")?.text;
                            if (name != null && fight.OnBar.TryGetValue(name, out var react)) react();
                        }
                    if (!victory && fight.OnVictory != null && ui.Q(className: "sp-battle__party")?.Q(className: "is-victory") != null)
                    {
                        victory = true;
                        fight.OnVictory();
                    }
                }
                var mode = game.InBattle && !game.Transitions.Busy ? BattleMode : "";
                if (mode == "Command")
                {
                    if (fight.Finished() || ++steps > 40)
                    {
                        if (steps > 40) runErrors.Add($"{currentGroup}：40 个回合指令后还没拍完，提前结束");
                        game.Battle.Abort();
                        yield return Until(() => battle.Done, 30f, "中止战斗");
                        break;
                    }
                    var id = ActorId;
                    if (fight.Steps.TryGetValue(id, out var list) && list.Count > 0)
                    {
                        var step = list[0];
                        list.RemoveAt(0);
                        int strikes = (int)fStrikes.GetValue(null), shots = (int)fShots.GetValue(null);
                        retake = () => list.Insert(0, () => Redo(id, strikes, shots, step));
                        yield return step();
                        retake = null;
                    }
                    else yield return fight.Fallback != null ? fight.Fallback(id) : QuickGuard();
                    continue;
                }
                if (mode == "Prompt") yield return fight.OnPrompt != null ? fight.OnPrompt() : Decline();
                else if (mode == "Results") yield return fight.OnResults != null ? fight.OnResults() : Press(Key.Enter);
                yield return null;
            }
            if (battle.Error != null) throw new LabFailure("战斗流程出错：" + battle.Error.Message);
            Release();
            yield return Until(() => !game.InBattle && !game.Transitions.Busy, 30f, "战斗结束后回到原来的画面");
            yield return Frames(5);
        }

        static List<Func<IEnumerator>> Q(params Func<IEnumerator>[] steps) => new List<Func<IEnumerator>>(steps);

        // ------------------------------------------------------------------ battle: the main fight

        // The male protagonist and the awakened DeepSeek against 标题党型, 转发型, 标题党型 that can't fall: every
        // way in of 截图 and 饭勺, 防御, 深度思考, 寒流 charged and plain, the rice ball and both throws; the noise
        // bodies' turns give 震惊！, 转发保平安 and one landing on the player's guard.
        IEnumerator BattleMain()
        {
            Prepare("male", true, true, ("white_rice", 3), ("ice_soda", 3), ("spicy_strip", 2));
            ResetStyles();
            UnityEngine.Random.InitState(1101);
            var battle = Run(game.RunBattle(Lab("lab_main", Foe("clickbait", 999), Foe("repost", 999), Foe("clickbait", 999))));
            var opening = StartFilm(S("battle-opening"), null, 0.1f, 32, ScreenView());
            yield return Until(() => game.InBattle && Stage != null, 20f, "战斗开始");

            var fight = new Fight();
            fight.Steps[GameState.PlayerId] = Q(
                BattlePerf,
                () => Faces(S("battle-face-clickbait"), 0),
                () => Faces(S("battle-face-repost"), 1),
                () => Stance(S("battle-stance-player"), 0, -1f),
                () => Close(Attack(0, S("battle-shot-eye"), 0.1f, 16), S("battle-hitstop-shot"), () => ui.Q(className: "sp-viewfinder") != null, 0.15f, Enemies[0].Unit),
                () => Command("防御", S("battle-guard"), 0.1f, 12),
                () => Attack(2, S("battle-shot-reach"), 0.1f, 16),
                () => Attack(1, S("battle-shot-low"), 0.1f, 16),
                () => Item("白米饭", Member("DeepSeek"), S("battle-item-rice"), 0.1f, 16),
                () => Item("冰镇汽水", Enemies[1].Unit, S("battle-soda"), 0.133f, 32, true),
                () => Item("辣条", Enemies[0].Unit, S("battle-strip"), 0.133f, 32, true));
            fight.Steps["DeepSeek"] = Q(
                () => Stance(S("battle-stance-deepseek"), 1, 1f),
                () => Close(Attack(1, S("battle-spoon-slash"), 0.0667f, 40, Beside(1, 1, "跟在 DeepSeek 身侧的镜头（看冲过去和回位）")), S("battle-hitstop"), () => OnStage("Spoon Slash"), 0.85f, Enemies[1].Unit),
                () => Command("深度思考", S("battle-think"), 0.133f, 16),
                () => Charged("寒流", 0, S("battle-current-charged"), 0.133f, 32),
                () => Attack(1, S("battle-spoon-sweep"), 0.0667f, 40, Beside(1, 1, "跟在 DeepSeek 身侧的镜头（看冲过去和回位）")),
                () => Skill("寒流", 2, S("battle-current"), 0.133f, 32),
                () => Attack(1, S("battle-spoon-rush"), 0.0667f, 40, Beside(1, 1, "跟在 DeepSeek 身侧的镜头（看冲过去和回位）")),
                () => Attack(1, S("battle-spoon-leap"), 0.0667f, 40, Beside(1, 1, "跟在 DeepSeek 身侧的镜头（看冲过去和回位）")));
            // A hit on a member is only kept when it took HP off (a miss is filmed again on the next).
            fight.OnBar["震惊！"] = () =>
            {
                var sc = S("battle-bait");
                if (!Wanted(sc) || Rolling(sc)) return;
                var hp = PartyHp;
                var f = StartFilm(sc, null, 0.1f, 24, ScreenView());
                f.Valid = () => PartyHp < hp;
                f.Invalid = "这一下没打中（MISS）";
            };
            fight.OnBar["转发保平安"] = () =>
            {
                var player = Member(GameState.PlayerId);
                var sc = player.Guarding ? S("battle-guard-hit") : S("battle-forward");
                if (!Wanted(sc) || Rolling(sc)) return;
                var hp = player.Hp;
                var f = StartFilm(sc, null, 0.1f, 24, ScreenView());
                f.Valid = () => player.Hp < hp;
                f.Invalid = "男主角这次没被打中";
            };
            fight.Finished = () => GroupDone("battle-main")
                || films.Count == 0 && fight.Steps.Values.All(q => q.Count == 0) && !Wanted(S("battle-bait")) && !Wanted(S("battle-forward")) && !Wanted(S("battle-guard-hit"));
            yield return Drive(fight, battle);
            if (!opening.Done) yield return WaitFilm(opening);
        }

        // ------------------------------------------------------------------ battle: the finale

        // A weak 标题党型 that 寒流 finishes off, the 转发型 calling another in, 深海潜流 knocking all three down, the
        // all-out attack and the win.
        IEnumerator BattleFinale()
        {
            Prepare("male", true, true, ("ice_soda", 2));
            ResetStyles();
            UnityEngine.Random.InitState(1202);
            var battle = Run(game.RunBattle(Lab("lab_finale", Foe("repost", 200), Foe("clickbait", 8))));
            yield return Until(() => game.InBattle && Stage != null, 20f, "战斗开始");

            var fight = new Fight();
            fight.Steps[GameState.PlayerId] = Q();
            fight.Fallback = id => id == "DeepSeek" ? FinaleDeepSeek() : QuickGuard();
            fight.OnBar["转发到十个群"] = () =>
            {
                var sc = S("battle-summon");
                if (Rolling(sc) || sc.Captured) return;
                var f = StartFilm(sc, null, 0.1f, 24, ScreenView());
                // The one called in stands through 深海潜流, so all three go down and the all-out takes them.
                f.OnDone = () =>
                {
                    var called = Enemies.LastOrDefault(v => v.Unit.Summoned)?.Unit;
                    if (called == null) return;
                    called.MaxHp = called.Hp = 200;
                };
            };
            fight.OnPrompt = FinalePrompt;
            fight.OnVictory = () => StartFilm(S("battle-victory"), null, 0.1f, 32, ScreenView(), Bare("同一镜头、不带界面（看立绘挡住的欢呼）")());
            fight.OnResults = FinaleResults;
            fight.Finished = () => GroupDone("battle-finale");
            yield return Drive(fight, battle);
        }

        // Two at the start: with three the 转发型 calls none in (BattleScreen).
        IEnumerator FinaleDeepSeek()
        {
            var weak = Enemies[1].Unit;
            if (!weak.Dead)
            {
                yield return Skill("寒流", 1, S("battle-vanish"), 0.133f, 32, () => weak.Dead, "寒流没有打死它");
                yield break;
            }
            if (!Enemies.Any(v => v.Unit.Summoned))
            {
                yield return QuickGuard(); // the 转发型 calls one in on its turn
                yield break;
            }
            if (!S("battle-maelstrom").Captured && !Rolling(S("battle-maelstrom")))
            {
                yield return Skill("深海潜流", -1, S("battle-maelstrom"), 0.133f, 40);
                yield break;
            }
            var standing = Enemies.FindIndex(v => !v.Unit.Dead && !v.Unit.Down);
            if (standing >= 0) yield return Skill("寒流", standing, null, 0f, 0);
            else yield return QuickGuard();
        }

        IEnumerator FinalePrompt()
        {
            if (!S("battle-maelstrom").Captured)
            {
                yield return Decline();
                yield break;
            }
            foreach (var v in Enemies.Where(v => !v.Unit.Dead))
            {
                v.Unit.Hp = Mathf.Min(v.Unit.Hp, 30);
                v.Refresh();
            }
            StartFilm(S("battle-allout"), null, 0.133f, 48, ScreenView());
            yield return Press(Key.Enter);
        }

        IEnumerator FinaleResults()
        {
            var victory = S("battle-victory");
            yield return Until(() => !Rolling(victory), 10f, "胜利胶片拍完");
            yield return Wait(0.3f);
            yield return Press(Key.Enter);
        }

        // ------------------------------------------------------------------ battle: the female protagonist and the staged moves

        IEnumerator BattleFemale()
        {
            Prepare("female", true, true, ("white_rice", 2));
            ResetStyles();
            UnityEngine.Random.InitState(1303);
            var battle = Run(game.RunBattle(Lab("lab_female", Foe("coldfact", 999), Foe("clickbait", 999))));
            yield return Until(() => game.InBattle && Stage != null, 20f, "战斗开始");

            Film sheet = null;
            var fight = new Fight();
            fight.Steps[GameState.PlayerId] = Q(
                () => Faces(S("battle-face-coldfact"), 0),
                () => FemaleGuard(f => sheet = f),
                () => FemaleRice(() => sheet),
                FemaleFinish,
                FemaleKill);
            fight.Steps["DeepSeek"] = Q(StagedRound, StagedFaint);
            fight.Finished = () => GroupDone("battle-female") || fight.Steps.Values.All(q => q.Count == 0) && films.Count == 0;
            yield return Drive(fight, battle);
        }

        IEnumerator FemaleGuard(Action<Film> made)
        {
            yield return InMode("Command", "指令菜单");
            yield return Wait(0.6f);
            var f = StartFilm(S("battle-female"), null, 0.2f, 24, ScreenView("指令镜头（游戏画面）"), SideView(0, -1f, "侧面检查镜头")());
            f.PauseAt = 14;
            made(f);
            yield return Wait(1.2f);
            yield return Command("防御", null, 0f, 0);
        }

        IEnumerator FemaleRice(Func<Film> sheet)
        {
            yield return PickCommand("道具");
            yield return PickRow("sp-battle__items", "Item", "白米饭");
            yield return Press(Key.Enter);
            yield return PickTarget(Member(GameState.PlayerId));
            var f = sheet();
            if (f != null && f.Paused) Resume(f);
            yield return Press(Key.Enter);
        }

        // On the player's third turn, the menu hidden: a hit on the 标题党型's weakness with 减少闪烁 on and 画面震动
        // off, then the 冷知识型 finished off; then a guard.
        IEnumerator FemaleFinish()
        {
            yield return InMode("Command", "男主角的指令菜单");
            const int coldfact = 0, clickbait = 1;
            var st = Stage;
            HideMenu(true);
            try
            {
                game.Settings.reduceFlash = true;
                game.Settings.screenShake = false;
                st.Move(st.Watch(clickbait), 0.4f);
                yield return Wait(0.5f);
                var calm = StartFilm(S("battle-calm"), null, 0.05f, 16, ScreenView(), Bare()());
                yield return Wait(0.1f);
                yield return Await(ApplyHit(Enemies[clickbait].Unit, new Hit { Element = BElement.Ice, Damage = 20, Affinity = BAffinity.Weak }), 5f, "减少闪烁时的弱点命中");
                yield return WaitFilm(calm);
                game.Settings.reduceFlash = false;
                game.Settings.screenShake = true;

                st.Move(st.Watch(coldfact), 0.4f);
                yield return Wait(0.5f);
                var target = Enemies[coldfact].Unit;
                target.Hp = 1;
                var debunk = StartFilm(S("battle-debunk"), null, 0.06f, 16, ScreenView(), Bare()());
                yield return Wait(0.1f);
                yield return Await(ApplyHit(target, new Hit { Element = BElement.Physical, Damage = 34, Critical = true }), 5f, "打死冷知识型");
                yield return WaitFilm(debunk);
            }
            finally
            {
                game.Settings.reduceFlash = false;
                game.Settings.screenShake = true;
                HideMenu(false);
            }
            yield return QuickGuard();
        }

        // On the player's fourth turn, the 冷知识型 gone and DeepSeek down: the 标题党型 left at 1 HP and shot, the
        // battle's last blow; the win and its results follow (Drive presses through them).
        IEnumerator FemaleKill()
        {
            yield return InMode("Command", "女主角的指令菜单");
            Enemies[1].Unit.Hp = 1;
            yield return Attack(1, S("battle-kill"), 2f / Fps, 40);
        }

        void HideMenu(bool hide)
        {
            var menu = ui.Q(className: "sp-battle__menu");
            if (menu != null) menu.style.visibility = hide ? new StyleEnum<Visibility>(Visibility.Hidden) : new StyleEnum<Visibility>(StyleKeyword.Null);
        }

        // On DeepSeek's first turn, the menu hidden: a lone lunge, 冷知识型's 你知道吗？ at her, a hit on a
        // weakness and a critical one, each on cue; then she guards.
        IEnumerator StagedRound()
        {
            yield return InMode("Command", "DeepSeek 的指令菜单");
            const int coldfact = 0, clickbait = 1;
            var st = Stage;
            HideMenu(true);
            try
            {
                st.Move(st.Threat(clickbait, 1), 0.35f);
                yield return Wait(0.5f);
                // Wound up under its skill's name (as EnemyTurn has it), then the lunge.
                var lunge = StartFilm(S("battle-lunge"), null, 0.05f, 24, ScreenView("敌人出招的镜头（Threat）"), FieldSide(clickbait, 1, "场地侧面看扑击全程")());
                st.Brace(clickbait, 1);
                yield return Wait(0.25f);
                yield return Await(st.Lunge(clickbait, 1), 5f, "扑击");
                yield return WaitFilm(lunge);

                st.Move(st.Threat(coldfact, 1), 0.35f);
                var lecture = StartFilm(S("battle-lecture"), null, 0.1f, 24, ScreenView());
                st.Brace(coldfact, 1);
                yield return Await(EnemyBar(coldfact, "你知道吗？"), 5f, "冷知识型的技能条");
                yield return Await(st.Lecture(coldfact, 1), 8f, "你知道吗？");
                yield return Await(ApplyHit(Member("DeepSeek"), new Hit { Element = BElement.Physical, Damage = 6 }), 5f, "DeepSeek 受击");
                yield return WaitFilm(lecture);

                st.Move(st.Watch(clickbait), 0.4f);
                yield return Wait(0.5f);
                var weak = StartFilm(S("battle-weak"), null, 0.1f, 16, ScreenView(), Bare()());
                yield return Wait(0.2f);
                yield return Await(ApplyHit(Enemies[clickbait].Unit, new Hit { Element = BElement.Ice, Damage = 28, Affinity = BAffinity.Weak, KnockedDown = true }), 5f, "弱点命中");
                yield return WaitFilm(weak);
                var getUp = StartFilm(S("battle-getup"), null, 0.05f, 16, ScreenView(), Bare()());
                yield return GetUp(clickbait);
                yield return WaitFilm(getUp);

                st.Move(st.Watch(coldfact), 0.4f);
                yield return Wait(0.5f);
                var crit = StartFilm(S("battle-crit"), null, 0.1f, 16, ScreenView(), Bare()());
                yield return Wait(0.2f);
                yield return Await(ApplyHit(Enemies[coldfact].Unit, new Hit { Element = BElement.Physical, Damage = 34, Critical = true, KnockedDown = true }), 5f, "暴击");
                yield return WaitFilm(crit);
                yield return GetUp(coldfact);

                st.Move(st.Behind(Actor), 0.3f);
                yield return Wait(0.4f);
            }
            finally
            {
                HideMenu(false);
            }
            yield return QuickGuard();
        }

        // A noise body's battle face (NoiseLook.Face) close up from in front, half a second in each mood, the mood
        // set 0.35 s before its frame; all of them looking straight ahead.
        IEnumerator Faces(Scenario sc, int foe)
        {
            if (!Wanted(sc)) yield break;
            yield return InMode("Command", "指令菜单");
            var st = Stage;
            var list = (System.Collections.IList)Field(typeof(BattleStage), "foes").GetValue(st);
            var look = (NoiseLook)Field(list[foe].GetType(), "Look").GetValue(list[foe]);
            st.Facing(-1);
            var moods = new[] { NoiseMood.Calm, NoiseMood.Wary, NoiseMood.Angry, NoiseMood.Hurt, NoiseMood.Cower, NoiseMood.Dizzy, NoiseMood.Asleep };
            var view = CamView("face", "正面特写（不带界面）", st.Camera, () =>
            {
                var e = st.FoePoint(foe);
                var way = (st.MemberPoint(0) + st.MemberPoint(1)) * 0.5f - e;
                way.y = 0f;
                return new CamPose { At = e + way.normalized * 2.4f + Vector3.up * 0.15f, Look = e + Vector3.up * 0.05f, Fov = 40f };
            });
            look.SetMood(moods[0], 0.6f);
            yield return Wait(0.35f);
            var f = StartFilm(sc, null, 0.5f, moods.Length, view);
            for (var k = 1; k < moods.Length; k++)
            {
                yield return Wait(0.15f);
                look.SetMood(moods[k], 0.6f);
                yield return Wait(0.35f);
            }
            yield return WaitFilm(f);
        }

        IEnumerator GetUp(int foe)
        {
            var unit = Enemies[foe].Unit;
            unit.Down = false;
            Enemies[foe].SetDown(false);
            Stage.Down(foe, false);
            yield return Wait(0.6f);
        }

        // On her second turn: the player, guarding, is knocked down by a critical 震惊！ and gets up as her own turn
        // would have it (PartyTurn: the guard ended, then Rise); then DeepSeek, guarding, is knocked down too, and a
        // second 震惊！ takes her last HP where she lies.
        IEnumerator StagedFaint()
        {
            yield return InMode("Command", "DeepSeek 的指令菜单");
            const int clickbait = 1;
            var st = Stage;
            HideMenu(true);
            try
            {
                var player = Member(GameState.PlayerId);
                player.Guarding = true;
                yield return Await(st.Guard(0), 5f, "女主角防御");
                st.Move(st.Threat(clickbait, 0), 0.35f);
                var knock = StartFilm(S("battle-knock"), null, 0.1f, 48, ScreenView(), SideView(0, -1f, "女主角左侧的检查镜头")());
                yield return Await(st.Bait(clickbait, 0), 8f, "震惊！打女主角");
                yield return Await(ApplyHit(player, new Hit { Element = BElement.Physical, Damage = 4, Critical = true, KnockedDown = true }), 5f, "女主角被击倒");
                yield return Wait(1.6f); // the fall takes 0.75 s from the blow, which lands a little after ApplyHit returns
                yield return Shoot(S("battle-knock"), "battle-knock-lying", "被击倒后躺着的姿势：从脚边上方俯看（整窗）", st.Camera, () => LyingView(0, -1f));
                yield return Shoot(S("battle-knock"), "battle-knock-lying-side", "被击倒后躺着的姿势：左侧平视（整窗）", st.Camera, () => BattleSide(0, -1f));
                player.Guarding = false;
                st.Unguard(0);
                player.Down = false;
                st.Rise(0);
                yield return WaitFilm(knock);

                var ds = Member("DeepSeek");
                ds.Guarding = true;
                yield return Await(st.Guard(1), 5f, "DeepSeek 防御");
                st.Move(st.Threat(clickbait, 1), 0.35f);
                var faint = StartFilm(S("battle-faint"), null, 0.1f, 48, ScreenView(), SideView(1, 1f, "DeepSeek 右侧的检查镜头")());
                st.Brace(clickbait, 1);
                yield return Await(EnemyBar(clickbait, "震惊！"), 5f, "标题党型的技能条");
                yield return Await(st.Bait(clickbait, 1), 8f, "震惊！");
                yield return Await(ApplyHit(ds, new Hit { Element = BElement.Physical, Damage = 4, Critical = true, KnockedDown = true }), 5f, "DeepSeek 被击倒");
                yield return Wait(0.8f);
                yield return Await(st.Bait(clickbait, 1), 8f, "再一次震惊！");
                ds.Hp = 1;
                yield return Await(ApplyHit(ds, new Hit { Element = BElement.Physical, Damage = 30 }), 5f, "DeepSeek 倒下");
                yield return WaitFilm(faint);
                yield return Shoot(S("battle-faint"), "battle-faint-lying", "倒下后躺着的姿势：从脚边上方俯看（整窗）", st.Camera, () => LyingView(1, 1f));
                yield return Shoot(S("battle-faint"), "battle-faint-lying-side", "倒下后躺着的姿势：右侧平视（整窗）", st.Camera, () => BattleSide(1, 1f));
                // Her face from straight ahead of it, and the face the stage has her show (eyes shut: cell 0).
                yield return Shoot(S("battle-faint"), "battle-faint-face", "倒下后的脸：正对着脸（整窗）", st.Camera, () =>
                {
                    var (at, forward) = st.MemberFaceAt(1);
                    return new CamPose { At = at + forward * 0.7f, Look = at, Fov = 30f };
                });
                S("battle-faint").Notes.Add($"倒下后的表情格（眼、嘴、腮红，-1 为原样）：{st.MemberFace(1)}");
            }
            finally
            {
                HideMenu(false);
            }
        }

        // ------------------------------------------------------------------ the maze

        IEnumerator Dive(string id, string what)
        {
            var job = Run(game.RunDungeon(id));
            yield return Until(() => game.Dungeon.Exploring && !game.Transitions.Busy, 90f, what);
            if (job.Done && job.Error != null) throw new LabFailure(what + "：" + job.Error.Message);
            Hush();
            yield return Wait(0.5f);
        }

        IEnumerator Surface(string what)
        {
            Release();
            if (game.InDungeon) game.Dungeon.Abort();
            yield return Until(() => !game.InDungeon && !game.Transitions.Busy, 30f, what);
            yield return Frames(5);
        }

        // Noise bodies blind to the player, the zones' banners and story already seen, the maintenance meter low.
        void Hush()
        {
            var w = World;
            w.SafeUntil = 1e9f;
            var visited = (HashSet<ZoneDef>)fVisited.GetValue(game.Dungeon);
            foreach (var z in w.Def.Zones) visited.Add(z);
            if (w.Def.Meter) fMeter.SetValue(game.Dungeon, 20f);
        }

        // Puts the player at a point (not only a cell's middle), facing `yaw`, the camera behind and DeepSeek beside.
        void Teleport(Vector3 at, float yaw)
        {
            var w = World;
            w.Place(w.Map.CellAt(at), yaw);
            pPlayerPosition.GetSetMethod(true).Invoke(w, new object[] { at });
            w.Avatar.Root.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            w.Companion?.Place(at, yaw, w.Map);
            mUpdateCamera.Invoke(w, new object[] { 0f, true });
        }

        void ResetNoises()
        {
            foreach (var e in World.Enemies)
            {
                e.Remove();
                e.Revive();
            }
        }

        IEnumerator Block(string what, Func<IEnumerator> body, params string[] ids)
        {
            if (!ids.Any(id => S(id).Selected)) yield break;
            yield return Guarded(what, Ready(body), RecoverMaze);
        }

        IEnumerator Ready(Func<IEnumerator> body)
        {
            Release();
            if (World == null) throw new LabFailure("迷宫已经不在了");
            SetMode("Explore");
            Hush();
            yield return body();
            Release();
            Hush();
        }

        IEnumerator RecoverMaze()
        {
            Release();
            if (game.InBattle)
            {
                game.Battle.Abort();
                yield return Until(() => !game.InBattle && !game.Transitions.Busy, 30f, "恢复：退出战斗");
            }
            if (game.InDungeon && World != null)
            {
                SetMode("Explore");
                Hush();
                ResetNoises();
            }
            yield return Frames(5);
        }

        Func<CamPose> Fixed(Vector3 at, Vector3 look, float fov = 50f) => () => new CamPose { At = at, Look = look, Fov = fov };

        // Beside the player, `side` of them, at hip height, moving with them.
        Func<CamPose> Track(Vector3 side, float metres = 3.5f) => () =>
        {
            var p = World.PlayerPosition;
            return new CamPose { At = p + side * metres + Vector3.up * 0.95f, Look = p + Vector3.up * 0.9f, Fov = 50f };
        };

        View SideTrack(Vector3 side) => CamView("side", "南侧 3.5 米、髋高的跟拍镜头", World.Camera, Track(side));

        // Close on a figure as it moves: `from` of it, 2.6 m off at hip height, the whole figure filling the frame's height.
        View Close(string tag, string what, Func<Vector3> who, Vector3 from) => CamView(tag, what, World.Camera, () =>
        {
            var p = who();
            return new CamPose { At = p + from.normalized * 2.6f + Vector3.up * 0.9f, Look = p + Vector3.up * 0.85f, Fov = 40f };
        });

        // The figures themselves from close by, as stills the size of the window: the player and the companion standing,
        // from five sides and close on the face, the back of the head, the bust, each hand, the legs and the feet; then
        // both at a run, from beside, behind and ahead. For looking at the models, not at what they do.
        IEnumerator ModelSheet(Ground g)
        {
            var id = g.P + "-model";
            var sc = S(id);
            if (!sc.Selected) yield break;
            Teleport(World.Map.World(g.Lane), 90f);
            yield return Wait(2.5f);
            var up = Vector3.up;
            foreach (var (who, label, root) in new[] { ("player", "主角", World.Avatar.Root), ("her", "同伴", World.Companion?.Root) })
            {
                if (root == null) continue;
                var a = root.GetComponentInChildren<Animator>();
                Transform head = a.GetBoneTransform(HumanBodyBones.Head), leftHand = a.GetBoneTransform(HumanBodyBones.LeftHand),
                    rightHand = a.GetBoneTransform(HumanBodyBones.RightHand), leftFoot = a.GetBoneTransform(HumanBodyBones.LeftFoot),
                    rightFoot = a.GetBoneTransform(HumanBodyBones.RightFoot);
                CamPose Body(Vector3 from) => new CamPose { At = root.position + from.normalized * 2.4f + up * 0.95f, Look = root.position + up * 0.85f, Fov = 42f };
                // The figure's side the other one is not standing on.
                Vector3 Clear(Transform figure)
                {
                    var other = figure == World.Avatar.Root ? (World.Companion != null ? World.Companion.Root : null) : World.Avatar.Root;
                    return other != null && Vector3.Dot(other.position - figure.position, figure.right) > 0f ? -figure.right : figure.right;
                }
                CamPose Near(Vector3 at, Vector3 from, float metres, float fov = 32f) => new CamPose { At = at + from.normalized * metres, Look = at, Fov = fov };
                foreach (var (name, what, pose) in new (string, string, Func<CamPose>)[]
                {
                    ("front", "正面全身", () => Body(root.forward)),
                    ("front34", "右前方全身", () => Body(root.forward + root.right)),
                    ("side", "侧面全身（同伴不在的一侧）", () => Body(Clear(root))),
                    ("back34", "左后方全身", () => Body(-root.forward - root.right)),
                    ("back", "背面全身", () => Body(-root.forward)),
                    ("face", "脸（正面）", () => Near(head.position + up * 0.06f, root.forward + up * 0.05f, 0.7f)),
                    ("face34", "脸（右前方）", () => Near(head.position + up * 0.06f, root.forward + root.right * 0.9f, 0.7f)),
                    ("head-back", "后脑和头发", () => Near(head.position + up * 0.05f, -root.forward + up * 0.25f, 0.9f)),
                    ("bust", "上半身", () => Near(head.position - up * 0.25f, root.forward + root.right * 0.3f, 1.2f, 36f)),
                    ("hand-left", "左手", () => Near(leftHand.position, root.forward - root.right * 0.6f + up * 0.2f, 0.45f)),
                    ("hand-right", "右手", () => Near(rightHand.position, root.forward + root.right * 0.6f + up * 0.2f, 0.45f)),
                    ("legs", "腰以下", () => Near(root.position + up * 0.45f, root.forward + root.right * 0.4f, 1.5f, 38f)),
                    ("feet", "脚", () => Near((leftFoot.position + rightFoot.position) * 0.5f, root.forward + root.right * 0.5f + up * 0.5f, 0.8f)),
                })
                    yield return Shoot(sc, $"{id}-{who}-{name}", $"{label}：{what}", World.Camera, pose);
            }
            Stick(StickFor(Vector3.right, 1f), false);
            yield return Wait(0.9f);
            Func<Vector3> player = () => World.PlayerPosition;
            Func<Vector3> her = () => World.Companion != null ? World.Companion.Root.position : World.PlayerPosition;
            foreach (var (name, what, who, from) in new (string, string, Func<Vector3>, Vector3)[]
            {
                ("run-player-side", "主角跑动：侧面", player, Vector3.forward), ("run-player-back", "主角跑动：身后", player, Vector3.left),
                ("run-player-front", "主角跑动：斜前方", player, Vector3.right + Vector3.back * 0.6f),
                ("run-her-side", "同伴跑动：侧面", her, Vector3.back), ("run-her-back", "同伴跑动：身后", her, Vector3.left),
                ("run-her-front", "同伴跑动：斜前方", her, Vector3.right + Vector3.back * 0.6f),
            })
                yield return Shoot(sc, $"{id}-{name}", what, World.Camera, () =>
                {
                    var p = who();
                    return new CamPose { At = p + from.normalized * 2.3f + up * 0.95f, Look = p + up * 0.85f, Fov = 42f };
                });
            Release();
            yield return Wait(0.6f);
        }

        // The gaits at a steady pace, close up: a slow walk, the run and the sprint heading east, each filmed over a
        // second from beside, behind and ahead of the player, and from beside and behind the companion. With `idle`,
        // standing first.
        IEnumerator Gait(Ground g, bool idle)
        {
            var id = g.P + "-gait";
            var sc = S(id);
            Vector3 east = Vector3.right, south = Vector3.back;
            var lane = World.Map.World(g.Lane);
            Func<Vector3> player = () => World.PlayerPosition;
            Func<Vector3> her = () => World.Companion != null ? World.Companion.Root.position : World.PlayerPosition;
            View[] Views() => new[]
            {
                // The player from the north: the companion runs on their south, and from there she fills the picture.
                Close("side", "主角侧面", player, -south), Close("back", "主角身后", player, -east), Close("front", "主角斜前方", player, east - south * 0.6f),
                Close("her-side", "同伴侧面", her, south), Close("her-back", "同伴身后", her, -east),
            };
            if (idle)
            {
                Teleport(lane, 90f);
                yield return Wait(1.5f);
                yield return WaitFilm(StartFilm(sc, id + "-idle", 0.2f, 8, Views()));
            }
            foreach (var (name, push, sprint, lead) in new[] { ("walk", 0.35f, false, 1.0f), ("run", 1f, false, 0.7f), ("sprint", 1f, true, 0.7f) })
            {
                Teleport(lane, 90f);
                yield return Wait(1.2f);
                Stick(StickFor(east, push), sprint);
                yield return Wait(lead);
                var f = StartFilm(sc, id + "-" + name, 1f / 15f, 24, Views());
                yield return WaitFilm(f);
                Release();
                yield return Wait(0.5f);
            }
        }

        View FixedView(string tag, string what, Vector3 at, Vector3 look, float fov = 50f) => CamView(tag, what, World.Camera, Fixed(at, look, fov));

        IEnumerator Zones(string p, (string Name, string What, int X, int Y, float Yaw)[] list)
        {
            var sc = S(p + "-zones");
            SetMode("Busy");
            foreach (var z in list)
            {
                World.Place(new Vector2Int(z.X, z.Y), z.Yaw);
                yield return Wait(1.0f);
                yield return Shoot(sc, $"{p}-zone-{z.Name}", z.What + "；" + Framing());
            }
            SetMode("Explore");
        }

        // How the follow camera frames the player now: its boom, how far the top edge is over the horizon, and where the
        // bottom edge crosses them (on their own vertical, centimetres up).
        string Framing()
        {
            var cam = World.Camera;
            var top = Mathf.Asin(cam.transform.forward.y) * Mathf.Rad2Deg + cam.fieldOfView * 0.5f;
            var cut = 0;
            while (cut < 180 && cam.WorldToViewportPoint(World.PlayerPosition + Vector3.up * (cut * 0.01f)).y < 0f) cut += 5;
            return $"镜头 {(float)fCameraDistance.GetValue(World):0.0} 米，画面顶边在地平线上 {top:0}°，" + (cut == 0 ? "主角全身在画面里" : $"画面底边切在主角 {cut} 厘米高处");
        }

        IEnumerator Wides(string p, (string Name, string What, Vector3 At, Vector3 Look)[] list)
        {
            var sc = S(p + "-wide");
            SetMode("Busy");
            var cam = World.Camera;
            var fov = cam.fieldOfView;
            try
            {
                foreach (var v in list)
                {
                    // The camera stays there a few frames, so the sky dome (which follows it) is round it too. The follow
                    // camera takes it back every frame, so it is put there again for the shot.
                    cam.transform.SetPositionAndRotation(v.At, Quaternion.LookRotation(v.Look - v.At));
                    cam.fieldOfView = 55f;
                    yield return Frames(3);
                    cam.transform.SetPositionAndRotation(v.At, Quaternion.LookRotation(v.Look - v.At));
                    cam.fieldOfView = 55f;
                    yield return Shoot(sc, $"{p}-wide-{v.Name}", v.What, cam);
                }
            }
            finally
            {
                cam.fieldOfView = fov;
            }
            Teleport(World.PlayerPosition, World.PlayerYaw);
            SetMode("Explore");
        }

        // ------------------------------------------------------------------ the water's air (Dungeon/SeaAtmos.cs)

        // The frame time with the 60-a-second cap lifted, after a still of the view: the mean, median and 95th percentile
        // of 240 frames, to compare one run of the lab with another on this machine (under the cap a frame takes 16.7 ms
        // however little of it the GPU was busy). `place`, if any, puts the camera back every frame (the follow camera
        // takes it). Nothing may be filming meanwhile.
        IEnumerator Perf(Scenario sc, string name, string what, Camera cam, Action place = null)
        {
            if (!sc.Selected) yield break;
            place?.Invoke();
            yield return Frames(3);
            place?.Invoke();
            yield return Shoot(sc, name, what, place != null ? cam : null);
            var times = new float[240];
            Application.targetFrameRate = -1;
            try
            {
                for (var i = 0; i < 30; i++)
                {
                    place?.Invoke();
                    yield return null;
                }
                var t = Time.realtimeSinceStartup;
                for (var i = 0; i < times.Length; i++)
                {
                    place?.Invoke();
                    yield return null;
                    var now = Time.realtimeSinceStartup;
                    times[i] = (now - t) * 1000f;
                    t = now;
                }
            }
            finally
            {
                Application.targetFrameRate = Pace;
            }
            var sorted = times.OrderBy(x => x).ToArray();
            sc.Notes.Add($"{name}：不限帧时 {times.Length} 帧平均 {times.Average():0.00} 毫秒，中位 {sorted[sorted.Length / 2]:0.00}，95% {sorted[sorted.Length * 95 / 100]:0.00}，最慢 {sorted[sorted.Length - 1]:0.00}");
        }

        // The first command of the main battle, the camera over the player's shoulder.
        IEnumerator BattlePerf()
        {
            if (!S("battle-perf").Selected) yield break;
            yield return InMode("Command", "指令菜单");
            yield return Wait(0.6f);
            yield return Perf(S("battle-perf"), "battle-perf", "第一个指令镜头（游戏画面）", null);
            // Again with the atmosphere's switch off (no shadows or rim), for what they cost.
            var airOff = AirSwitch;
            if (airOff == null) yield break;
            airOff.SetValue(null, true);
            try
            {
                yield return Perf(S("battle-perf"), "battle-perf-off", "同一镜头，影子和轮廓光关掉（SeaAtmos.off）", null);
            }
            finally
            {
                airOff.SetValue(null, false);
            }
        }

        // SeaAtmos.off, found by name so the lab runs on a tree from before it too: the zone's air None everywhere, no
        // shadows, rim or local lights.
        static FieldInfo AirSwitch => typeof(DungeonWorld).Assembly.GetType("SystemPrompt.Dungeon.SeaAtmos")?.GetField("off", BindingFlags.Static | BindingFlags.NonPublic);

        // A layer's air: the frame time at the start and from high up, each also with the atmosphere switched off (AirSwitch);
        // a still with it off and on; the figures' rim and shadows; a run over a zone border; the same battle met in two zones.
        IEnumerator Air(string p, Vector2Int start, float startYaw, Vector3 highAt, Vector3 highLook, Vector2Int still, float stillYaw,
            Vector2Int border, Vector3 borderWay, (string Name, string What, int X, int Y, float Yaw)[] battles)
        {
            var w = World;
            SetMode("Busy");
            if (S(p + "-perf").Selected)
            {
                var cam = w.Camera;
                var fov = cam.fieldOfView;
                var airOff = AirSwitch;
                for (var pass = 0; pass < (airOff != null ? 2 : 1); pass++)
                {
                    var tag = pass == 0 ? "" : "-off";
                    var off = pass == 0 ? "" : "，区域空气、影子、轮廓光和局部光关掉";
                    airOff?.SetValue(null, pass == 1);
                    try
                    {
                        Teleport(w.Map.World(start), startYaw);
                        yield return Wait(1.5f);
                        yield return Perf(S(p + "-perf"), p + "-perf-start" + tag, "起点朝北（游戏画面" + off + "）", null);
                        yield return Perf(S(p + "-perf"), p + "-perf-high" + tag, "高处俯瞰（只有 3D 画面" + off + "）", cam, () =>
                        {
                            cam.transform.SetPositionAndRotation(highAt, Quaternion.LookRotation(highLook - highAt));
                            cam.fieldOfView = 55f;
                        });
                    }
                    finally
                    {
                        cam.fieldOfView = fov;
                        airOff?.SetValue(null, false);
                    }
                }
                Teleport(w.Map.World(start), startYaw);
            }
            if (S(p + "-airless").Selected)
            {
                var sc = S(p + "-airless");
                var off = AirSwitch;
                if (off == null) sc.Notes.Add("这一版还没有 SeaAtmos：两张都是原样");
                off?.SetValue(null, true);
                try
                {
                    Teleport(w.Map.World(still), stillYaw);
                    yield return Wait(1.5f);
                    yield return Shoot(sc, p + "-airless-off", "区域空气关掉（游戏镜头，只有 3D 画面）", w.Camera);
                }
                finally
                {
                    off?.SetValue(null, false);
                }
                Teleport(w.Map.World(still), stillYaw);
                yield return Wait(1.5f);
                yield return Shoot(sc, p + "-airless-on", "区域空气打开（同一处）", w.Camera);
            }
            if (S(p + "-shade").Selected)
            {
                var sc = S(p + "-shade");
                Teleport(w.Map.World(still), stillYaw);
                yield return Wait(1.5f);
                var body = w.Avatar.Root;
                Vector3 at = body.position, ahead = body.forward, right = body.right;
                yield return Shoot(sc, p + "-shade-back", "背后近景：后脑和肩膀的轮廓光", w.Camera, () => new CamPose
                    { At = at - ahead * 2.4f + right * 0.7f + Vector3.up * 1.8f, Look = at + ahead * 0.6f + Vector3.up * 1.1f, Fov = 40f });
                yield return Shoot(sc, p + "-shade-side", "侧面低机位：脚下的影子", w.Camera, () => new CamPose
                    { At = at + right * 3.4f - ahead * 0.6f + Vector3.up * 1.3f, Look = at + ahead * 0.8f + Vector3.up * 0.5f, Fov = 45f });
            }
            if (S(p + "-border").Selected)
            {
                foreach (var e in w.Enemies) e.Remove();
                Teleport(w.Map.World(border), YawOf(borderWay));
                yield return Wait(2.5f);
                SetMode("Explore");
                // Run on until the avatar is in the next zone and its air has had 2.6 s to ease in (14 s at most): a
                // time set for one running speed stopped short of the border at another.
                var f = StartFilm(S(p + "-border"), null, 0.4f, 24, ScreenView());
                yield return Wait(0.4f);
                var from = w.Zone;
                float end = Time.unscaledTime + 14f, crossed = -1f;
                while (Time.unscaledTime < end && (crossed < 0f || Time.unscaledTime - crossed < 2.6f))
                {
                    if (crossed < 0f && w.Zone != from) crossed = Time.unscaledTime;
                    Stick(StickFor(borderWay, 1f), false);
                    yield return null;
                }
                Release();
                yield return WaitFilm(f);
                SetMode("Busy");
                ResetNoises();
            }
            if (S(p + "-battle-zones").Selected)
            {
                var sc = S(p + "-battle-zones");
                foreach (var z in battles)
                {
                    Teleport(Cell(z.X, z.Y), z.Yaw);
                    yield return Wait(1.5f);
                    var battle = Run(game.RunBattle(Lab("lab_zone", Foe("clickbait", 999), Foe("repost", 999))));
                    yield return Until(() => game.InBattle && Stage != null, 20f, "战斗开始");
                    yield return InMode("Command", "指令菜单");
                    yield return Wait(0.6f);
                    yield return Shoot(sc, $"{p}-battle-{z.Name}", z.What);
                    game.Battle.Abort();
                    yield return Until(() => battle.Done && !game.InBattle && !game.Transitions.Busy, 30f, "中止战斗、回到迷宫");
                    yield return Frames(5);
                    Hush();
                    SetMode("Busy");
                }
            }
            SetMode("Explore");
        }

        IEnumerator Motion(Ground g)
        {
            var p = g.P;
            var east = Vector3.right;
            var south = Vector3.back;
            var lane = World.Map.World(g.Lane);

            if (S(p + "-idle").Selected)
            {
                Teleport(lane, 90f);
                yield return Wait(1.2f);
                yield return WaitFilm(StartFilm(S(p + "-idle"), null, 0.2f, 16, ScreenView(), SideTrack(south)));
            }
            if (S(p + "-walk").Selected)
            {
                Teleport(lane, 90f);
                yield return Wait(1.2f);
                var f = StartFilm(S(p + "-walk"), null, 0.167f, 16, ScreenView(), SideTrack(south));
                yield return Wait(0.17f);
                Stick(StickFor(east, 0.35f), false);
                yield return Wait(1.9f);
                Release();
                yield return WaitFilm(f);
            }
            foreach (var (id, sprint) in new[] { (p + "-run", false), (p + "-sprint", true) })
            {
                if (!S(id).Selected) continue;
                Teleport(lane, 90f);
                yield return Wait(1.2f);
                var f = StartFilm(S(id), null, 0.133f, 16, ScreenView(), SideTrack(south));
                yield return Wait(0.13f);
                Stick(StickFor(east, 1f), sprint);
                yield return Wait(1.2f);
                Release();
                yield return WaitFilm(f);
            }
            if (S(p + "-gait").Selected) yield return Gait(g, false);
            yield return ModelSheet(g);
            if (S(p + "-halt").Selected)
            {
                // A node firing mid-run: the screen takes the controls (Busy) with the stick still held.
                Teleport(lane, 90f);
                yield return Wait(1.2f);
                Stick(StickFor(east, 1f), false);
                yield return Wait(0.6f);
                var f = StartFilm(S(p + "-halt"), null, 1f / 30f, 16, ScreenView(), SideTrack(south));
                yield return Wait(0.1f);
                SetMode("Busy");
                yield return WaitFilm(f);
                Release();
                SetMode("Explore");
            }
            if (S(p + "-turn").Selected)
            {
                Teleport(World.Map.World(g.TurnFrom), 90f);
                yield return Wait(1.2f);
                var f = StartFilm(S(p + "-turn"), null, 0.133f, 16, ScreenView(), SideTrack(south));
                yield return Wait(0.13f);
                Stick(StickFor(east, 1f), false);
                yield return Wait(0.7f);
                Stick(StickFor(-east, 1f), false);
                yield return Wait(1.0f);
                Release();
                yield return WaitFilm(f);
            }
            if (S(p + "-circle").Selected)
            {
                var c = g.CircleCentre;
                Teleport(c + east * g.Radius, 0f);
                yield return Wait(1.2f);
                var outside = c + g.CircleCam + Vector3.up * 1.1f;
                var f = StartFilm(S(p + "-circle"), null, 0.2f, 16, ScreenView(),
                    CamView("side", "圈外的固定位置，转着看人", World.Camera, () => new CamPose { At = outside, Look = World.PlayerPosition + Vector3.up * 0.9f, Fov = 50f }));
                var end = Time.unscaledTime + 3.0f;
                while (Time.unscaledTime < end)
                {
                    var from = World.PlayerPosition - c;
                    from.y = 0f;
                    var d = from.magnitude;
                    var radial = d > 0.01f ? from / d : Vector3.right;
                    var tangent = new Vector3(-radial.z, 0f, radial.x); // counter-clockwise seen from above
                    var dir = tangent + radial * Mathf.Clamp((g.Radius - d) * 0.8f, -1f, 1f);
                    Stick(StickFor(dir, 1f), false);
                    yield return null;
                }
                Release();
                yield return WaitFilm(f);
            }
            if (S(p + "-follow").Selected)
            {
                Teleport(lane, 90f);
                yield return Wait(1.5f);
                var w = World;
                var f = StartFilm(S(p + "-follow"), null, 0.167f, 16, ScreenView(),
                    CamView("side", "两人中点南侧 6 米的跟拍镜头", w.Camera, () =>
                    {
                        var mid = (w.PlayerPosition + w.Companion.Root.position) * 0.5f;
                        mid.y = 0f;
                        return new CamPose { At = mid + south * 6f + Vector3.up * 1.1f, Look = mid + Vector3.up * 0.9f, Fov = 50f };
                    }));
                yield return Wait(0.17f);
                Stick(StickFor(east, 1f), false);
                yield return Wait(1.6f);
                Release();
                yield return WaitFilm(f);
            }
            if (S(p + "-look").Selected)
            {
                Teleport(lane, 90f);
                var w = World;
                var f = StartFilm(S(p + "-look"), null, 0.5f, 16, ScreenView(),
                    CamView("front", "两人前方 3.5 米、对着两张脸的镜头", w.Camera, () =>
                    {
                        var mid = (w.PlayerPosition + w.Companion.Root.position) * 0.5f;
                        mid.y = 0f;
                        return new CamPose { At = mid + east * 3.5f + Vector3.up * 1.5f, Look = mid + Vector3.up * 1.35f, Fov = 40f };
                    }));
                yield return WaitFilm(f);
            }
            if (S(p + "-tail").Selected)
            {
                var sc = S(p + "-tail");
                Teleport(lane, 90f);
                yield return Wait(1.0f);
                var her = World.Companion.Root;
                // A view each: a film's views keep their frames until they are written out.
                View Behind() => CamView("behind", "她右后方 3 米、对着尾巴的镜头", World.Camera, () => new CamPose
                {
                    At = her.position - her.forward * 2.4f + her.right * 1.8f + Vector3.up * 1.3f,
                    Look = her.position - her.forward * 0.5f + Vector3.up * 0.5f, Fov = 50f,
                });
                View sway = Behind(), wag = Behind();
                yield return WaitFilm(StartFilm(sc, null, 0.25f, 16, sway));
                World.Companion.Wag();
                yield return WaitFilm(StartFilm(sc, p + "-tail-wag", 1f / 15f, 16, wag));
            }
            if (S(p + "-steps").Selected)
            {
                Teleport(lane, 90f);
                yield return Wait(1.2f);
                Stick(StickFor(east, 1f), true);
                yield return Wait(0.8f);
                // Low beside the run: the seabed round the feet fills the lower half.
                var f = StartFilm(S(p + "-steps"), null, 1f / 15f, 16, ScreenView(),
                    CamView("low", "南侧 5 米、膝高的跟拍镜头", World.Camera, () =>
                    {
                        var at = World.PlayerPosition;
                        return new CamPose { At = at + south * 5f + Vector3.up * 0.5f, Look = at + Vector3.up * 0.35f, Fov = 50f };
                    }));
                yield return Wait(0.35f);
                Release();
                yield return Wait(0.45f);
                Stick(StickFor(east, 1f), false);
                yield return WaitFilm(f);
                Release();
            }
        }

        // A camera in front of a noise body (turned `angle` off its facing), pulled in where a wall is in the way.
        Func<CamPose> NoiseCam(SeaNoise e, float metres = 4.6f, float height = 1.7f, float[] angles = null)
        {
            angles ??= new[] { 25f, -25f, 50f, -50f, 0f, 80f, -80f };
            var angle = angles[0];
            foreach (var a in angles)
                if (CamSpot(e, a, metres, height, out _))
                {
                    angle = a;
                    break;
                }
            return () =>
            {
                CamSpot(e, angle, metres, height, out var at);
                return new CamPose { At = at, Look = e.Position + Vector3.up * 1.0f, Fov = 50f };
            };
        }

        bool CamSpot(SeaNoise e, float angle, float metres, float height, out Vector3 at)
        {
            var map = World.Map;
            var dir = Quaternion.Euler(0f, e.Yaw + angle, 0f) * Vector3.forward;
            var target = e.Position + Vector3.up;
            for (var d = metres; d >= 1.6f; d -= 0.2f)
            {
                at = e.Position + dir * d + Vector3.up * height;
                if (map.Free(at, 0.3f) && map.Clear(at, target)) return d > metres - 0.01f;
            }
            at = e.Position + dir * 1.6f + Vector3.up * height;
            return false;
        }

        static string KindName(string kind) => kind == "repost" ? "转发型" : kind == "coldfact" ? "冷知识型" : "标题党型";

        IEnumerator NoiseRest(string p, Vector2Int rest)
        {
            ResetNoises();
            Teleport(World.Map.World(rest), 0f);
            World.SafeUntil = 1e9f;
            yield return Wait(0.3f);
            var views = World.Enemies.Where(e => e.Active).Select(e =>
                CamView("noise" + e.Spawn.Mark, $"{e.Spawn.Mark} 号{KindName(e.Spawn.Kind)}{(e.Spawn.Dozing ? "打瞌睡" : "巡逻")}（它前方的检查镜头）", World.Camera, NoiseCam(e))).ToArray();
            yield return WaitFilm(StartFilm(S(p + "-noise-rest"), null, 0.2f, 16, views));
        }

        // Stands `metres` in front of a noise body, lets it see them, and runs off along `run` once it gives chase.
        IEnumerator Notice(string id, char mark, float metres, Vector3 run)
        {
            var sc = S(id);
            if (!sc.Selected) yield break;
            ResetNoises();
            var e = Noise(mark);
            World.SafeUntil = 1e9f;
            Teleport(e.Position + e.Forward * metres, YawOf(-e.Forward));
            yield return Frames(2);
            // From well off its facing: the player and DeepSeek stand straight in front of it.
            var f = StartFilm(sc, null, 0.133f, 16, ScreenView(),
                CamView("noise", "噪声体斜前方的检查镜头", World.Camera, NoiseCam(e, 5f, 1.7f, new[] { 70f, -70f, 55f, -55f, 90f, -90f })));
            World.SafeUntil = 0f;
            yield return Until(() => e.State == NoiseState.Chase, 3f, $"{mark} 号察觉玩家");
            Stick(StickFor(run, 1f), true);
            yield return Wait(1.4f);
            Release();
            World.SafeUntil = 1e9f;
            yield return WaitFilm(f);
            ResetNoises();
            Hush();
        }

        // Steps up behind a noise body and jumps it: the preemptive stamp and the battle's wipe, then the battle is
        // called off and the maze is back.
        IEnumerator Strike(string id, char mark)
        {
            var sc = S(id);
            if (!sc.Selected) yield break;
            ResetNoises();
            var e = Noise(mark);
            World.SafeUntil = 1e9f;
            Teleport(e.Position - e.Forward * 1.6f, e.Yaw);
            yield return null;
            var f = StartFilm(sc, null, 1f / 15f, 16, ScreenView(), StrikeSide(e));
            yield return Press(Key.Enter);
            yield return WaitFilm(f);
            yield return CallOff();
        }

        // Stands 6 m in front of a noise body and lets it see them and come. counter: facing it, Enter as soon as it is in
        // reach (迎击); otherwise their back to it until it runs into them (被偷袭).
        IEnumerator Charged(string id, char mark, bool counter)
        {
            var sc = S(id);
            if (!sc.Selected) yield break;
            ResetNoises();
            var e = Noise(mark);
            World.SafeUntil = 1e9f;
            Teleport(e.Position + e.Forward * 6f, YawOf(counter ? -e.Forward : e.Forward));
            yield return Frames(2);
            World.SafeUntil = 0f;
            yield return Until(() => e.State == NoiseState.Chase, 4f, $"{mark} 号察觉玩家");
            yield return Until(() => Vector3.Distance(e.Position, World.PlayerPosition) < 3.2f, 4f, $"{mark} 号追到跟前");
            var f = StartFilm(sc, null, 1f / 15f, 16, ScreenView(), StrikeSide(e));
            if (counter)
            {
                yield return Until(() => World.EnemyInReach(2.6f) == e, 2f, $"{mark} 号进入迎击距离");
                yield return Press(Key.Enter);
            }
            yield return Until(() => game.InBattle, 3f, "进入战斗");
            yield return WaitFilm(f);
            yield return CallOff();
        }

        // The battle a strike started, called off as soon as it is up, and the maze back as it was.
        IEnumerator CallOff()
        {
            yield return Until(() => game.InBattle && !game.Transitions.Busy && Stage != null && BattleMode == "Command", 30f, "偷袭后的战斗开始");
            game.Battle.Abort();
            yield return Until(() => !game.InBattle && !game.Transitions.Busy && game.Dungeon.Exploring, 30f, "中止战斗回到迷宫");
            Hush();
            ResetNoises();
            yield return Wait(0.3f);
        }

        // Side on to the player and a noise body, 5 m off their middle, chest high, held where it was put.
        View StrikeSide(SeaNoise e)
        {
            var map = World.Map;
            var player = World.PlayerPosition;
            var middle = (player + e.Position) * 0.5f + Vector3.up;
            var side = Vector3.Cross(Vector3.up, e.Position - player).normalized;
            return FixedView("side", "主角和噪声体连线侧面 2.5–5 米、胸高的固定镜头", Spot(), middle);

            Vector3 Spot()
            {
                foreach (var s in new[] { side, -side })
                    for (var d = 5f; d >= 2.5f; d -= 0.5f)
                    {
                        var c = middle + s * d + Vector3.up * 0.4f;
                        if (map.Free(c, 0.3f) && map.Clear(c, middle)) return c;
                    }
                return middle + side * 2.5f + Vector3.up * 0.4f;
            }
        }

        // ------------------------------------------------------------------ 缓存海·浅层

        // The girl in the shallows, for her gaits alone (every other scenario there is filmed with the boy).
        IEnumerator Girl()
        {
            Prepare("female", false, false);
            UnityEngine.Random.InitState(2101);
            yield return Dive("cache_sea_shallows", "潜入浅层（女主角）");
            SetMode("Busy");
            foreach (var e in World.Enemies) e.Remove();
            SetMode("Explore");
            var g = new Ground { P = "girl", Lane = new Vector2Int(11, 7) };
            if (S("girl-gait").Selected) yield return Gait(g, true);
            yield return ModelSheet(g);
            yield return Surface("离开浅层");
        }

        IEnumerator Sea()
        {
            Prepare("male", false, false);
            UnityEngine.Random.InitState(2101);
            yield return Dive("cache_sea_shallows", "潜入浅层");
            // Runs head east across 灯塔广场's south half (row 7, clear of the goal at (14,6)); the circle goes round its
            // east end, filmed from the west.
            var g = new Ground { P = "sea", Lane = new Vector2Int(11, 7), TurnFrom = new Vector2Int(13, 7), Rest = new Vector2Int(14, 20), Radius = 2.5f };
            g.CircleCentre = World.Map.World(new Vector2Int(16, 7));
            g.CircleCam = Vector3.left * 7f;

            yield return Block("浅层区域静帧", () => Zones("sea", new[]
            {
                ("reef", "气泡礁（起点 S，朝北）", 14, 20, 0f),
                ("channel", "主航道（朝北，远处是防火墙）", 12, 16, 20f),
                ("plaza", "灯塔广场（朝北看灯塔）", 12, 8, 20f),
                ("ridge", "灯塔背面（朝东）", 11, 1, 90f),
                ("coral", "表情包珊瑚林（朝北）", 2, 21, 15f),
                ("graveyard", "删帖墓地（朝北）", 5, 8, 0f),
                ("maintenance", "维护间（朝东南看缓存块和插槽）", 19, 11, 135f),
                ("canyon", "机柜峡谷（朝北）", 26, 7, 0f),
            }), "sea-zones");
            yield return Block("浅层远景", () => Wides("sea", new[]
            {
                ("lighthouse", "灯塔广场：从主航道上空往北看灯塔", Cell(14, 13) + new Vector3(0f, 11f, 0f), Cell(14, 4) + new Vector3(0f, 4f, 0f)),
                ("overview", "整个浅层：西南高处俯瞰", new Vector3(8f, 36f, 4f), new Vector3(58f, 0f, 50f)),
                ("canyon", "机柜峡谷：从维护间上方顺着峡谷往北看", Cell(26, 13) + new Vector3(0f, 7f, 0f), Cell(26, 3) + new Vector3(0f, 1f, 0f)),
                ("sky", "头顶的水面：从主航道齐人高处往北仰望，天空里的光网", Cell(14, 13) + new Vector3(0f, 1.6f, 0f), Cell(14, 4) + new Vector3(0f, 30f, 0f)),
            }), "sea-wide");
            yield return Block("浅层空气", () => Air("sea", new Vector2Int(14, 20), 0f, new Vector3(8f, 36f, 4f), new Vector3(58f, 0f, 50f),
                new Vector2Int(12, 16), 20f, new Vector2Int(16, 15), Vector3.left, new[]
                {
                    ("reef", "气泡礁（起点 S）遇敌：第一个指令镜头", 14, 20, 0f),
                    ("graveyard", "删帖墓地遇敌：第一个指令镜头", 5, 8, 0f),
                }), "sea-perf", "sea-airless", "sea-shade", "sea-border", "sea-battle-zones");
            yield return Block("浅层动作", () => Motion(g),"sea-idle", "sea-walk", "sea-run", "sea-sprint", "sea-gait", "sea-model", "sea-halt", "sea-turn", "sea-circle", "sea-follow", "sea-look", "sea-tail", "sea-steps");
            yield return Block("浅层机关", SeaProps, "sea-pearl", "sea-beacon", "sea-chest", "sea-bump", "sea-block", "sea-gate", "sea-firewall", "sea-terminal");
            yield return Block("浅层噪声体", () => SeaNoises(g), "sea-noise-rest", "sea-noise1-notice", "sea-noise1-strike", "sea-noise4-notice", "sea-noise4-strike", "sea-noise4-counter", "sea-noise4-ambush", "sea-lamp");
            yield return Block("浅层镜头", SeaCamera, "sea-near", "sea-lookup", "sea-wall");
            yield return Block("浅层布景", SeaSights, "sea-sweep", "sea-sights", "sea-sway");
            yield return Block("离开浅层", SeaLeave, "sea-leave");
            yield return Surface("离开浅层");
        }

        // The set pieces from where the player walks: the lighthouse's beams sweeping over the main channel (filmed as
        // one is about to reach the player), the landmarks up close, and the coral and kelp swaying; the noise bodies
        // are out of the way meanwhile.
        IEnumerator SeaSights()
        {
            var w = World;
            SetMode("Busy");
            foreach (var e in w.Enemies) e.Remove();
            if (S("sea-sweep").Selected)
            {
                Teleport(Cell(12, 16), 0f);
                yield return Wait(1.0f);
                var beams = w.GetComponentsInChildren<Transform>().First(t => t.name == "Beams" && t.parent.name == "Lighthouse");
                var to = w.PlayerPosition - beams.position;
                var bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                // The two beams are half a turn apart: degrees until the next one points at the player.
                yield return Until(() =>
                {
                    var d = Mathf.Repeat(bearing - beams.eulerAngles.y, 180f);
                    return d > 14f && d < 20f;
                }, 20f, "光束转到人前");
                var f = StartFilm(S("sea-sweep"), null, 0.2f, 16, ScreenView(),
                    FixedView("high", "西南高处俯瞰：光带从塔脚三十多米外一直铺到海的边上", new Vector3(8f, 36f, 4f), new Vector3(58f, 0f, 50f), 55f));
                yield return WaitFilm(f);
            }
            if (S("sea-sights").Selected)
            {
                var sc = S("sea-sights");
                foreach (var (name, what, x, y, yaw, pitch) in new[]
                {
                    ("phone", "主航道南头朝北看沉没的手机（十来米外）", 14, 16, 0f, 20f),
                    ("phone-edge", "站在手机南沿朝北：屏幕和它上面的东西贴得住、不闪", 14, 15, 0f, 20f),
                    ("coral", "表情包珊瑚林入口朝西", 9, 15, 270f, 20f),
                    ("graveyard", "删帖墓地里朝北：一排排矮碑", 3, 6, 0f, 20f),
                    ("rack-foot", "机柜峡谷西段走道朝北：墙脚", 22, 5, 0f, 20f),
                    ("canyon-up", "机柜峡谷朝北抬头到 −12°：头顶的线缆", 26, 7, 0f, -12f),
                    ("plaza", "灯塔广场东南角朝西北看灯塔脚下", 17, 8, 315f, 20f),
                })
                {
                    Teleport(Cell(x, y), yaw);
                    fPitch.SetValue(w, pitch);
                    mUpdateCamera.Invoke(w, new object[] { 0f, true });
                    yield return Wait(1.0f);
                    yield return Shoot(sc, "sea-sight-" + name, what);
                }
                fPitch.SetValue(w, 20f);
                mUpdateCamera.Invoke(w, new object[] { 0f, true });
            }
            if (S("sea-sway").Selected)
            {
                Teleport(Cell(9, 15), 270f);
                yield return Wait(1.0f);
                var f = StartFilm(S("sea-sway"), null, 0.5f, 8,
                    FixedView("coral", "珊瑚林里的固定近景：头在摇、茎不断，后面的海草也在摆", Cell(8, 14) + new Vector3(1.5f, 1.5f, -1f), Cell(8, 13) + new Vector3(-1f, 1.4f, 0f), 50f));
                yield return WaitFilm(f);
            }
            ResetNoises();
            SetMode("Explore");
        }

        // The scenery making way for the lens (Toon.shader _Fade): the camera behind the player in the one-cell lane north
        // of the reef, its walls a couple of metres off the lens, then with the player at the lane's east side, so the
        // camera runs along the bubble stack half a metre off it.
        IEnumerator SeaCamera()
        {
            if (S("sea-near").Selected)
            {
                SetMode("Busy");
                Teleport(Cell(14, 19), 180f);
                yield return Wait(1.0f);
                yield return Shoot(S("sea-near"), "sea-near-lane", "气泡礁朝南（游戏画面）：镜头在北边走道里，两侧的布景离镜头两米多，照常画");
                Teleport(Cell(14, 19) + Vector3.right * 1.3f, 180f);
                yield return Wait(1.0f);
                yield return Shoot(S("sea-near"), "sea-near-wall", "同一处，人贴着东边（游戏画面）：镜头擦着右手的气泡堆，贴着镜头的那段变成网点");
                SetMode("Explore");
            }
            if (S("sea-lookup").Selected)
            {
                // The player's own pitch, as the mouse would set it; put back to the default after.
                SetMode("Busy");
                Teleport(Cell(14, 20), 0f);
                yield return Wait(1.0f);
                yield return Shoot(S("sea-lookup"), "sea-lookup-level", "起点朝北（游戏画面）：默认俯仰 20°，灯塔只露出塔身；" + Framing());
                fPitch.SetValue(World, -12f);
                mUpdateCamera.Invoke(World, new object[] { 0f, true });
                yield return Wait(0.5f);
                yield return Shoot(S("sea-lookup"), "sea-lookup-up", "同一处抬头到 −12°（游戏画面）：机位降到 6° 后视线再上抬（最多 18°，主角的头留在画面底边），灯室、光晕和光束进画面");
                fPitch.SetValue(World, 20f);
                mUpdateCamera.Invoke(World, new object[] { 0f, true });
                SetMode("Explore");
            }
            if (S("sea-wall").Selected)
            {
                // The racks south of the channel's west side behind the player and the camera: pulled in to start, let out
                // as they walk off north, pulled in again as they come back (facing the camera then).
                var w = World;
                var wall = Cell(12, 17).z - 2f;
                Teleport(new Vector3(Cell(12, 17).x, 0f, wall + 0.9f), 0f);
                yield return Wait(1.0f);
                var f = StartFilm(S("sea-wall"), null, 0.1f, 92, ScreenView());
                yield return Wait(0.6f);
                yield return Until(() =>
                {
                    Stick(StickFor(Vector3.forward, 0.68f), false);
                    return w.PlayerPosition.z > wall + 8.5f;
                }, 6f, "从墙前走到空地");
                Release();
                yield return Wait(0.8f);
                yield return Until(() =>
                {
                    Stick(StickFor(Vector3.back, 0.68f), false);
                    return w.PlayerPosition.z < wall + 1.1f;
                }, 6f, "退回墙前");
                Release();
                yield return WaitFilm(f);
            }
        }

        IEnumerator SeaProps()
        {
            var w = World;
            if (S("sea-pearl").Selected)
            {
                var pearl = w.Pearls.First(p => p.Cell == new Vector2Int(15, 20));
                Teleport(Cell(14, 20), 90f);
                yield return Wait(1.0f);
                var f = StartFilm(S("sea-pearl"), null, 0.1f, 16, ScreenView(),
                    FixedView("near", "珍珠旁的固定镜头", Cell(15, 19) + new Vector3(-1.2f, 1.3f, 0f), pearl.Position + Vector3.up * 0.3f));
                yield return Wait(0.1f);
                Stick(StickFor(Vector3.right, 1f), false);
                yield return Until(() => pearl.Taken, 3f, "捡到珍珠");
                Release();
                yield return WaitFilm(f);
            }
            if (S("sea-beacon").Selected)
            {
                var sc = S("sea-beacon");
                var beacon = w.Props.First(p => p.Kind == PropKind.Beacon && p.Cell == new Vector2Int(5, 15));
                Teleport(Cell(5, 17), 0f);
                yield return Wait(1.2f);
                Begin(sc);
                yield return Shoot(sc, "sea-beacon-before", "信标点亮前（游戏画面）");
                var f = StartFilm(sc, null, 0.133f, 16, ScreenView(),
                    FixedView("near", "信标西南侧的固定镜头", Cell(3, 16) + new Vector3(0f, 1.8f, 0f), beacon.Position + Vector3.up * 1.6f));
                yield return Wait(0.13f);
                Stick(StickFor(Vector3.forward, 1f), false);
                yield return Until(() => beacon.Used, 3f, "走到信标旁");
                Release();
                yield return WaitFilm(f);
                yield return Wait(0.5f);
                yield return Shoot(sc, "sea-beacon-after", "信标休息后（游戏画面）");
                End(sc);
            }
            if (S("sea-chest").Selected)
            {
                var chest = w.Props.First(p => p.Kind == PropKind.Chest && p.Cell == new Vector2Int(9, 21));
                Teleport(Cell(9, 21) + new Vector3(0f, 0f, 2.4f), 180f);
                yield return Wait(1.0f);
                var f = StartFilm(S("sea-chest"), null, 0.08f, 24, ScreenView(),
                    FixedView("near", "宝箱西北侧的固定镜头", Cell(9, 21) + new Vector3(-2.4f, 1.5f, 2.2f), chest.Position + Vector3.up * 0.6f));
                yield return Wait(0.1f);
                yield return Press(Key.Enter);
                yield return WaitFilm(f);
                if (!chest.Used) Fail(S("sea-chest"), "宝箱没有打开");
            }
            if (S("sea-bump").Selected)
            {
                // The middle block of the north row pushed against the racks: once into (21,11), against them; then
                // again, and it knocks against the racks. The push is taken back after, for the blocks' own run.
                var sc = S("sea-bump");
                var at = new Vector2Int(21, 12);
                var step = Vector2Int.down; // north
                var d3 = DungeonMap.Direction(step);
                Teleport(w.Map.World(at) - d3 * 2.6f, YawOf(d3));
                yield return Frames(3);
                yield return Press(Key.Enter);
                yield return Wait(0.6f);
                if (!w.Map.Blocks.Contains(at + step)) throw new LabFailure("缓存块没推到北边机柜前");
                Teleport(w.Map.World(at + step) - d3 * 2.6f, YawOf(d3));
                yield return Wait(1.0f);
                var f = StartFilm(sc, null, 0.05f, 16, ScreenView(),
                    FixedView("near", "缓存块南侧的固定镜头", Cell(22, 13) + new Vector3(0f, 1.6f, 0f), Cell(21, 11) + Vector3.up * 1.2f));
                yield return Wait(0.05f);
                yield return Press(Key.Enter);
                yield return WaitFilm(f);
                if (!w.Map.Blocks.Contains(at + step)) Fail(sc, "推不动的缓存块动了");
                Teleport(w.Map.World(at) - d3 * 2.6f, YawOf(d3));
                yield return Frames(3);
                yield return Press(Key.Backspace);
                yield return Wait(0.6f);
                if (!w.Map.Blocks.Contains(at)) throw new LabFailure("撤回后缓存块没回到原处");
            }
            if (S("sea-block").Selected || S("sea-gate").Selected)
            {
                var pushes = new[]
                {
                    (new Vector2Int(21, 15), Vector2Int.down), (new Vector2Int(20, 13), Vector2Int.right), (new Vector2Int(21, 13), Vector2Int.right),
                    (new Vector2Int(20, 11), Vector2Int.up), (new Vector2Int(19, 13), Vector2Int.right), (new Vector2Int(22, 13), Vector2Int.right),
                    (new Vector2Int(24, 12), Vector2Int.up), (new Vector2Int(25, 14), Vector2Int.left), (new Vector2Int(24, 14), Vector2Int.left),
                    (new Vector2Int(20, 13), Vector2Int.right), (new Vector2Int(21, 13), Vector2Int.right), (new Vector2Int(22, 13), Vector2Int.right),
                    (new Vector2Int(21, 11), Vector2Int.up), (new Vector2Int(20, 13), Vector2Int.right), (new Vector2Int(21, 13), Vector2Int.right),
                    (new Vector2Int(24, 12), Vector2Int.up), (new Vector2Int(25, 14), Vector2Int.left), (new Vector2Int(22, 13), Vector2Int.right),
                    (new Vector2Int(24, 12), Vector2Int.up),
                };
                Film block = null, gate = null;
                for (var i = 0; i < pushes.Length; i++)
                {
                    var (from, dir) = pushes[i];
                    var at = from + dir;
                    if (!w.Map.Blocks.Contains(at)) throw new LabFailure($"第 {i + 1} 推：{at} 没有缓存块");
                    var d3 = DungeonMap.Direction(dir);
                    Teleport(w.Map.World(at) - d3 * 2.6f, YawOf(d3));
                    yield return Frames(3);
                    if (i == pushes.Length - 1)
                    {
                        yield return Wait(0.5f);
                        block = StartFilm(S("sea-block"), null, 0.1f, 16, ScreenView(),
                            FixedView("near", "插槽东北侧的固定镜头", Cell(26, 13) + new Vector3(0f, 3f, 0f), Cell(24, 14) + Vector3.up * 1.2f));
                        // From here the gate is at the top of the picture past the racks: the game cuts away to it.
                        gate = StartFilm(S("sea-gate"), null, 0.167f, 16, ScreenView(),
                            FixedView("gate", "维护间里、闸门北面的固定镜头", Cell(23, 15) + new Vector3(0f, 1.6f, 1.2f), Cell(23, 16) + Vector3.up * 1.4f, 55f));
                        yield return Wait(0.1f);
                    }
                    yield return Press(Key.Enter);
                    yield return Wait(0.45f);
                    if (!w.Map.Blocks.Contains(at + dir)) throw new LabFailure($"第 {i + 1} 推没推动：{at} → {at + dir}");
                    if (i == pushes.Length - 1 && S("sea-gate").Selected && game.Dungeon.Cut != "gate") Fail(S("sea-gate"), "闸门在画面顶上、隔着机柜，却没有切插入镜头");
                }
                yield return WaitFilm(block);
                yield return WaitFilm(gate);
                if (!w.Puzzles.Solved) Fail(S("sea-gate"), "三块缓存块推完了闸门还没开");
            }
            if (S("sea-firewall").Selected)
            {
                // The terminal's lines as the story has them: the way is open at once, and <<reveal firewall>> shows the
                // walls going under them. The lab has no story, so the command is run here, before the terminal's own run
                // (which, with no lines to show them under, settles the walls at once).
                var sc = S("sea-firewall");
                Teleport(Cell(23, 17) + new Vector3(-2.4f, 0f, 0f), 90f);
                yield return Wait(1.0f);
                w.OpenFirewall(true);
                SetMode("Talk");
                var f = StartFilm(sc, null, 0.1f, 40, ScreenView());
                yield return Wait(0.1f);
                yield return Await(game.Dungeon.Reveal("firewall"), 10f, "防火墙的插入镜头");
                yield return WaitFilm(f);
                SetMode("Explore");
                if (w.Firewalls.Any(x => !x.Opened)) Fail(sc, "镜头放完还有防火墙没溶解");
            }
            if (S("sea-terminal").Selected)
            {
                Teleport(Cell(23, 17) + new Vector3(-2.4f, 0f, 0f), 90f);
                yield return Wait(1.0f);
                // The screen faces the way its root does, tipped back 20°: square on, 1.3 m out, to read the log by.
                var term = w.Props.First(p => p.Kind == PropKind.Terminal).Root;
                var screen = term.position + Vector3.up * 1.24f + term.forward * 0.12f;
                var f = StartFilm(S("sea-terminal"), null, 0.133f, 16, ScreenView(),
                    FixedView("near", "终端东侧的固定镜头", Cell(24, 17) + new Vector3(0.6f, 1.5f, 0.8f), Cell(23, 17) + new Vector3(-0.5f, 1.0f, 0f), 55f),
                    FixedView("screen", "正对终端屏幕：日志往上滚，读得出行", screen + Quaternion.AngleAxis(-20f, term.right) * term.forward * 1.3f, screen, 40f));
                yield return Wait(0.13f);
                yield return Press(Key.Enter);
                yield return WaitFilm(f);
                if (!w.Map.FirewallOpen) Fail(S("sea-terminal"), "防火墙没有打开");
            }
        }

        // The way out of the shallows (DungeonScreen.Goal: no lines here, the departure, then out): run straight from the
        // goal, as the walk into it would, without the leaving (the lab surfaces on its own after).
        IEnumerator SeaLeave()
        {
            var sc = S("sea-leave");
            var w = World;
            SetMode("Busy");
            foreach (var e in w.Enemies) e.Remove();
            Teleport(Cell(14, 6), 0f);
            yield return Wait(1.0f);
            var f = StartFilm(sc, null, 0.1f, 24, ScreenView(),
                FixedView("plaza", "灯塔广场西南高处：灯转下来照住两人", Cell(11, 10) + new Vector3(0f, 7f, 0f), Cell(14, 5) + Vector3.up * 5f, 55f));
            yield return Wait(0.1f);
            yield return Await(w.Depart(() => false, () => false), 6f, "浅层的离场");
            yield return WaitFilm(f);
        }

        IEnumerator SeaNoises(Ground g)
        {
            if (S("sea-noise-rest").Selected) yield return NoiseRest("sea", g.Rest);
            yield return Notice("sea-noise1-notice", '1', 4f, Vector3.left);
            yield return Strike("sea-noise1-strike", '1');
            yield return Notice("sea-noise4-notice", '4', 6f, Vector3.back);
            yield return Strike("sea-noise4-strike", '4');
            yield return Charged("sea-noise4-counter", '4', true);
            yield return Charged("sea-noise4-ambush", '4', false);
            yield return Lamp("sea-lamp", '4', Vector3.back);
        }

        // The seabed's glow round the player (DungeonWorld.ApplyGlobals): standing, half a breath apart (its reach between
        // 6.2 and 6.8 m), then sprinting off along `run` with a noise body after them (7.5 m, 35 % of the way to red).
        // Looked down on from over the player and from the game.
        IEnumerator Lamp(string id, char mark, Vector3 run)
        {
            var sc = S(id);
            if (!sc.Selected) yield break;
            ResetNoises();
            var e = Noise(mark);
            World.SafeUntil = 1e9f;
            Teleport(e.Position + e.Forward * 6f, YawOf(-e.Forward));
            yield return Wait(1.0f);
            Func<CamPose> over = () => new CamPose { At = World.PlayerPosition + new Vector3(0f, 12f, -4f), Look = World.PlayerPosition, Fov = 60f };
            yield return Shoot(sc, id + "-rest", "站着（游戏画面）");
            yield return Shoot(sc, id + "-rest-top-a", "站着，俯拍", World.Camera, over);
            yield return Wait(Mathf.PI / 1.3f);
            yield return Shoot(sc, id + "-rest-top-b", "站着，半个呼吸之后，俯拍", World.Camera, over);
            // It has patrolled on during the stills: back in front of it before it is let see them.
            Teleport(e.Position + e.Forward * 6f, YawOf(-e.Forward));
            yield return Frames(2);
            World.SafeUntil = 0f;
            yield return Until(() => e.State == NoiseState.Chase, 4f, $"{mark} 号察觉玩家");
            Stick(StickFor(run, 1f), true);
            yield return Wait(1.0f);
            yield return Shoot(sc, id + "-chased", "被追着冲刺（游戏画面）");
            yield return Shoot(sc, id + "-chased-top", "被追着冲刺，俯拍", World.Camera, over);
            Release();
            World.SafeUntil = 1e9f;
            yield return Wait(0.5f);
            ResetNoises();
            Hush();
        }

        // ------------------------------------------------------------------ 缓存海·深层

        IEnumerator Deep()
        {
            Prepare("male", true, true);
            UnityEngine.Random.InitState(2202);
            yield return Dive("cache_sea_deep", "潜入深层");
            var g = new Ground { P = "deep", Lane = new Vector2Int(11, 17), TurnFrom = new Vector2Int(14, 17), Rest = new Vector2Int(14, 23), Radius = 2.5f };
            g.CircleCentre = World.Map.World(new Vector2Int(14, 17)) + Vector3.back * 2f;

            yield return Block("深层区域静帧", () => Zones("deep", new[]
            {
                ("rift", "裂缝（起点 S，朝北）", 14, 23, 0f),
                ("servers", "沉没机房（朝北）", 14, 17, 0f),
                ("context", "长上下文回廊（朝北）", 27, 16, 0f),
                ("hall", "白门大厅（朝北看白门）", 14, 7, 0f),
                ("bin", "回收站（朝北）", 4, 19, 0f),
            }), "deep-zones");
            yield return Block("深层远景", () => Wides("deep", new[]
            {
                ("hall", "白门大厅：南侧高处往北看白门", Cell(14, 10) + new Vector3(0f, 14f, 0f), Cell(14, 2) + new Vector3(0f, 3f, 0f)),
                ("servers", "沉没机房：裂缝上空往北看", Cell(14, 22) + new Vector3(0f, 18f, 0f), Cell(14, 12)),
                ("context", "长上下文回廊：东南高处往北看", Cell(25, 22) + new Vector3(0f, 15f, 0f), Cell(25, 10)),
                ("context-log", "长上下文回廊：南口消息的高度往北看，近处是最新的消息，越往里越淡越灰", Cell(27, 17) + new Vector3(0f, 4.6f, 0f), Cell(27, 3) + new Vector3(0f, 4.8f, 0f)),
                ("context-oldest", "长上下文回廊北头：从东端低处往西看最早的几条，最淡最灰，最里面一条是「…（已截断）」", Cell(27, 2) + new Vector3(1.2f, 2.8f, 0f), Cell(21, 2) + new Vector3(0f, 4.8f, 0f)),
            }), "deep-wide");
            yield return Block("深层空气", () => Air("deep", new Vector2Int(14, 23), 0f, Cell(14, 22) + new Vector3(0f, 18f, 0f), Cell(14, 12),
                new Vector2Int(14, 17), 0f, new Vector2Int(2, 4), Vector3.right, new[]
                {
                    ("rift", "裂缝（起点 S）遇敌：第一个指令镜头", 14, 23, 0f),
                    ("hall", "白门大厅遇敌：第一个指令镜头", 14, 7, 0f),
                }), "deep-perf", "deep-airless", "deep-shade", "deep-border", "deep-battle-zones");
            yield return Block("深层珍珠", DeepPearl, "deep-pearl");
            yield return Block("深层机柜", DeepRack, "deep-rack");
            yield return Block("深层动作", () => Motion(g), "deep-idle", "deep-walk", "deep-run", "deep-sprint", "deep-gait", "deep-model", "deep-halt", "deep-turn", "deep-circle", "deep-follow", "deep-look", "deep-tail", "deep-steps");
            yield return Block("深层噪声体", () => DeepNoises(g), "deep-noise-rest", "deep-noise1-notice", "deep-noise1-strike", "deep-noise2-notice", "deep-noise2-strike", "deep-noise2-counter", "deep-noise2-ambush", "deep-noise5-notice", "deep-noise5-strike");
            yield return Block("深层超时", DeepTimeout, "deep-timeout", "deep-calm");
            yield return Block("深层机关", DeepProps, "deep-plate", "deep-exit", "deep-mirror-hurry", "deep-mirror", "deep-leave");
            yield return Surface("离开深层");
        }

        // The rift's pearl by the start, first of the dive (E2E dungeon-deep): up the one-cell way and round the corner
        // to it, the camera in close between the racks. Before anything else here has taken it.
        IEnumerator DeepPearl()
        {
            var sc = S("deep-pearl");
            if (!sc.Selected) yield break;
            var w = World;
            var pearl = w.Pearls.First(p => p.Cell == new Vector2Int(13, 22));
            Teleport(Cell(14, 23), 0f);
            yield return Wait(1.2f);
            var f = StartFilm(sc, null, 0.1f, 24, ScreenView(),
                FixedView("near", "珍珠北边的固定镜头", Cell(13, 21) + new Vector3(-0.8f, 1.5f, 0f), pearl.Position + Vector3.up * 0.4f));
            yield return Wait(0.1f);
            var corner = Cell(14, 22).z - 0.3f;
            yield return Until(() =>
            {
                Stick(StickFor(Vector3.forward, 1f), false);
                return w.PlayerPosition.z > corner;
            }, 3f, "走到裂缝的拐角");
            yield return Until(() =>
            {
                Stick(StickFor(pearl.Position - w.PlayerPosition, 1f), false);
                return pearl.Taken;
            }, 3f, "捡到珍珠");
            Release();
            yield return WaitFilm(f);
        }

        // The avatar's lamp on a rack it walks up to (Toon.shader _Gloss), and the rack dark at its foot (_FootDark).
        IEnumerator DeepRack()
        {
            if (S("deep-rack").Selected)
            {
                SetMode("Busy");
                Teleport(Cell(16, 11) + Vector3.forward * 0.5f, 0f);
                yield return Wait(1.0f);
                yield return Shoot(S("deep-rack"), "deep-rack", "沉没机房（游戏画面）：人朝北站在机柜前一米半，柜面上的分段高光和压暗的柜脚");
                SetMode("Explore");
            }
        }

        // The meter run out (DungeonScreen.CheckMeter): the dark on its own first, as long as the lines over it would
        // give it (the lab has none, so the real run cuts it short at the flash), then the real thing from 99.9%. With
        // 减弱闪光 on (deep-calm), the beacon's event light and the bounce's flash instead.
        IEnumerator DeepTimeout()
        {
            var w = World;
            foreach (var (id, calm) in new[] { ("deep-timeout", false), ("deep-calm", true) })
            {
                var sc = S(id);
                if (!sc.Selected) continue;
                Begin(sc);
                game.Settings.reduceFlash = calm;
                try
                {
                    if (!calm)
                    {
                        // Facing east down the sunken servers' long way, racks at every distance.
                        SetMode("Busy");
                        Teleport(Cell(14, 17), 90f);
                        yield return Wait(1.0f);
                        var dark = StartFilm(sc, id + "-dark", 0.2f, 16, ScreenView(),
                            FixedView("high", "沉没机房上空往北：远处的机柜先没入黑暗", Cell(14, 22) + new Vector3(0f, 18f, 0f), Cell(14, 12)));
                        yield return Wait(0.1f);
                        yield return Await(w.Blackout(2.5f, () => false), 5f, "黑场");
                        yield return WaitFilm(dark);
                        yield return Await(w.Restore(0.3f), 2f, "雾散开");
                        SetMode("Explore");
                    }
                    else
                    {
                        // The beacon in the bin, walked up to from the south, a step short of it.
                        var beacon = w.Props.First(p => p.Kind == PropKind.Beacon && p.Cell == new Vector2Int(4, 17));
                        Teleport(beacon.Position + Vector3.back * (beacon.Reach + 1.2f), 0f);
                        yield return Wait(1.0f);
                        var lit = StartFilm(sc, id + "-beacon", 0.1f, 16, ScreenView());
                        yield return Wait(0.1f);
                        Stick(StickFor(Vector3.forward, 1f), false);
                        yield return Until(() => beacon.Used, 3f, "走到信标旁");
                        Release();
                        yield return WaitFilm(lit);
                        Hush();
                    }
                    Teleport(Cell(14, 17), 90f);
                    yield return Wait(1.0f);
                    fMeter.SetValue(game.Dungeon, 99.9f);
                    var bounce = StartFilm(sc, null, 0.1f, 24, ScreenView());
                    yield return Until(() =>
                    {
                        Stick(StickFor(Vector3.right, 1f), false);
                        return (float)fMeter.GetValue(game.Dungeon) >= 100f;
                    }, 3f, "走到超时");
                    Release();
                    yield return Until(() => (float)fMeter.GetValue(game.Dungeon) < 99f, 5f, "拖回去");
                    yield return WaitFilm(bounce);
                    yield return Until(() => game.Dungeon.Exploring, 3f, "回到探索");
                }
                finally
                {
                    game.Settings.reduceFlash = false;
                    End(sc);
                }
                Hush();
                ResetNoises();
            }
        }

        IEnumerator DeepNoises(Ground g)
        {
            if (S("deep-noise-rest").Selected) yield return NoiseRest("deep", g.Rest);
            yield return Notice("deep-noise1-notice", '1', 4f, Vector3.forward);
            yield return Strike("deep-noise1-strike", '1');
            yield return Notice("deep-noise2-notice", '2', 6f, Vector3.right);
            yield return Strike("deep-noise2-strike", '2');
            yield return Charged("deep-noise2-counter", '2', true);
            yield return Charged("deep-noise2-ambush", '2', false);
            yield return Notice("deep-noise5-notice", '5', 6f, Vector3.left);
            yield return Strike("deep-noise5-strike", '5');
        }

        IEnumerator DeepProps()
        {
            var w = World;
            if (S("deep-plate").Selected || S("deep-exit").Selected)
            {
                // The east way round: (14,6) | → /, (17,3) | → \, (11,3) | → \ (both back with Backspace); (17,6) starts at / .
                var turns = new[]
                {
                    (new Vector2Int(14, 6), new Vector2Int(14, 5), 1), (new Vector2Int(17, 3), new Vector2Int(18, 3), -1),
                    (new Vector2Int(11, 3), new Vector2Int(10, 3), -1),
                };
                for (var k = 0; k < turns.Length; k++)
                {
                    var (plate, stand, times) = turns[k];
                    var centre = w.Map.World(plate);
                    var away = (w.Map.World(stand) - centre).normalized;
                    Teleport(centre + away * 2.6f, YawOf(-away));
                    yield return Frames(3);
                    for (var j = 0; j < Mathf.Abs(times); j++)
                    {
                        Film f = null;
                        if (k == 0 && j == 0)
                        {
                            yield return Wait(0.5f);
                            f = StartFilm(S("deep-plate"), null, 0.1f, 16, ScreenView(),
                                FixedView("near", "反射板东北侧的固定镜头", centre + new Vector3(3.0f, 2.2f, 2.6f), centre + Vector3.up * 1.0f));
                            yield return Wait(0.1f);
                        }
                        if (k == turns.Length - 1)
                        {
                            yield return Wait(0.5f);
                            f = StartFilm(S("deep-exit"), null, 0.133f, 32, ScreenView(),
                                FixedView("exit", "看终点的固定镜头", Cell(10, 5) + new Vector3(0f, 2f, 0f), Cell(11, 2) + Vector3.up * 1.0f));
                            yield return Wait(0.13f);
                        }
                        yield return Press(times < 0 ? Key.Backspace : Key.Enter);
                        yield return Wait(0.45f);
                        if (f != null) yield return WaitFilm(f);
                    }
                }
                if (!w.Puzzles.Lit) Fail(S("deep-exit"), "四块反射板转完了光还没照到终点");
                yield return Until(() => w.Puzzles.LightArrived && !game.Dungeon.Cutting && game.Dungeon.Exploring, 6f, "出口的插入镜头放完");
            }
            if (S("deep-mirror-hurry").Selected)
            {
                // Enter 0.4 s into the walk: the rest at three times the pace, DeepSeek still walking to her place (her
                // steps tracked for a jump). The mirror is left for deep-mirror.
                var sc = S("deep-mirror-hurry");
                SetMode("Busy");
                Begin(sc);
                Teleport(Cell(11, 2), 0f);
                yield return Wait(1.0f);
                var steps = WatchSteps(w.Companion.Root);
                var f = StartFilm(sc, null, 0.05f, 32, ScreenView());
                yield return Wait(0.05f);
                var job = Run(game.Dungeon.MirrorWalk());
                yield return Wait(0.4f);
                yield return Press(Key.Enter);
                yield return Until(() => job.Done, 10f, "催过的走到白门前");
                yield return WaitFilm(f);
                yield return Wait(0.3f);
                yield return Shoot(sc, "deep-mirror-hurry-end", "催过之后停在门前（游戏画面）");
                NoteSteps(sc, steps, "走到门前（催过）");
                End(sc);
            }
            if (S("deep-mirror").Selected)
            {
                // Last of all: the door stays a mirror and DeepSeek stays where the scene stood her.
                var sc = S("deep-mirror");
                SetMode("Busy");
                Begin(sc);
                // From the goal beside it, where the white door's lines start.
                Teleport(Cell(11, 2), 0f);
                yield return Wait(1.0f);
                var steps = WatchSteps(w.Companion.Root);
                var walk = StartFilm(sc, sc.Id + "-walk", 0.1f, 54, ScreenView());
                yield return Wait(0.1f);
                yield return Await(game.Dungeon.MirrorWalk(), 10f, "走到白门前");
                var walked = Time.unscaledTime - steps.Start;
                var her = w.Companion.Root.position;
                var him = w.Avatar.Root.position;
                yield return WaitFilm(walk);
                // The ripple (0.8 s) at 20 frames a second, then the band settling the reflection (0.5 s).
                var f = StartFilm(sc, null, 0.05f, 24, ScreenView());
                var job = Run(game.Dungeon.Mirror(true));
                yield return Until(() => job.Done, 10f, "镜子打开");
                NoteSteps(sc, steps, "走到门前、镜子打开");
                sc.Notes.Add($"走到门前用了 {walked:0.00} 秒；<<mirror on>> 把 DeepSeek 挪了 {Flat(w.Companion.Root.position - her):0.00} 米，主角 {Flat(w.Avatar.Root.position - him):0.00} 米");
                yield return WaitFilm(f);
                yield return Wait(0.3f);
                yield return Shoot(sc, "deep-mirror-still", "镜子停稳后的游戏画面");
                var settle = StartFilm(sc, sc.Id + "-settle", 0.05f, 16, ScreenView());
                yield return Await(game.Dungeon.Mirror(false), 10f, "镜子收回");
                yield return WaitFilm(settle);
                End(sc);
            }
            if (S("deep-leave").Selected)
            {
                // After the white door's lines (DungeonScreen.Goal): where <<mirror on>> stood the two, from its camera.
                var sc = S("deep-leave");
                SetMode("Busy");
                if (!S("deep-mirror").Selected)
                {
                    yield return Await(game.Dungeon.Mirror(true), 10f, "镜子打开");
                    yield return Await(game.Dungeon.Mirror(false), 10f, "镜子收回");
                }
                var f = StartFilm(sc, null, 0.1f, 16, ScreenView(),
                    FixedView("hall", "白门大厅西南：光柱从两人中间升起", Cell(11, 5) + new Vector3(0f, 3f, 0f), Cell(14, 2) + Vector3.up * 2f, 55f));
                yield return Wait(0.1f);
                yield return Await(w.Depart(() => false, () => false), 5f, "深层的离场");
                yield return WaitFilm(f);
            }
        }

        // ------------------------------------------------------------------ the index

        bool WriteIndex()
        {
            var sb = new StringBuilder();
            var selected = scenarios.Where(s => s.Selected).ToList();
            var problems = new List<string>();
            problems.AddRange(runErrors);
            lock (writeErrors) problems.AddRange(writeErrors.Select(e => "写文件失败：" + e));
            problems.AddRange(unknownTokens.Select(t => $"-labOnly 里的「{t}」不对应任何场景"));
            foreach (var s in selected)
            {
                if (s.Failed) problems.Add($"{s.Id} 失败");
                else if (!s.Captured) problems.Add($"{s.Id} 没有拍到");
                else if (s.Errors.Count > 0) problems.Add($"{s.Id} 期间有 {s.Errors.Count} 条错误日志");
            }
            if (finished && (writing.Count > 0 || Volatile.Read(ref pendingWrites) > 0)) problems.Add("有图片一分钟后还没写完");
            var ok = problems.Count == 0 && finished;

            sb.AppendLine("# 视觉实验室");
            sb.AppendLine();
            sb.AppendLine($"- 结果：{(ok ? "全部完成" : "有问题")}（{selected.Count} 个场景，用时 {Time.realtimeSinceStartup - started:0} 秒）");
            sb.AppendLine($"- 时钟：游戏按真实时间跑，限 {Pace} 帧/秒（{Pace} 帧实测 {clockMeasured:0.000} 秒），胶片的帧取在时间点上或之后最近的一帧；界面时钟{(panelClock ? "已接到帧时间" : "没接上（见下）")}");
            sb.AppendLine($"- 窗口 {Screen.width}×{Screen.height}；胶片每帧 {FrameW}×{FrameH}，每行 {Columns} 帧、从左到右从上到下读，每张最多 {PerSheet} 帧；屏幕截图{(flipScreen ? "按 Metal 上下翻转过" : "未翻转")}");
            sb.AppendLine("- 游戏状态只在内存里（临时 HOME），设置为默认值：画面震动开、减少闪烁关、静音");
            if (only != null) sb.AppendLine($"- 只跑：{string.Join(", ", only)}");
            if (timings.Count > 0) sb.AppendLine($"- 各组用时：{string.Join("；", timings)}");
            sb.AppendLine();
            if (problems.Count > 0)
            {
                sb.AppendLine("## 问题");
                sb.AppendLine();
                foreach (var p in problems) sb.AppendLine("- " + p);
                sb.AppendLine();
            }
            if (groupLog.Count > 0)
            {
                sb.AppendLine("## 场景以外的日志");
                sb.AppendLine();
                foreach (var l in groupLog) sb.AppendLine("- " + l);
                sb.AppendLine();
            }
            foreach (var group in scenarios.Select(s => s.Group).Distinct())
            {
                var list = scenarios.Where(s => s.Group == group).ToList();
                sb.AppendLine($"## {group}");
                sb.AppendLine();
                foreach (var s in list)
                {
                    var state = !s.Selected ? "没选" : s.Failed ? "失败" : s.Captured ? (s.Errors.Count > 0 ? "拍到了，但有错误日志" : "完成") : "没拍到";
                    sb.AppendLine($"### {s.Id}（{state}）");
                    sb.AppendLine();
                    sb.AppendLine(s.Shows);
                    sb.AppendLine();
                    foreach (var o in s.Outputs) sb.AppendLine("- " + o);
                    foreach (var n in s.Notes) sb.AppendLine("- 备注：" + n);
                    foreach (var e in s.Errors) sb.AppendLine("- 错误：" + e);
                    foreach (var w in s.Warnings) sb.AppendLine("- 警告：" + w);
                    sb.AppendLine();
                }
            }
            try
            {
                File.WriteAllText(Path.Combine(outDir, "index.md"), sb.ToString());
            }
            catch (Exception e)
            {
                Debug.Log("[lab] index: " + e.Message);
                return false;
            }
            return ok;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            foreach (var rt in new[] { screenRT, midRT, camRT, fullRT, fullFlat }.Concat(smallRTs))
                if (rt != null) rt.Release();
        }
    }
}
#endif
