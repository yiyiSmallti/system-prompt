#if UNITY_EDITOR || SP_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SystemPrompt.Dungeon;
using SystemPrompt.UI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace SystemPrompt.E2E
{
    // A filmed tour of the game in the E2E player build (game/Tools/e2e.sh tour), each thing shown for a moment
    // and the stretches between cut: the same virtual keyboard as PrologueRun walks the title, the save list
    // and the settings pages, registers, reads the prologue a line or two per kind of line (the date card, a
    // choice, a system toast, the phone, the pause menu, DeepSeek's thinking bubble, a whisper, the phone's
    // screen and the fall into the latent space), then creeps up on the dozing noise in the cache sea's
    // shallows, fights the 3D battle's first round, and is back for the enemies' turn, the results and a few
    // steps toward the coral. While the camera rolls, the screen is read back from the GPU 30 times a second and saved as a JPEG
    // with its film time, and the mixed sound is taken off the audio listener into a WAV; e2e.sh joins them
    // into tour.mp4. Only starts when the player is launched with `-tour <output dir>`.
    public sealed class GameTour : MonoBehaviour
    {
        const int Fps = 30;

        string outDir;
        Keyboard keyboard;
        GameRoot game;
        VisualElement ui;
        SoundTap tap;
        RenderTexture screen;
        // Film time runs only while the camera rolls, so a cut drops the same stretch from picture and sound.
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        readonly List<float> frames = new List<float>(); // film seconds, one per saved frame
        int pending;
        bool filming;
        bool rolling;
        string failure;

        DungeonScreen Maze => game.Dungeon;
        DungeonWorld World => game.Dungeon.World;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-tour");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("E2E Game Tour");
            DontDestroyOnLoad(go);
            go.AddComponent<GameTour>().outDir = args[i + 1];
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(Path.Combine(outDir, "frames"));
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("Tour Keyboard");
            foreach (var device in InputSystem.devices.OfType<Pointer>().ToList()) InputSystem.DisableDevice(device);
            Application.logMessageReceived += OnLog;

            while (GameRoot.Instance == null) yield return null;
            game = GameRoot.Instance;
            ui = game.GetComponent<UIDocument>().rootVisualElement;
            tap = FindFirstObjectByType<AudioListener>().gameObject.AddComponent<SoundTap>();
            StartCoroutine(Film());
            yield return Title();
            if (failure == null) yield return Register();
            if (failure == null) yield return Story();
            if (failure == null) yield return Shallows();
            Roll(false);
            filming = false;
            while (Volatile.Read(ref pending) > 0) yield return null;
            WriteFrameList();
            tap.WriteWav(Path.Combine(outDir, "sound.wav"));
            File.WriteAllText(Path.Combine(outDir, "tour.txt"), failure ??
                $"ok {frames.Count} frames, picture {clock.Elapsed.TotalSeconds:0.00} s, sound {tap.Seconds:0.00} s\nsound {tap.Gaps}\n");
            Application.Quit(failure == null ? 0 : 1);
        }

        IEnumerator Title()
        {
            // The boot log and the logo coming in, the portraits breathing, the cursor and one portrait tapped.
            yield return Until(() => game.Title.Ready && !game.ModalOpen && !game.Transitions.Busy);
            yield return Wait(1.2f);
            yield return Press(Key.DownArrow);
            yield return Wait(0.4f);
            yield return Press(Key.UpArrow);
            yield return Wait(0.4f);
            Click(ui.Q(className: "sp-title").Query(className: "sp-touch__head").ToList().Last()); // DeepSeek
            yield return Wait(1.8f);

            // 读取存档: an empty list on a fresh install.
            yield return Press(Key.DownArrow);
            yield return Wait(0.3f);
            yield return Press(Key.Enter);
            yield return Until(() => game.SaveLoad.IsOpen);
            yield return Wait(1.2f);
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen);
            yield return Wait(0.3f);

            // 设置: the master volume nudged; text at 快 and AUTO after 0.75 s, the pace the story below is read
            // at; a moment on each of the other pages.
            yield return Press(Key.DownArrow);
            yield return Wait(0.3f);
            yield return Press(Key.Enter);
            yield return Until(() => game.SettingsModal.IsOpen);
            yield return Wait(1f);
            yield return Press(Key.DownArrow);
            yield return Wait(0.3f);
            yield return Presses(Key.LeftArrow, 2, 0.12f);
            yield return Presses(Key.RightArrow, 2, 0.12f);
            yield return Press(Key.UpArrow); // back up to the tabs
            yield return Press(Key.RightArrow); // 文本
            yield return Wait(0.6f);
            yield return Press(Key.DownArrow);
            yield return Wait(0.3f);
            yield return Press(Key.RightArrow); // 文字速度 标准 → 快
            yield return Wait(1f);
            yield return Press(Key.DownArrow);
            yield return Wait(0.3f);
            yield return Presses(Key.LeftArrow, 3, 0.12f); // 自动播放间隔 1.5 s → 0.75 s
            yield return Wait(0.3f);
            yield return Presses(Key.UpArrow, 2, 0.1f);
            foreach (var hold in new[] { 0.8f, 0.8f, 1f }) // 显示, 辅助, 操作
            {
                yield return Press(Key.RightArrow);
                yield return Wait(hold);
            }
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen);
            yield return Wait(0.3f);
        }

        // 陪伴生登记: the name filled in like a form filler, the gender switched and back, then 登记并开始.
        IEnumerator Register()
        {
            yield return Presses(Key.UpArrow, 2, 0.25f); // 设置 → 开始游戏
            yield return Press(Key.Enter);
            yield return Until(() => game.NewGame.IsOpen);
            yield return Wait(0.6f);
            ui.Q<TextField>(className: "sp-newgame__field").value = "鱼片";
            yield return Wait(0.5f);
            yield return Press(Key.Enter); // done typing: the selection jumps to 登记并开始
            yield return Wait(0.4f);
            yield return Press(Key.UpArrow);
            yield return Wait(0.3f);
            yield return Press(Key.RightArrow);
            yield return Wait(0.6f);
            var chip = ui.Q(className: "sp-newgame").Q(className: "is-on");
            if (chip?.Q<Label>()?.text != "女") failure ??= "登记界面按 → 没有切到「女」\n";
            yield return Press(Key.LeftArrow);
            yield return Wait(0.4f);
            yield return Press(Key.DownArrow);
            yield return Wait(0.3f);
            for (var i = 0; i < 4 && !game.Confirm.IsOpen; i++)
            {
                yield return Press(Key.Enter);
                yield return Wait(0.5f);
            }
            yield return Until(() => game.Confirm.IsOpen);
            yield return Wait(0.6f);
            yield return Press(Key.Enter);
            yield return Until(() => game.InStory && !game.Transitions.Busy);
        }

        // The prologue a line or two per kind of line, cut between.
        IEnumerator Story()
        {
            // 校门: the date and place cards, the first narration and a passer-by.
            yield return Watch("听说今年真的有AI", 0.8f, false);
            // The protagonist's thoughts.
            yield return SkipTo("我在门口停下脚步");
            yield return Watch("陪伴生，到底是干什么的", 0.8f);
            // ChatGPT comes in.
            yield return SkipTo("啊，会长早");
            yield return Watch("今天风大", 0.8f);
            // The first choice, the cursor down to 谢谢，会长。 and her answer.
            yield return SkipTo("不用紧张");
            yield return Watch("帮上忙就好", 0.8f, choice: 2);
            // A system toast; the phone, with her in the contacts; the pause menu down into the backlog and back.
            yield return SkipTo("我把联络方式存进你的手机了");
            yield return Watch("已登记至通讯录", 0.6f);
            yield return Press(Key.Tab);
            yield return Until(() => game.Phone.IsOpen);
            yield return Wait(1.6f);
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen);
            yield return Wait(0.3f);
            yield return Press(Key.Escape);
            yield return Until(() => game.Menu.IsOpen);
            yield return Wait(1.2f);
            yield return Presses(Key.UpArrow, 2, 0.35f); // 回看: up two, round past the top
            yield return Press(Key.Enter);
            yield return Until(() => game.Backlog.IsOpen);
            yield return Wait(1.4f);
            yield return Press(Key.Escape);
            yield return Until(() => !game.Backlog.IsOpen && game.Menu.IsOpen);
            yield return Until(() => !game.Menu.Switching);
            yield return Wait(0.5f);
            yield return Press(Key.Escape);
            yield return Until(() => !game.ModalOpen);
            yield return Wait(0.4f);
            // 教室: DeepSeek's thinking bubble, the thinking timer, her answer.
            yield return SkipTo("眼神空了三秒");
            yield return Watch("我在深度思考", 1f);
            // A whisper.
            yield return SkipTo("……评测？");
            yield return Watch("需要人陪", 1f);
            // 维护时刻: the phone's screen breaking up, its choice, the fall into the cache sea.
            yield return SkipTo("睡不着，盯着天花板");
            yield return Watch("潜空间·缓存海", 1.5f);
        }

        // Off camera on to the shallows. Then up behind the dozing noise at the reef's mouth at a walk, the jump,
        // the battle's first round, its results, and a few steps toward the coral.
        IEnumerator Shallows()
        {
            Roll(false);
            yield return Pump(() => game.InDungeon && Maze.Exploring, 300f);
            if (failure != null) yield break;
            Roll(true);
            yield return Wait(0.8f);

            var w = World;
            var dozing = w.Enemies.Find(e => e.Spawn.Mark == '1');
            yield return Sneak(dozing);
            if (failure != null) yield break;
            yield return Wait(0.3f);
            yield return Press(Key.Enter);
            yield return Attend(() => game.InBattle, 15f);
            yield return Battle();
            if (failure != null) yield break;

            Roll(false);
            yield return Pump(() => !game.InBattle && Maze.Exploring, 60f);
            if (failure != null) yield break;
            Roll(true);
            var coral = w.Props.Find(p => p.Kind == PropKind.Beacon && w.Def.ZoneAt(p.Cell)?.Name == "表情包珊瑚林");
            var stop = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < stop && Maze.Exploring)
            {
                var path = w.Map.Path(w.PlayerCell, coral.Cell);
                if (path.Count < 2) break;
                Steer(w.Map.World(path[1]), true);
                yield return null;
            }
            Release();
            yield return Wait(1.2f);
        }

        // As PrologueRun jumps it: let go 3 m out, braking carries the avatar to about 2.5 m, inside the 2.6 m
        // reach and outside the 1.8 m where even a dozing one notices; short of it, creep on in single-frame taps.
        IEnumerator Sneak(SeaNoise dozing)
        {
            var w = World;
            var end = Time.realtimeSinceStartup + 30f;
            while (Vector3.Distance(w.PlayerPosition, dozing.Position) > 3f && Time.realtimeSinceStartup < end)
            {
                if (!Maze.Exploring)
                {
                    Release();
                    yield return Press(Key.Enter);
                    yield return Wait(0.8f);
                    continue;
                }
                var path = w.Map.Path(w.PlayerCell, w.Map.CellAt(dozing.Position));
                Steer(path.Count > 2 ? w.Map.World(path[1]) : dozing.Position, false);
                yield return null;
            }
            Release();
            yield return Wait(0.6f);
            end = Time.realtimeSinceStartup + 4f;
            while (w.EnemyInReach(2.6f) != dozing && Vector3.Distance(w.PlayerPosition, dozing.Position) > 2.1f && Time.realtimeSinceStartup < end)
            {
                Steer(dozing.Position, false);
                yield return null;
                Release();
                yield return Wait(0.15f);
            }
            if (dozing.State != NoiseState.Doze || w.EnemyInReach(2.6f) != dozing)
                failure ??= $"没能走到发呆的噪声体背后（{dozing.State}，距离 {Vector3.Distance(w.PlayerPosition, dozing.Position):0.00} 米）\n";
        }

        // The first round at a pace to follow: the command menu's cursor down and back, a beat before each
        // choice, the hints left up a while (Enter does nothing mid-animation). From the second time the menu
        // comes up the camera is off and Enters come quick, except for the enemies' first turn (a preemptive
        // strike costs them the first one) and the results.
        IEnumerator Battle()
        {
            var end = Time.realtimeSinceStartup + 120f;
            var rounds = 0;
            var up = false; // the command menu was up last time round
            var theirs = false; // the enemies' turn has been filmed
            var last = Time.realtimeSinceStartup;
            while (game.InBattle && !Exists("sp-results"))
            {
                if (Time.realtimeSinceStartup > end)
                {
                    failure ??= "战斗超时（120 秒）\n";
                    yield break;
                }
                if (!theirs && ui.Query<Label>(className: "sp-turnbanner__text").ToList().Any(l => l.text == "敌人的回合"))
                {
                    theirs = true;
                    Roll(true);
                }
                var menu = Visible(ui.Q(className: "sp-battle__menu"));
                if (menu && !up && ++rounds >= 2) Roll(false);
                up = menu;
                if (menu || ui.Query(className: "sp-enemy__cursor").ToList().Any(Visible))
                {
                    if (rolling) yield return Wait(0.8f);
                    if (menu && rounds == 1 && rolling)
                    {
                        yield return Press(Key.DownArrow);
                        yield return Wait(0.4f);
                        yield return Press(Key.UpArrow);
                        yield return Wait(0.4f);
                    }
                    // 防御 in the second round, or a second screenshot can end it before the enemies ever move.
                    if (menu && rounds == 2 && !theirs) yield return Press(Key.DownArrow);
                    yield return Press(Key.Enter);
                    last = Time.realtimeSinceStartup;
                    yield return Wait(0.3f);
                }
                else if (Time.realtimeSinceStartup - last > (rolling ? 2f : 0.15f))
                {
                    yield return Press(Key.Enter);
                    last = Time.realtimeSinceStartup;
                }
                else yield return null;
            }
            Roll(true);
            yield return Wait(2.2f);
            yield return Press(Key.Enter);
            yield return Wait(0.5f);
        }

        // ------------------------------------------------------------------ story

        // On camera into the next line (or straight on with `advance` false), AUTO on, until a line with `line`
        // in it has been read through, then `hold` more. AUTO answers neither a choice (the cursor goes down
        // `choice` rows first) nor the thinking timer, so those get Enter after a beat.
        IEnumerator Watch(string line, float hold, bool advance = true, int choice = 0)
        {
            Roll(true);
            if (advance) yield return Press(Key.Enter);
            yield return Auto();
            var options = ui.Q(className: "sp-options");
            var answered = -1; // the backlog length when the thinking timer was last answered
            var end = Time.realtimeSinceStartup + 60f;
            while (failure == null && !Read(line))
            {
                if (Time.realtimeSinceStartup > end)
                {
                    failure ??= $"剧情等不到「{line}」\n";
                    yield break;
                }
                if (options.childCount > 0)
                {
                    yield return Wait(0.9f);
                    for (var i = 0; i < choice; i++)
                    {
                        yield return Press(Key.DownArrow);
                        yield return Wait(0.5f);
                    }
                    yield return Press(Key.Enter);
                    yield return Until(() => options.childCount == 0, 5f);
                    yield return Auto();
                }
                else if (Read("思考中") && answered != game.Story.Backlog.Count)
                {
                    answered = game.Story.Backlog.Count;
                    yield return Wait(1f);
                    yield return Press(Key.Enter); // also turns AUTO off
                    yield return Auto();
                }
                else yield return null;
            }
            yield return Wait(hold);
        }

        // Off camera, Enter on until a line with `line` in it has been read through; every choice its first option.
        IEnumerator SkipTo(string line)
        {
            Roll(false);
            yield return Pump(() => Read(line), 180f);
        }

        // Q until 自动, the first quick key, is lit: a press during a wipe or a card is not heard, nor while a
        // choice is up (Watch turns it on again after).
        IEnumerator Auto()
        {
            var key = ui.Q(className: "sp-quick");
            var options = ui.Q(className: "sp-options");
            for (var i = 0; i < 12 && !key.ClassListContains("is-on"); i++)
            {
                if (options.childCount > 0) yield break;
                yield return Press(Key.Q);
                yield return Wait(0.25f);
            }
            if (!key.ClassListContains("is-on")) failure ??= "剧情里按 Q 没有打开自动播放\n";
        }

        bool Read(string line)
        {
            var log = game.Story.Backlog;
            return log.Count > 0 && log[log.Count - 1].Text.Contains(line);
        }

        // ------------------------------------------------------------------ filming

        IEnumerator Film()
        {
            var endOfFrame = new WaitForEndOfFrame();
            var slot = -1;
            filming = true;
            Roll(true);
            while (filming)
            {
                yield return endOfFrame;
                if (!rolling) continue;
                var at = (float)clock.Elapsed.TotalSeconds;
                // One frame per 1/30 s slot; a slow frame leaves a gap that the frame list fills by holding.
                if ((int)(at * Fps) <= slot) continue;
                slot = (int)(at * Fps);
                if (screen == null || screen.width != Screen.width || screen.height != Screen.height)
                {
                    if (screen != null) screen.Release();
                    screen = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);
                var index = frames.Count;
                frames.Add(at);
                Interlocked.Increment(ref pending);
                AsyncGPUReadback.Request(screen, 0, TextureFormat.RGBA32, r => Save(r, index));
            }
        }

        // Picture and sound start and stop together. The music pauses over a cut too, so it carries on where it left
        // off instead of jumping (a track change during the cut still starts the new one).
        void Roll(bool on)
        {
            if (on == rolling) return;
            var music = game.Audio.GetComponents<AudioSource>().Where(s => s.loop);
            if (on)
            {
                foreach (var s in music) s.UnPause();
                clock.Start();
                tap.Begin(clock);
            }
            else
            {
                clock.Stop();
                tap.Stop();
                foreach (var s in music) s.Pause();
            }
            rolling = on;
        }

        void Save(AsyncGPUReadbackRequest request, int index)
        {
            if (request.hasError)
            {
                failure ??= $"第 {index} 帧回读失败\n";
                Interlocked.Decrement(ref pending);
                return;
            }
            var pixels = request.GetData<byte>().ToArray();
            int width = request.width, height = request.height;
            // Where texture rows start at the top (Metal), the captured screen comes back upside down.
            var flip = SystemInfo.graphicsUVStartsAtTop;
            var path = Path.Combine(outDir, "frames", $"{index:00000}.jpg");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (flip)
                {
                    var row = new byte[width * 4];
                    for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
                    {
                        Buffer.BlockCopy(pixels, top * row.Length, row, 0, row.Length);
                        Buffer.BlockCopy(pixels, bottom * row.Length, pixels, top * row.Length, row.Length);
                        Buffer.BlockCopy(row, 0, pixels, bottom * row.Length, row.Length);
                    }
                }
                File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height, 0, 92));
                Interlocked.Decrement(ref pending);
            });
        }

        // An ffconcat list: each frame held until the next one was taken. The sound starts at film time 0, and
        // the first frame comes back a while later when the game is still loading, so it is held from 0 too;
        // starting the picture at the first frame instead put it that much ahead of the sound.
        void WriteFrameList()
        {
            var list = new StringBuilder("ffconcat version 1.0\n");
            for (var i = 0; i < frames.Count; i++)
            {
                var from = i == 0 ? 0f : frames[i];
                var to = i + 1 < frames.Count ? frames[i + 1] : frames[i] + 1f / Fps;
                list.Append($"file 'frames/{i:00000}.jpg'\nduration {to - from:0.00000}\n");
            }
            if (frames.Count > 0) list.Append($"file 'frames/{frames.Count - 1:00000}.jpg'\n");
            File.WriteAllText(Path.Combine(outDir, "frames.txt"), list.ToString());
        }

        // ------------------------------------------------------------------ input

        IEnumerator Press(Key key)
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        IEnumerator Presses(Key key, int count, float gap)
        {
            for (var i = 0; i < count; i++)
            {
                yield return Press(key);
                yield return Wait(gap);
            }
        }

        // Holds the movement keys toward a point, relative to where the camera looks, as PrologueRun steers.
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

        static IEnumerator Wait(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator Until(Func<bool> condition, float timeout = 30f, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                {
                    failure ??= $"等待界面超时（GameTour.cs 第 {line} 行）\n";
                    yield break;
                }
                yield return null;
            }
        }

        // Waits, pressing Enter at a reading pace in case the game waits on it.
        IEnumerator Attend(Func<bool> condition, float timeout, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            var end = Time.realtimeSinceStartup + timeout;
            var next = Time.realtimeSinceStartup + 1.6f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                {
                    failure ??= $"等待超时（GameTour.cs 第 {line} 行）\n";
                    yield break;
                }
                if (Time.realtimeSinceStartup > next)
                {
                    yield return Press(Key.Enter);
                    next = Time.realtimeSinceStartup + 1.6f;
                }
                else yield return null;
            }
        }

        // Off camera: Enter as fast as PrologueRun reads, every choice its first option.
        IEnumerator Pump(Func<bool> condition, float timeout, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                {
                    failure ??= $"快进剧情超时（GameTour.cs 第 {line} 行）\n";
                    yield break;
                }
                yield return Press(Key.Enter);
                yield return Wait(0.06f);
            }
        }

        static void Click(VisualElement target)
        {
            using (var e = ClickEvent.GetPooled())
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        bool Exists(string className) => ui.Q(className: className) != null;

        static bool Visible(VisualElement e)
        {
            if (e == null || e.panel == null) return false;
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure ??= $"日志报错：{message}\n{stack}";
        }

        // The listener's mixed output, kept while the camera rolls. Runs on the audio thread. Time the mixer
        // falls behind the clock is never mixed, so counting samples alone ran the sound ahead of the picture,
        // 0.2 s by the end of a 4-minute tour; a block that comes late is put back on the film clock behind that
        // much silence.
        sealed class SoundTap : MonoBehaviour
        {
            const double Slack = 0.04; // a block or two of mixing jitter, left alone

            readonly List<float> samples = new List<float>();
            readonly List<string> gaps = new List<string>();
            System.Diagnostics.Stopwatch clock;
            int rate;
            int channels = 2;
            volatile bool on;

            public void Begin(System.Diagnostics.Stopwatch filmClock)
            {
                clock = filmClock;
                rate = AudioSettings.outputSampleRate;
                on = true;
            }

            public void Stop() => on = false;

            public float Seconds
            {
                get { lock (samples) return samples.Count / (float)channels / rate; }
            }

            // The silences put in, "at <film s>: <s>", for tour.txt.
            public string Gaps
            {
                get { lock (samples) return gaps.Count == 0 ? "no gaps" : $"{gaps.Count} gaps: {string.Join(", ", gaps)}"; }
            }

            void OnAudioFilterRead(float[] data, int count)
            {
                if (!on) return;
                lock (samples)
                {
                    channels = count;
                    var now = clock.Elapsed.TotalSeconds;
                    var behind = (long)(now * rate) - samples.Count / count;
                    if (behind > Slack * rate)
                    {
                        for (var i = behind * count; i > 0; i--) samples.Add(0f);
                        gaps.Add($"{now:0.00} s: {behind / (double)rate:0.000} s");
                    }
                    samples.AddRange(data);
                }
            }

            public void WriteWav(string path)
            {
                lock (samples)
                {
                    using var w = new BinaryWriter(File.Create(path));
                    w.Write(Encoding.ASCII.GetBytes("RIFF"));
                    w.Write(36 + samples.Count * 2);
                    w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    w.Write(16);
                    w.Write((short)1);
                    w.Write((short)channels);
                    w.Write(rate);
                    w.Write(rate * channels * 2);
                    w.Write((short)(channels * 2));
                    w.Write((short)16);
                    w.Write(Encoding.ASCII.GetBytes("data"));
                    w.Write(samples.Count * 2);
                    foreach (var s in samples) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * short.MaxValue));
                }
            }
        }
    }
}
#endif
