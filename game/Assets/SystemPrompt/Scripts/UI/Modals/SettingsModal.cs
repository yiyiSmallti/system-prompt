using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // 设置, dressed as the game's own config file open in a terminal editor: a file tab per page, a
    // line-number gutter, `键 = 值` rows with block-bar sliders, [ON OFF] switches and ◀ 值 ▶ choices, the
    // focused row's help as a // comment and a status bar. It powers on like a CRT and each page's lines
    // type themselves in.
    // Changes apply immediately and are written to settings.json when the menu closes.
    public sealed class SettingsModal : Modal
    {
        const string PreviewLine = "……鱼片，这是文字速度的预览。看清楚了吗？完毕。";
        const int SliderCells = 20;
        const float PowerOn = 0.22f;
        const float ExitSeconds = 0.24f;
        const float LineStagger = 0.028f;
        const float HelpCharsPerSecond = 160f;

        abstract class Row
        {
            public VisualElement Root;
            public string Help;
            public abstract void Step(int direction);
            public virtual void Activate() => Step(1);
            public abstract void Refresh();
        }

        sealed class SliderRow : Row
        {
            public Func<float> Get;
            public Action<float> Set;
            public float Min, Max, Increment;
            public Func<float, string> Format;
            public readonly List<VisualElement> Cells = new List<VisualElement>();
            public Label Value;

            public override void Step(int direction) => Apply(Get() + direction * Increment);

            public override void Activate() { }

            public void Apply(float v)
            {
                v = Mathf.Clamp(Mathf.Round(v / Increment) * Increment, Min, Max);
                Set(v);
                Refresh();
            }

            public override void Refresh()
            {
                var v = Get();
                var filled = Mathf.RoundToInt(Mathf.InverseLerp(Min, Max, v) * Cells.Count);
                for (var i = 0; i < Cells.Count; i++) Cells[i].EnableInClassList("is-on", i < filled);
                Value.text = Format(v);
            }
        }

        sealed class ChoiceRow : Row
        {
            public Func<int> Get;
            public Action<int> Set;
            public Func<string[]> Options;
            public Label Value;

            public override void Step(int direction)
            {
                var n = Options().Length;
                if (n == 0) return;
                Set(((Get() + direction) % n + n) % n);
                Refresh();
            }

            public override void Refresh()
            {
                var options = Options();
                var i = Get();
                Value.text = i >= 0 && i < options.Length ? options[i] : "—";
            }
        }

        sealed class ToggleRow : Row
        {
            public Func<bool> Get;
            public Action<bool> Set;
            public Label On, Off;

            public override void Step(int direction)
            {
                Set(!Get());
                Refresh();
            }

            public override void Refresh()
            {
                var on = Get();
                On.EnableInClassList("is-on", on);
                Off.EnableInClassList("is-on", !on);
            }
        }

        sealed class Page
        {
            public string Name;
            public string File;
            public string Help;
            public VisualElement Root;
            public readonly List<Row> Rows = new List<Row>();
            public readonly List<VisualElement> Lines = new List<VisualElement>(); // in typing order
            public readonly MenuList Menu = new MenuList();
        }

        readonly List<Page> pages = new List<Page>();
        readonly MenuList tabs = new MenuList { Horizontal = true };
        readonly VisualElement crt;
        readonly VisualElement window;
        readonly VisualElement glare;
        readonly VisualElement beam;
        readonly VisualElement tabBar;
        readonly VisualElement pageHost;
        readonly Label previewText;
        readonly Label help;
        readonly Label statusMode;
        readonly Label statusFile;
        readonly Label statusLine;
        List<Vector2Int> resolutions = new List<Vector2Int>();
        Page current;
        bool onTabs;
        float previewChars;
        float previewHold;
        string helpText = "";
        float helpChars;
        float blink;
        int typing;

        GameSettings S => Game.Settings;

        protected override bool OwnEntrance => true;
        protected override float OwnExit => ExitSeconds;

        public SettingsModal(GameRoot game) : base(game, "sp-term", decorated: false)
        {
            crt = Ui.El("sp-term__crt", Root);
            Ui.Fill(crt);
            Ui.Text("CONFIG", "sp-term__watermark", crt, FontKind.MonoBold);
            window = Ui.El("sp-term__window", crt);
            Shape.Backdrop(window, Shape.Box(), new Color(0.05f, 0.063f, 0.08f)).Stroke(Palette.Green.WithAlpha(0.6f), 2).Shadow(new Vector2(16, 16), Palette.Green.WithAlpha(0.22f));

            var titleBar = Ui.El("sp-term__titlebar", window);
            foreach (var dot in new[] { "is-red", "is-amber", "is-green" }) Ui.El("sp-term__dot " + dot, titleBar);
            Ui.Text("system_prompt.cfg", "sp-term__filename", titleBar, FontKind.MonoBold);
            Ui.Text("设置 · CONFIG", "sp-term__titletag", titleBar, FontKind.Bold);
            tabBar = Ui.El("sp-term__tabs", window);
            pageHost = Ui.El("sp-term__body", window);
            help = Ui.Text("", "sp-term__help", window, FontKind.Mono);
            var status = Ui.El("sp-term__status", window);
            statusMode = Ui.Text("", "sp-term__mode", status, FontKind.MonoBold);
            statusFile = Ui.Text("", "sp-term__status-file", status, FontKind.MonoBold);
            statusLine = Ui.Text("", "sp-term__status-line", status, FontKind.MonoBold);
            Ui.Text("←→ 调整　↑↓ 选择　Esc 保存并返回", "sp-term__status-hints", status, FontKind.MonoBold);

            var lines = new Scanlines(Palette.Ink.WithAlpha(0.35f), 4f);
            Ui.Fill(lines);
            crt.Add(lines);
            glare = Ui.El("sp-term__glare", crt);
            Ui.Fill(glare);
            glare.pickingMode = PickingMode.Ignore;
            beam = Ui.El("sp-term__beam", Root);
            beam.pickingMode = PickingMode.Ignore;

            var audio = AddPage("音频", "audio.cfg", "音乐和音效的音量。");
            Slider(audio, "主音量", "所有声音的总音量。", () => S.masterVolume, v => { S.masterVolume = v; Game.Audio.ApplyVolumes(); }, 0f, 1f, 0.05f, Percent);
            Slider(audio, "音乐", "背景音乐的音量。", () => S.musicVolume, v => { S.musicVolume = v; Game.Audio.ApplyVolumes(); }, 0f, 1f, 0.05f, Percent);
            Slider(audio, "音效", "按键、演出和战斗音效的音量。", () => S.sfxVolume, v => { S.sfxVolume = v; Game.Audio.ApplyVolumes(); }, 0f, 1f, 0.05f, Percent);

            var text = AddPage("文本", "text.cfg", "台词的显示速度、自动播放和对话框。");
            Choice(text, "文字速度", "台词逐字显示的速度。「瞬间」直接显示整句。", () => S.textSpeed, v => { S.textSpeed = v; previewChars = 0; }, () => GameSettings.TextSpeedLabels);
            Slider(text, "自动播放间隔", "AUTO 模式下，一句显示完后等多久进入下一句。", () => S.autoDelay, v => S.autoDelay = v, 0.5f, 4f, 0.25f, v => $"{v:0.00}s");
            Toggle(text, "跳过未读文字", "OFF 时，快进会停在还没读过的台词前。", () => S.skipUnread, v => S.skipUnread = v);
            Slider(text, "对话框不透明度", "对话框底色的不透明度，调低能看到更多背景。", () => S.boxOpacity, v => S.boxOpacity = v, 0.5f, 1f, 0.05f, Percent);
            Choice(text, "DeepSeek 思考速度", "DeepSeek 思考框里文字的显示速度。", () => S.thinkingSpeed, v => S.thinkingSpeed = v, () => GameSettings.ThinkingSpeedLabels);
            Toggle(text, "大字号", "放大对话框里的台词。", () => S.largeText, v => S.largeText = v);
            Comment(text, "// preview: 当前文字速度");
            var previewLine = Line(text, "sp-term__line is-preview");
            var preview = Ui.El("sp-term__preview", previewLine);
            Shape.Backdrop(preview, Shape.Crooked(10), Palette.Ink).Stroke(Palette.Paper, 3).Shadow(new Vector2(8, 8), Palette.Green);
            var previewName = Ui.El("sp-term__preview-name", preview);
            Shape.Backdrop(previewName, Shape.Slant(10), Palette.Paper);
            Ui.Text("DeepSeek", "sp-term__preview-name-text", previewName, FontKind.Black);
            previewText = Ui.Text("", "sp-term__preview-text", preview);

            var display = AddPage("显示", "display.cfg", "窗口、分辨率和帧率。");
            Toggle(display, "全屏", "全屏或窗口显示。", () => S.fullscreen, v => { S.fullscreen = v; S.ApplyDisplay(); });
            Choice(display, "分辨率", "画面的分辨率，界面按比例缩放。", ResolutionIndex, SetResolution, ResolutionLabels);
            Toggle(display, "垂直同步", "和显示器刷新同步，避免画面撕裂。", () => S.vsync, v => { S.vsync = v; S.ApplyDisplay(); });
            Choice(display, "帧率上限", "关闭垂直同步时的最高帧率。", () => Mathf.Max(0, Array.IndexOf(GameSettings.FrameCaps, S.frameCap)),
                v => { S.frameCap = GameSettings.FrameCaps[v]; S.ApplyDisplay(); },
                () => Array.ConvertAll(GameSettings.FrameCaps, f => f == 0 ? "不限" : f.ToString()));

            var access = AddPage("辅助", "access.cfg", "闪光、震动和背景动态。");
            Toggle(access, "减弱闪光", "降低命中、转场时闪白的亮度。", () => S.reduceFlash, v => S.reduceFlash = v);
            Toggle(access, "画面震动", "攻击命中时画面震动。", () => S.screenShake, v => S.screenShake = v);
            Toggle(access, "背景动态", "背景里飘动的花瓣、光点、气泡，以及背景的缓慢漂移。", () => S.backgroundMotion, v => S.backgroundMotion = v);

            var controls = AddPage("操作", "keys.md", "键盘、鼠标和手柄的按键一览。");
            foreach (var (action, keys) in new[]
            {
                ("推进对话 / 确定", "Enter · Space · 左键 · 滚轮下 · Ⓐ"),
                ("返回 / 菜单", "Esc · 右键 · Ⓑ · Start"),
                ("自动播放", "Q · Ⓧ"),
                ("快进（切换 / 按住）", "F / Ctrl · RB"),
                ("回看", "L · 滚轮上 · LB"),
                ("手机", "Tab · Ⓨ"),
                ("隐藏界面", "H"),
                ("快速存档 / 读档", "F5 / F9"),
                ("删除存档", "Delete · View"),
            })
            {
                var r = Line(controls, "sp-term__line is-key");
                Ui.Text("bind", "sp-term__bind", r, FontKind.MonoBold);
                Ui.Text(action, "sp-term__key", r, FontKind.Bold);
                Ui.Text(keys, "sp-term__keys", r, FontKind.Mono);
            }

            foreach (var page in pages)
            {
                foreach (var row in page.Rows) page.Menu.Add(row.Root, row.Activate);
                var reset = Line(page, "sp-term__line sp-term__row is-command");
                Ui.Text("$", "sp-term__caret", reset, FontKind.MonoBold);
                Ui.Text("reset --all", "sp-term__command", reset, FontKind.MonoBold);
                Ui.Text("# 恢复默认", "sp-term__aside", reset, FontKind.Mono);
                Ui.El("sp-term__cursor", reset);
                page.Menu.Add(reset, AskReset);
                page.Menu.SelectionChanged += _ =>
                {
                    onTabs = false;
                    ShowHelp();
                };
                var tab = Ui.El("sp-term__tab", tabBar);
                Ui.Text(page.File, "sp-term__tab-file", tab, FontKind.MonoBold);
                Ui.Text(page.Name, "sp-term__tab-name", tab, FontKind.Bold);
                tabs.Add(tab, FocusRows);
            }
            tabs.SelectionChanged += i =>
            {
                ShowPage(pages[i]);
                FocusTabs();
            };

            Root.schedule.Execute(Tick).Every(33);
        }

        static string Percent(float v) => $"{Mathf.RoundToInt(v * 100)}%";

        Page AddPage(string name, string file, string pageHelp)
        {
            var page = new Page { Name = name, File = file, Help = pageHelp, Root = Ui.El("sp-term__page", pageHost) };
            pages.Add(page);
            Comment(page, $"# {name} — {pageHelp}");
            return page;
        }

        // A numbered line of the file; content goes after the gutter.
        VisualElement Line(Page page, string classes)
        {
            var line = Ui.El(classes, page.Root);
            Ui.Text($"{page.Lines.Count + 1,3}", "sp-term__gutter", line, FontKind.Mono);
            page.Lines.Add(line);
            return line;
        }

        void Comment(Page page, string text)
        {
            var line = Line(page, "sp-term__line is-comment");
            Ui.Text(text, "sp-term__comment", line, FontKind.Mono);
        }

        VisualElement RowRoot(Page page, string label, string rowHelp, Row row)
        {
            var root = Line(page, "sp-term__line sp-term__row");
            Ui.Text("▶", "sp-term__caret", root, FontKind.MonoBold);
            Ui.Text(label, "sp-term__key", root, FontKind.Bold);
            Ui.Text("=", "sp-term__eq", root, FontKind.Mono);
            row.Root = root;
            row.Help = rowHelp;
            page.Rows.Add(row);
            return root;
        }

        void Slider(Page page, string label, string rowHelp, Func<float> get, Action<float> set, float min, float max, float increment, Func<float, string> format)
        {
            var row = new SliderRow { Get = get, Set = set, Min = min, Max = max, Increment = increment, Format = format };
            var root = RowRoot(page, label, rowHelp, row);
            var track = Ui.El("sp-term__track", root);
            for (var i = 0; i < SliderCells; i++) row.Cells.Add(Ui.El("sp-term__cell", track));
            row.Value = Ui.Text("", "sp-term__number", root, FontKind.MonoBold);
            Ui.El("sp-term__cursor", root);

            // Click or drag on the bar to set the value directly.
            void SetFromPointer(Vector2 local)
            {
                var w = track.resolvedStyle.width;
                if (w <= 0) return;
                row.Apply(Mathf.Lerp(min, max, Mathf.Clamp01(local.x / w)));
            }
            track.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            track.RegisterCallback<PointerDownEvent>(e =>
            {
                track.CapturePointer(e.pointerId);
                SetFromPointer(e.localPosition);
                e.StopPropagation();
            });
            track.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (track.HasPointerCapture(e.pointerId)) SetFromPointer(e.localPosition);
            });
            track.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!track.HasPointerCapture(e.pointerId)) return;
                track.ReleasePointer(e.pointerId);
                Game.Audio.Sfx("cursor", 0.6f);
            });
        }

        void Choice(Page page, string label, string rowHelp, Func<int> get, Action<int> set, Func<string[]> options)
        {
            var row = new ChoiceRow { Get = get, Set = set, Options = options };
            var root = RowRoot(page, label, rowHelp, row);
            Ui.Text("◀", "sp-term__arrow", root, FontKind.MonoBold);
            row.Value = Ui.Text("", "sp-term__choice", root, FontKind.Bold);
            Ui.Text("▶", "sp-term__arrow", root, FontKind.MonoBold);
            Ui.El("sp-term__cursor", root);
        }

        void Toggle(Page page, string label, string rowHelp, Func<bool> get, Action<bool> set)
        {
            var row = new ToggleRow { Get = get, Set = set };
            var root = RowRoot(page, label, rowHelp, row);
            Ui.Text("[", "sp-term__bracket", root, FontKind.Mono);
            row.On = Ui.Text("ON", "sp-term__switch", root, FontKind.MonoBold);
            row.Off = Ui.Text("OFF", "sp-term__switch", root, FontKind.MonoBold);
            Ui.Text("]", "sp-term__bracket", root, FontKind.Mono);
            Ui.El("sp-term__cursor", root);
        }

        int ResolutionIndex()
        {
            var w = S.resolutionWidth > 0 ? S.resolutionWidth : Screen.width;
            var h = S.resolutionHeight > 0 ? S.resolutionHeight : Screen.height;
            return resolutions.IndexOf(new Vector2Int(w, h));
        }

        void SetResolution(int i)
        {
            S.resolutionWidth = resolutions[i].x;
            S.resolutionHeight = resolutions[i].y;
            S.ApplyDisplay();
        }

        string[] ResolutionLabels() => resolutions.ConvertAll(r => $"{r.x} × {r.y}").ToArray();

        void ShowPage(Page page)
        {
            var changed = current != page;
            current = page;
            foreach (var p in pages) Ui.Show(p.Root, p == page);
            foreach (var row in page.Rows) row.Refresh();
            previewChars = 0;
            statusFile.text = page.File;
            if (changed && IsOpen) TypeIn(page, 0f).Forget();
        }

        void FocusTabs()
        {
            onTabs = true;
            current.Menu.Deselect();
            tabBar.AddToClassList("is-focus");
            ShowHelp();
        }

        void FocusRows()
        {
            current.Menu.Select(0);
            onTabs = false;
            ShowHelp();
        }

        // The comment line explains whatever is focused: the file, a setting, or the reset command.
        void ShowHelp()
        {
            tabBar.EnableInClassList("is-focus", onTabs);
            var i = current.Menu.Index;
            var text = onTabs || i < 0 ? $"{current.Name}：{current.Help}"
                : i < current.Rows.Count ? current.Rows[i].Help
                : "所有设置恢复为默认值（会先确认）。";
            statusMode.text = onTabs ? "-- FILES --" : "-- EDIT --";
            var line = !onTabs && i >= 0 ? current.Lines.IndexOf(current.Menu[i]) + 1 : 0;
            statusLine.text = line > 0 ? $"LN {line:00}" : "LN --";
            text = "// " + text;
            if (text == helpText) return;
            helpText = text;
            helpChars = 0;
            help.text = "";
        }

        void AskReset()
        {
            Game.Confirm.Ask("所有设置恢复为默认值？", () =>
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new GameSettings()), S);
                Game.Audio.ApplyVolumes();
                S.ApplyDisplay();
                foreach (var p in pages)
                foreach (var r in p.Rows) r.Refresh();
            });
        }

        protected override void OnOpen()
        {
            resolutions = GameSettings.AvailableResolutions();
            current = null;
            tabs.Select(0, false);
            ShowPage(pages[0]);
            FocusTabs();
            Enter().Forget();
            TypeIn(current, PowerOn).Forget();
        }

        protected override void OnClose()
        {
            ++typing;
            Leave().Forget();
            try { S.Save(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Game.Notice("设置无法保存");
            }
        }

        // ------------------------------------------------------------------ motion

        // CRT power-on: a bright line opens into the picture, which flares and settles.
        async Awaitable Enter()
        {
            Game.Audio.Sfx("glitch", 0.35f);
            await PlayMotion(PowerOn + 0.12f, time =>
            {
                var line = Ease.OutCubic(Mathf.Clamp01(time / 0.08f));
                beam.style.scale = new Scale(new Vector2(line, 1f));
                beam.style.opacity = 1 - Mathf.Clamp01((time - 0.1f) / 0.08f);
                var open = Ease.OutCubic(Mathf.Clamp01((time - 0.08f) / (PowerOn - 0.08f)));
                crt.style.opacity = time < 0.08f ? 0f : 1f;
                crt.style.scale = new Scale(new Vector2(1f, Mathf.Lerp(0.008f, 1f, open)));
                glare.style.opacity = time < 0.08f ? 0f : 0.85f * (1 - Mathf.Clamp01((time - 0.08f) / 0.26f));
            });
        }

        // CRT power-off: the picture squashes to a line, the line to a dot.
        async Awaitable Leave()
        {
            await PlayMotion(ExitSeconds, time =>
            {
                var squash = Ease.InCubic(Mathf.Clamp01(time / 0.12f));
                crt.style.scale = new Scale(new Vector2(1f, Mathf.Lerp(1f, 0.008f, squash)));
                glare.style.opacity = squash * 0.9f;
                crt.style.opacity = time < 0.12f ? 1f : 0f;
                beam.style.opacity = time < 0.1f ? 0f : 1f;
                beam.style.scale = new Scale(new Vector2(1 - Ease.InCubic(Mathf.Clamp01((time - 0.12f) / 0.1f)), 1f));
            });
        }

        // The page's lines appear one after another, each with a small kick, like output scrolling in.
        async Awaitable TypeIn(Page page, float delay)
        {
            var id = ++typing;
            var lines = page.Lines;
            var total = delay + lines.Count * LineStagger + 0.06f;
            await Anim.Tween(total, t =>
            {
                if (id != typing) return;
                var time = t * total - delay;
                for (var i = 0; i < lines.Count; i++)
                {
                    var since = time - i * LineStagger;
                    lines[i].style.opacity = since >= 0f ? 1f : 0f;
                    lines[i].style.translate = new Translate(since >= 0f && since < 0.05f ? 14f : 0f, 0);
                }
            }, Ease.Linear, () => id != typing);
        }

        // The preview line, the help comment typing out and the cursor blink.
        void Tick()
        {
            if (!IsOpen || current == null) return;
            blink += 0.033f;
            window.EnableInClassList("is-blink", blink % 1f > 0.55f);
            if (helpChars < helpText.Length)
            {
                helpChars = Mathf.Min(helpText.Length, helpChars + HelpCharsPerSecond * 0.033f);
                help.text = helpText.Substring(0, (int)helpChars);
            }
            if (current.Name != "文本") return;
            var cps = S.CharsPerSecond;
            previewText.style.fontSize = 30f * S.TextScale;
            if (previewChars >= PreviewLine.Length)
            {
                previewHold += 0.033f;
                if (previewHold > S.autoDelay) { previewChars = 0; previewHold = 0; }
            }
            else previewChars = float.IsInfinity(cps) ? PreviewLine.Length : previewChars + cps * 0.033f;
            previewText.text = PreviewLine.Substring(0, Mathf.Min(PreviewLine.Length, (int)previewChars));
        }

        public override void HandleInput()
        {
            if (GameInput.Consume(Btn.Cancel) || GameInput.Consume(Btn.Menu))
            {
                Game.Audio.Sfx("cursor");
                Close();
                return;
            }
            if (onTabs)
            {
                if (GameInput.Consume(Btn.Down))
                {
                    FocusRows();
                    return;
                }
                tabs.HandleInput();
                return;
            }
            var menu = current.Menu;
            if (menu.Index == 0 && GameInput.Consume(Btn.Up))
            {
                FocusTabs();
                Game.Audio.Sfx("cursor");
                return;
            }
            var selected = menu.Index >= 0 && menu.Index < current.Rows.Count ? current.Rows[menu.Index] : null;
            if (selected != null)
            {
                if (GameInput.Consume(Btn.Left)) { selected.Step(-1); Game.Audio.Sfx("cursor", 0.6f); return; }
                if (GameInput.Consume(Btn.Right)) { selected.Step(1); Game.Audio.Sfx("cursor", 0.6f); return; }
            }
            menu.HandleInput();
        }
    }
}
