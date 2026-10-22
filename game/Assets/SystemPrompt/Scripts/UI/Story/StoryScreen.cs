using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SystemPrompt.Story;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    public enum LineKind { Speech, Player, Inner, Narration, System, Screen, File, Thinking, Shadow }

    public sealed class LineInfo
    {
        public string Id;
        public string Speaker;
        public string Text;
        public LineKind Kind;
        public string Face;
        public bool Whisper;
    }

    public sealed class BacklogEntry
    {
        public string Speaker;
        public string Text;
        public LineKind Kind;
        public bool Choice;
    }

    // The daytime/dialogue screen: background, portraits, HUD and every kind of line in the Yarn script.
    public sealed partial class StoryScreen : IInputLayer, ILinePresenter
    {
        static readonly Regex ThinkTimer = new Regex(@"^思考中……（已思考 (.+) 秒）$");
        const string WarningPhrase = "终端回收，模型下线";

        public readonly VisualElement Root;
        public readonly PortraitStage Stage;
        public readonly List<BacklogEntry> Backlog = new List<BacklogEntry>();

        readonly GameRoot game;
        readonly Image bgBack;
        readonly Image bgFront;
        readonly AmbientLayer ambientBack;
        readonly AmbientLayer ambientFront;
        readonly VisualElement ui;
        readonly VisualElement fx;

        // dialogue box
        readonly VisualElement box;
        readonly Shape boxShape;
        readonly VisualElement scanlines;
        readonly VisualElement namePlate;
        readonly Shape namePlateBar;
        readonly Label nameLabel;
        readonly Label roleLabel;
        readonly Label textLabel;
        readonly VisualElement nextMark;

        // other line presentations
        readonly VisualElement toast;
        readonly Label toastText;
        readonly VisualElement toastCursor;
        readonly VisualElement think;
        readonly Label thinkHeader;
        readonly Label thinkText;
        readonly VisualElement phone;
        readonly VisualElement phoneLines;
        readonly VisualElement file;
        readonly VisualElement fileLines;
        readonly VisualElement options;
        readonly MenuList optionMenu = new MenuList();

        // HUD
        readonly VisualElement hud;
        readonly Label locDate;
        readonly Label locPlace;
        readonly VisualElement deadline;
        readonly Label deadlineDays;
        readonly Label deadlineName;
        readonly VisualElement quickBar;
        readonly VisualElement autoButton;
        readonly VisualElement skipButton;
        readonly VisualElement phoneButton;
        readonly VisualElement cover;
        bool covered;
        int coverMotion;

        // Bumped by Reset, so presentations still running from an abandoned story stop touching the screen.
        int epoch;
        bool advanceRequested;
        bool optionsActive;
        bool hidden;
        bool autoMode;
        bool skipMode;
        LineKind? boxKind;
        string lastSpeaker;

        public StoryScreen(GameRoot game, VisualElement parent)
        {
            this.game = game;
            Root = Ui.El("sp-story", parent);
            Ui.Fill(Root);

            var bgHost = Ui.El("sp-bg-host", Root);
            Ui.Fill(bgHost);
            ParticleField.Drift(bgHost);
            bgBack = Ui.Picture(null, ScaleMode.ScaleAndCrop, "sp-bg", bgHost);
            Ui.Fill(bgBack);
            bgFront = Ui.Picture(null, ScaleMode.ScaleAndCrop, "sp-bg", bgHost);
            Ui.Fill(bgFront);
            ambientBack = new AmbientLayer(Root, false);

            Stage = new PortraitStage(game, Root);
            ambientFront = new AmbientLayer(Root, true);

            // The cover fades the whole story UI while a menu or page is up (SetCovered); ui itself is HIDE's.
            cover = Ui.El("sp-story__cover", Root);
            Ui.Fill(cover);
            cover.pickingMode = PickingMode.Ignore;
            ui = Ui.El("sp-story__ui", cover);
            Ui.Fill(ui);
            ui.pickingMode = PickingMode.Ignore;

            // ---- HUD
            hud = Ui.El("sp-hud", ui);
            Ui.Fill(hud);
            hud.pickingMode = PickingMode.Ignore;
            var loc = Ui.El("sp-loc", hud);
            Shape.Backdrop(loc, Shape.Slant(18), Palette.Ink.WithAlpha(0.9f));
            Ui.El("sp-loc__bar", loc);
            var locTexts = Ui.El("sp-loc__texts", loc);
            locDate = Ui.Text("", "sp-loc__date", locTexts, FontKind.MonoBold);
            locPlace = Ui.Text("", "sp-loc__place", locTexts, FontKind.Bold);

            deadline = Ui.El("sp-deadline", hud);
            Shape.Backdrop(deadline, Shape.Slant(16), Palette.Paper).Shadow(new Vector2(8, 8), Palette.Ink).Stroke(Palette.Ink, 3);
            deadlineName = Ui.Text("", "sp-deadline__name", deadline, FontKind.Bold);
            var daysRow = Ui.El("sp-deadline__row", deadline);
            Ui.Text("还有", "sp-deadline__small", daysRow, FontKind.Bold);
            deadlineDays = Ui.Text("", "sp-deadline__days", daysRow, FontKind.Black);
            Ui.Text("天", "sp-deadline__small", daysRow, FontKind.Bold);
            Ui.Show(deadline, false);

            // ---- dialogue box
            box = Ui.El("sp-box", ui);
            boxShape = Shape.Backdrop(box, BoxOutline(null), Palette.Ink).Shadow(new Vector2(12, 12), Palette.Green).Stroke(Palette.Paper, 5);
            scanlines = Ui.El("sp-box__scanlines", box);
            Ui.Fill(scanlines);
            for (var i = 0; i < 9; i++) Ui.El("sp-scanline", scanlines);
            namePlate = Ui.El("sp-nameplate", box);
            Shape.Backdrop(namePlate, Shape.Slant(16), Palette.Paper).Shadow(new Vector2(8, 8), Palette.Ink);
            namePlateBar = new Shape(Shape.Slant(10), Palette.Green);
            namePlateBar.AddToClassList("sp-nameplate__bar");
            namePlate.Add(namePlateBar);
            nameLabel = Ui.Text("", "sp-nameplate__name", namePlate, FontKind.Black);
            roleLabel = Ui.Text("", "sp-nameplate__role", namePlate, FontKind.Bold);
            textLabel = Ui.Text("", "sp-box__text", box);
            nextMark = new Shape(Shape.TriangleDown(), Palette.Green).Shadow(new Vector2(3, 3), Palette.Ink);
            nextMark.AddToClassList("sp-next");
            box.Add(nextMark);
            Ui.Show(box, false);

            // ---- quick bar
            quickBar = Ui.El("sp-quickbar", ui);
            autoButton = QuickButton(IconKind.Auto, "自动", "Q", ToggleAuto);
            skipButton = QuickButton(IconKind.Skip, "快进", "F", ToggleSkip);
            QuickButton(IconKind.Log, "回看", "L", () => game.Backlog.Open());
            QuickButton(IconKind.Save, "存档", null, () => game.SaveLoad.OpenSave());
            QuickButton(IconKind.Load, "读档", null, () => game.SaveLoad.OpenLoad());
            phoneButton = QuickButton(IconKind.Phone, "手机", "Tab", () => game.Phone.Open());
            QuickButton(IconKind.Config, "设置", null, () => game.SettingsModal.Open());
            QuickButton(IconKind.Hide, "隐藏界面", "H", ToggleHidden);
            QuickButton(IconKind.Menu, "菜单", "Esc", () => game.Menu.Open());
            // Comes and goes with the dialogue box: on its own it floats over the date card and full-screen moments.
            Ui.Show(quickBar, false);

            // ---- system toast
            toast = Ui.El("sp-toast", ui);
            Shape.Backdrop(toast, Shape.Slant(22), Palette.Ink.WithAlpha(0.94f));
            var tag = Ui.El("sp-toast__tag", toast);
            Shape.Backdrop(tag, Shape.Slant(22), Palette.Green);
            Ui.Text("SYSTEM", "sp-toast__tag-text", tag, FontKind.MonoBold);
            toastText = Ui.Text("", "sp-toast__text", toast, FontKind.MonoBold);
            toastCursor = Ui.El("sp-toast__cursor", toast);
            Ui.Show(toast, false);

            // ---- DeepSeek's thinking box
            think = Ui.El("sp-think", ui);
            var thinkHead = Ui.El("sp-think__head", think);
            for (var i = 0; i < 3; i++) Ui.El("sp-think__dot", thinkHead);
            thinkHeader = Ui.Text("思考中……", "sp-think__header", thinkHead, FontKind.Bold);
            thinkText = Ui.Text("", "sp-think__text", think);
            Ui.Show(think, false);

            // ---- phone screen (〔屏幕〕 lines)
            phone = Ui.El("sp-phone-screen", ui);
            var phoneGlass = Ui.El("sp-phone-screen__glass", phone);
            Ui.Text("03:00", "sp-phone-screen__clock", phoneGlass, FontKind.MonoBold);
            phoneLines = Ui.El("sp-phone-screen__lines", phoneGlass);
            Ui.Show(phone, false);

            // ---- projected file (〔文件〕 lines)
            file = Ui.El("sp-file", ui);
            var fileBar = Ui.El("sp-file__bar", file);
            Ui.Text("TERMINAL://projection", "sp-file__bar-text", fileBar, FontKind.Mono);
            fileLines = Ui.El("sp-file__lines", file);
            Ui.Show(file, false);

            // ---- options
            options = Ui.El("sp-options", ui);

            fx = Ui.El("sp-fx", Root);
            Ui.Fill(fx);
            fx.pickingMode = PickingMode.Ignore;

            Root.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0) RequestAdvance();
            });

            Root.schedule.Execute(Blink).Every(400);
        }

        static readonly float[] QuickTilts = { -4f, 3f, -2f, 4f, -3f, 2f, -4f, 3f, -2f };

        // A Persona-style icon badge in the top-right corner: a crooked ink badge with the glyph, the key that
        // does the same on a paper tag at its corner, and the name below it while the pointer is on it.
        VisualElement QuickButton(IconKind icon, string name, string key, Action onClick)
        {
            var index = quickBar.childCount;
            var b = Ui.El("sp-quick", quickBar);
            Shape.Backdrop(b, Shape.Jag(index + 3, 8f), QuickOff).Stroke(Palette.Paper, 2f).Shadow(new Vector2(4, 4), Palette.Ink.WithAlpha(0.6f));
            Ui.Turn(b, QuickTilts[index % QuickTilts.Length]);
            b.EnableInClassList("is-low", index % 2 == 1);
            var glyph = new Icon(icon);
            glyph.AddToClassList("sp-quick__icon");
            b.Add(glyph);
            if (key != null)
            {
                var cap = Ui.El("sp-quick__key", b);
                Shape.Backdrop(cap, Shape.Slant(4), Palette.Paper);
                Ui.Text(key, "sp-quick__key-text", cap, FontKind.MonoBold);
            }
            var tip = Ui.El("sp-quick__tip", b);
            Shape.Backdrop(tip, Shape.Slant(8), Palette.Paper).Shadow(new Vector2(4, 4), Palette.Green);
            Ui.Text(name, "sp-quick__tip-name", tip, FontKind.Black);
            var overlays = b.Query(className: "sp-quick__key").ToList();
            overlays.Add(tip);
            foreach (var label in overlays)
            {
                label.pickingMode = PickingMode.Ignore;
                label.Query().ForEach(e => e.pickingMode = PickingMode.Ignore);
            }
            b.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            b.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                game.Audio.Sfx("cursor");
                onClick();
            });
            return b;
        }

        static readonly Color QuickOff = Palette.Ink.WithAlpha(0.9f);
        const float BoxLeft = 160f; // .sp-box left, to turn stage positions into box pixels

        // Lit quick buttons (AUTO/SKIP on, PHONE with unread) fill with a colour; USS recolours the icon.
        static void SetQuick(VisualElement button, string state, Color color)
        {
            button.EnableInClassList(state, color != QuickOff);
            button.Q<Shape>().Fill(color);
        }

        // A menu or page over the story: the dialogue box and HUD step aside so nothing shows through.
        public void SetCovered(bool on)
        {
            if (covered == on) return;
            covered = on;
            var from = cover.resolvedStyle.opacity;
            var id = ++coverMotion;
            Anim.Tween(on ? 0.12f : 0.2f, t =>
            {
                if (id == coverMotion) cover.style.opacity = Mathf.Lerp(from, on ? 0f : 1f, t);
            }).Forget();
        }

        // The P5 speech box: a quadrilateral with no two edges parallel and, for speech, a spike out of the
        // top edge pointing at the speaker. tailX is in box pixels.
        static Func<Rect, Vector2[]> BoxOutline(float? tailX) => r =>
        {
            var tl = new Vector2(r.xMin + 28f, r.yMin + 4f);
            var tr = new Vector2(r.xMax - 6f, r.yMin + 24f);
            var br = new Vector2(r.xMax - 46f, r.yMax);
            var bl = new Vector2(r.xMin, r.yMax - 14f);
            if (tailX == null) return new[] { tl, tr, br, bl };
            var x = Mathf.Clamp(tailX.Value, tl.x + 460f, tr.x - 160f); // clear of the name tag
            float TopY(float at) => Mathf.Lerp(tl.y, tr.y, (at - tl.x) / (tr.x - tl.x));
            var lean = x < r.center.x ? -60f : 60f;
            return new[]
            {
                tl, new Vector2(x - 40f, TopY(x - 40f)), new Vector2(x + lean, r.yMin - 78f), new Vector2(x + 34f, TopY(x + 34f)),
                tr, br, bl,
            };
        };

        public void Show(bool on)
        {
            Ui.Show(Root, on);
        }

        // Start of a new game or a load: wipe everything the previous story left on screen.
        public void Reset()
        {
            epoch++;
            Backlog.Clear();
            Stage.Clear();
            HideAllLines();
            options.Clear();
            optionsActive = false;
            fx.Clear();
            autoMode = false;
            skipMode = false;
            hidden = false;
            ui.style.opacity = 1;
            SetQuick(autoButton, "is-on", QuickOff);
            SetQuick(skipButton, "is-on", QuickOff);
            bgBack.image = null;
            bgFront.image = null;
            ambientBack.Set(Ambient.None);
            ambientFront.Set(Ambient.None);
            RefreshHud();
        }

        public void RestoreScene()
        {
            var scene = game.State.scene;
            if (!string.IsNullOrEmpty(scene) && GameData.Scenes.TryGetValue(scene, out var def)) ShowScene(def);
            RefreshHud();
        }

        void ShowScene(SceneDef def)
        {
            bgBack.image = null;
            bgFront.image = Ui.Tex(def.BackgroundKey);
            bgFront.style.opacity = 1;
            ambientBack.Set(def.Ambient);
            ambientFront.Set(def.Ambient);
        }

        public void RefreshHud()
        {
            var s = game.State;
            locDate.text = $"{s.DateLabel}  {GameData.SlotLabel(s.slot)}";
            locPlace.text = GameData.SceneLabel(s.scene);
            Ui.Show(locPlace, !string.IsNullOrEmpty(s.scene));
            var d = s.NextDeadline();
            Ui.Show(deadline, d != null);
            if (d != null)
            {
                deadlineName.text = GameData.Deadlines.TryGetValue(d.id, out var label) ? label.Hud : d.id;
                deadlineDays.text = s.DaysUntil(d).ToString();
            }
            SetQuick(phoneButton, "has-badge", s.contacts.Count > 0 && game.Phone.HasUnseen ? Palette.Red : QuickOff);
        }

        // ------------------------------------------------------------------ input

        public void HandleInput()
        {
            if (optionsActive)
            {
                if (GameInput.Consume(Btn.Menu)) { game.Menu.Open(); return; }
                optionMenu.HandleInput();
                return;
            }
            if (GameInput.Consume(Btn.Menu)) { game.Menu.Open(); return; }
            if (GameInput.Consume(Btn.Log) || GameInput.ScrollY > 0) { game.Backlog.Open(); return; }
            if (GameInput.Consume(Btn.Phone)) { game.Phone.Open(); return; }
            if (GameInput.Consume(Btn.Auto)) ToggleAuto();
            if (GameInput.Consume(Btn.Skip)) ToggleSkip();
            if (GameInput.Consume(Btn.Hide)) ToggleHidden();
            if (GameInput.Consume(Btn.QuickSave)) game.QuickSave();
            if (GameInput.Consume(Btn.QuickLoad)) game.QuickLoad();
            if (GameInput.Consume(Btn.Confirm) || GameInput.ScrollY < 0) RequestAdvance();
        }

        void RequestAdvance()
        {
            if (hidden)
            {
                ToggleHidden();
                return;
            }
            if (optionsActive) return;
            advanceRequested = true;
            if (autoMode) ToggleAuto();
        }

        bool ConsumeAdvance()
        {
            if (!advanceRequested) return false;
            advanceRequested = false;
            return true;
        }

        void ToggleAuto()
        {
            autoMode = !autoMode;
            SetQuick(autoButton, "is-on", autoMode ? Palette.Green : QuickOff);
        }

        void ToggleSkip()
        {
            skipMode = !skipMode;
            SetQuick(skipButton, "is-on", skipMode ? Palette.Green : QuickOff);
        }

        void ToggleHidden()
        {
            hidden = !hidden;
            ui.style.opacity = hidden ? 0 : 1;
        }

        bool Paused => game.ModalOpen;

        bool SkipWanted => (skipMode || GameInput.SkipHeld) && !Paused && !optionsActive;

        bool CanSkip(string lineId) => game.Settings.skipUnread || game.Records.IsRead(lineId);

        // Used by the presentation commands (calendar card, Coop card...): wait for a click or key,
        // but never less than minSeconds, and give up on the wait when skipping.
        public async Awaitable WaitAdvance(float minSeconds, float autoSeconds = 2.5f)
        {
            advanceRequested = false;
            var t = 0f;
            while (true)
            {
                await Awaitable.NextFrameAsync();
                if (Paused) continue;
                t += Time.unscaledDeltaTime;
                if (SkipWanted && t > 0.15f) return;
                if (t < minSeconds) { advanceRequested = false; continue; }
                if (ConsumeAdvance()) return;
                if (autoMode && t >= autoSeconds) return;
            }
        }

        void Blink()
        {
            var on = Time.unscaledTime % 0.8f < 0.5f;
            toastCursor.style.opacity = on ? 1 : 0;
        }

        // ------------------------------------------------------------------ lines

        public async Awaitable PresentLine(LineInfo line, Func<bool> nextRequested, Func<bool> hurryRequested)
        {
            advanceRequested = false;
            if (line.Kind != LineKind.Screen) HidePhone();
            if (line.Kind != LineKind.File) HideFile();

            var timer = line.Kind == LineKind.System ? ThinkTimer.Match(line.Text) : null;
            if (timer != null && timer.Success)
            {
                await PresentThinkTimer(line, timer.Groups[1].Value, nextRequested);
                return;
            }

            Label target;
            var text = line.Text;
            var cps = game.Settings.CharsPerSecond;
            switch (line.Kind)
            {
                case LineKind.System:
                    HideBox();
                    Ui.Show(think, false);
                    target = ShowToast();
                    break;
                case LineKind.Thinking:
                    HideBox();
                    Ui.Show(toast, false);
                    Stage.OnLine(line.Speaker, line.Face, true);
                    target = ShowThink();
                    cps *= game.Settings.ThinkingMultiplier;
                    break;
                case LineKind.Screen:
                    HideBox();
                    Ui.Show(toast, false);
                    Ui.Show(think, false);
                    target = AddPhoneLine(text);
                    break;
                case LineKind.File:
                    HideBox();
                    Ui.Show(toast, false);
                    Ui.Show(think, false);
                    target = AddFileLine(text);
                    break;
                default:
                    Ui.Show(toast, false);
                    Ui.Show(think, false);
                    target = ShowBox(line);
                    if (line.Kind == LineKind.Inner) text = $"（{text}）";
                    break;
            }

            var fullText = line.Kind == LineKind.File ? HighlightWarning(text) : text;
            var shown = 0f;
            var skipping = SkipWanted && CanSkip(line.Id);
            if (SkipWanted && !skipping) StopSkipping();
            if (skipping || float.IsInfinity(cps)) shown = text.Length;
            // The mouth moves about as long as the line takes to read; instant text still gets that much.
            var speaking = line.Kind == LineKind.Speech || line.Kind == LineKind.Shadow;
            if (speaking) Stage.Talk(line.Speaker, skipping ? 0f : text.Length / Mathf.Clamp(cps, 8f, 14f));

            while (shown < text.Length)
            {
                if (nextRequested()) return;
                if (hurryRequested() || ConsumeAdvance() || (SkipWanted && CanSkip(line.Id)))
                {
                    if (speaking) Stage.Talk(line.Speaker, 0f);
                    break;
                }
                if (!Paused)
                {
                    var before = (int)shown;
                    shown += Time.unscaledDeltaTime * cps;
                    if ((int)shown > before && line.Kind == LineKind.System) game.Audio.Sfx("key", 0.25f);
                }
                target.text = Ui.GluePunctuation(text.Substring(0, Mathf.Min(text.Length, (int)shown)));
                await Awaitable.NextFrameAsync();
            }
            target.text = Ui.GluePunctuation(fullText);
            Record(line);

            Ui.Show(nextMark, Ui.Visible(box));
            var waited = 0f;
            var autoWait = game.Settings.autoDelay + text.Length * 0.03f;
            while (true)
            {
                if (nextRequested()) break;
                if (ConsumeAdvance()) break;
                if (SkipWanted && CanSkip(line.Id))
                {
                    await Anim.Wait(0.04f);
                    break;
                }
                if (autoMode && !Paused)
                {
                    waited += Time.unscaledDeltaTime;
                    if (waited >= autoWait) break;
                }
                await Awaitable.NextFrameAsync();
            }
            Ui.Show(nextMark, false);
        }

        void StopSkipping()
        {
            if (!skipMode) return;
            skipMode = false;
            SetQuick(skipButton, "is-on", QuickOff);
        }

        void Record(LineInfo line)
        {
            game.Records.MarkRead(line.Id);
            var speaker = line.Kind == LineKind.Narration ? null : line.Speaker;
            var text = line.Kind == LineKind.Inner ? $"（{line.Text}）" : line.Text;
            Backlog.Add(new BacklogEntry { Speaker = speaker, Text = text, Kind = line.Kind });
        }

        Label ShowBox(LineInfo line)
        {
            var kind = line.Kind;
            var withName = kind == LineKind.Speech || kind == LineKind.Player || kind == LineKind.Shadow;
            box.EnableInClassList("is-narration", kind == LineKind.Narration);
            box.EnableInClassList("is-inner", kind == LineKind.Inner);
            box.EnableInClassList("is-shadow", kind == LineKind.Shadow);
            box.EnableInClassList("is-whisper", line.Whisper);
            Ui.Show(scanlines, kind == LineKind.Shadow);
            Ui.Show(namePlate, withName);

            var def = withName ? GameData.Character(line.Speaker) : null;
            var color = kind == LineKind.Player ? Palette.Green : def?.Color ?? GameData.MinorSpeakerColor;
            var alpha = game.Settings.boxOpacity;
            switch (kind)
            {
                case LineKind.Narration:
                    boxShape.Fill(Palette.Paper.WithAlpha(alpha)).Stroke(Palette.Ink, 4).Shadow(new Vector2(12, 12), Palette.Ink);
                    break;
                case LineKind.Inner:
                    boxShape.Fill(Palette.Ink.WithAlpha(0.88f * alpha)).Stroke(Palette.Hex("#3A6B55"), 3).Shadow(new Vector2(10, 10), Palette.Ink.WithAlpha(0.6f));
                    break;
                case LineKind.Shadow:
                    boxShape.Fill(Palette.Cold.WithAlpha(alpha)).Stroke(Palette.Hex("#96A5BE"), 4).Shadow(new Vector2(12, 12), Palette.Sea);
                    break;
                default:
                    boxShape.Fill(Palette.Ink.WithAlpha(0.95f * alpha)).Stroke(Palette.Paper, 5).Shadow(new Vector2(12, 12), color);
                    break;
            }
            Fonts.Apply(textLabel, kind == LineKind.Shadow ? FontKind.Mono : FontKind.Regular);
            textLabel.style.fontSize = (line.Whisper ? 30f : 36f) * game.Settings.TextScale;

            if (withName)
            {
                nameLabel.text = line.Speaker;
                roleLabel.text = line.Whisper ? "小声" : kind == LineKind.Player ? "陪伴生" : def?.Role ?? "";
                Ui.Show(roleLabel, roleLabel.text.Length > 0);
                namePlateBar.Fill(color);
                if (line.Speaker != lastSpeaker || boxKind == null || boxKind == LineKind.Narration || boxKind == LineKind.Inner)
                {
                    // A new speaker: the name tag snaps in tilted and the box gives a small jolt.
                    Anim.Tween(0.22f, t =>
                    {
                        namePlate.style.translate = new Translate(Mathf.Lerp(-90f, 0f, t), 0);
                        Ui.Turn(namePlate, Mathf.LerpUnclamped(-14f, -4f, t));
                        namePlate.style.opacity = Mathf.Clamp01(t * 2f);
                    }, Ease.OutBack).Forget();
                    if (Ui.Visible(box))
                        Anim.Tween(0.2f, t => Ui.Turn(box, (1 - t) * 1.4f), Ease.OutBack).Forget();
                }
            }
            lastSpeaker = withName ? line.Speaker : null;

            var isSpeech = kind == LineKind.Speech || kind == LineKind.Shadow;
            if (isSpeech) Stage.OnLine(line.Speaker, line.Face, true);
            else Stage.SetSpeaker(null, kind != LineKind.Narration);
            // The spike points at whoever is talking, once they stand on the stage.
            var speakerX = isSpeech ? Stage.CenterOf(line.Speaker) : null;
            boxShape.Outline(BoxOutline(speakerX - BoxLeft));

            if (!Ui.Visible(box))
            {
                Ui.Show(box, true);
                Ui.Show(quickBar, true);
                Anim.Tween(0.2f, t =>
                {
                    box.style.opacity = Mathf.Clamp01(t * 2f);
                    quickBar.style.opacity = t;
                    box.style.translate = new Translate((1 - t) * -40f, (1 - t) * 40f);
                    Ui.Turn(box, (1 - t) * -3f);
                    box.style.scale = new Scale(new Vector2(1f, Mathf.LerpUnclamped(0.7f, 1f, t)));
                }, Ease.OutBack).Forget();
            }
            boxKind = kind;
            textLabel.text = "";
            return textLabel;
        }

        void HideBox()
        {
            Ui.Show(box, false);
            Ui.Show(quickBar, false);
            boxKind = null;
            lastSpeaker = null;
        }

        Label ShowToast()
        {
            Ui.Show(toast, true);
            toastText.text = "";
            Anim.Tween(0.2f, t =>
            {
                toast.style.translate = new Translate(Mathf.Lerp(-300f, 0f, t), 0);
                toast.style.opacity = t;
            }).Forget();
            game.Audio.Sfx("confirm", 0.6f);
            return toastText;
        }

        Label ShowThink()
        {
            if (!Ui.Visible(think))
            {
                Ui.Show(think, true);
                Anim.Tween(0.2f, t => think.style.scale = new Scale(new Vector2(Mathf.Lerp(0.6f, 1f, Ease.OutBack(t)), Mathf.Lerp(0.6f, 1f, Ease.OutBack(t)))), Ease.Linear).Forget();
            }
            thinkHeader.text = "思考中……";
            thinkText.text = "";
            Ui.Show(thinkText, true);
            return thinkText;
        }

        async Awaitable PresentThinkTimer(LineInfo line, string seconds, Func<bool> nextRequested)
        {
            HideBox();
            Ui.Show(toast, false);
            Ui.Show(think, true);
            Ui.Show(thinkText, false);
            var infinite = seconds == "∞";
            var target = infinite ? 99f : float.TryParse(seconds, out var s) ? s : 0f;
            var duration = infinite ? 2.2f : Mathf.Clamp(target * 0.15f, 0.6f, 2.2f);
            var skipping = SkipWanted && CanSkip(line.Id);
            var t = skipping ? duration : 0f;
            while (t < duration)
            {
                if (nextRequested()) return;
                if (ConsumeAdvance()) break;
                if (!Paused) t += Time.unscaledDeltaTime;
                var k = t / duration;
                var n = infinite ? Mathf.Pow(10f, k * 4f) : target * k;
                thinkHeader.text = $"思考中……（已思考 {Mathf.FloorToInt(n)} 秒）";
                await Awaitable.NextFrameAsync();
            }
            thinkHeader.text = line.Text;
            Record(line);
            while (!nextRequested() && !ConsumeAdvance())
            {
                if (SkipWanted && CanSkip(line.Id)) break;
                await Awaitable.NextFrameAsync();
            }
            Ui.Show(think, false);
        }

        Label AddPhoneLine(string text)
        {
            if (!Ui.Visible(phone))
            {
                phoneLines.Clear();
                Ui.Show(phone, true);
                game.Audio.Sfx("glitch");
                Anim.Tween(0.25f, t =>
                {
                    phone.style.opacity = t;
                    phone.style.scale = new Scale(new Vector2(1f, Mathf.Lerp(0.04f, 1f, t)));
                }).Forget();
            }
            var l = Ui.Text("", "sp-phone-screen__line", phoneLines, FontKind.MonoBold);
            l.EnableInClassList("is-error", text.Contains("无效"));
            return l;
        }

        void HidePhone()
        {
            if (Ui.Visible(phone)) Ui.Show(phone, false);
        }

        Label AddFileLine(string text)
        {
            if (!Ui.Visible(file))
            {
                fileLines.Clear();
                Ui.Show(file, true);
                game.Audio.Sfx("rise");
                Anim.Tween(0.3f, t =>
                {
                    file.style.opacity = t;
                    file.style.scale = new Scale(new Vector2(Mathf.Lerp(0.2f, 1f, t), 1f));
                }).Forget();
            }
            var first = fileLines.childCount == 0;
            return Ui.Text("", first ? "sp-file__title" : "sp-file__line", fileLines, first ? FontKind.Black : FontKind.Mono);
        }

        void HideFile()
        {
            if (Ui.Visible(file)) Ui.Show(file, false);
        }

        static string HighlightWarning(string text) =>
            text.Contains(WarningPhrase) ? text.Replace(WarningPhrase, $"<color={Palette.ToHex(Palette.Red)}>{WarningPhrase}</color>") : text;

        void HideAllLines()
        {
            HideBox();
            Ui.Show(toast, false);
            Ui.Show(think, false);
            Ui.Show(phone, false);
            Ui.Show(file, false);
            Ui.Show(nextMark, false);
        }

        // ------------------------------------------------------------------ options

        public async Awaitable<int> PresentOptions(string[] texts, bool[] available, Func<bool> cancelled)
        {
            StopSkipping();
            options.Clear();
            optionMenu.Clear();
            var result = -1;
            for (var i = 0; i < texts.Length; i++)
            {
                var index = i;
                var item = Ui.El("sp-option", options);
                item.style.marginRight = (texts.Length - 1 - i) * 34f; // a staircase, like P5's choices
                Shape.Backdrop(item, Shape.Jag(i * 31 + texts[i].Length, 14f), Palette.Ink.WithAlpha(0.95f)).Stroke(Palette.Paper, 4);
                var arrow = new Shape(Shape.TriangleRight(), Palette.Green);
                arrow.AddToClassList("sp-option__arrow");
                item.Add(arrow);
                Ui.Text(texts[i], "sp-option__text", item, FontKind.Bold);
                optionMenu.Add(item, () => result = index, available[i]);
                item.style.opacity = 0;
                var delay = i * 0.06f;
                AnimateOptionIn(item, delay).Forget();
            }
            optionMenu.SelectionChanged += RestyleOptions;
            optionMenu.Select(Mathf.Max(0, optionMenu.Index), false);
            RestyleOptions(optionMenu.Index);
            optionsActive = true;
            game.Audio.Sfx("whoosh", 0.6f);

            while (result < 0 && !cancelled()) await Awaitable.NextFrameAsync();

            optionMenu.SelectionChanged -= RestyleOptions;
            optionsActive = false;
            if (result >= 0)
            {
                Backlog.Add(new BacklogEntry { Speaker = game.State.playerName, Text = texts[result], Choice = true, Kind = LineKind.Player });
                var chosen = optionMenu[result];
                chosen.AddToClassList("is-chosen");
                await Anim.Wait(0.22f);
            }
            options.Clear();
            optionMenu.Clear();
            return result;
        }

        static async Awaitable AnimateOptionIn(VisualElement item, float delay)
        {
            await Anim.Wait(delay);
            await Anim.Tween(0.22f, t =>
            {
                item.style.opacity = Mathf.Clamp01(t * 2f);
                item.style.translate = new Translate((1 - t) * 320f, 0);
                Ui.Turn(item, (1 - t) * 8f);
            }, Ease.OutBack);
        }

        void RestyleOptions(int selected)
        {
            for (var i = 0; i < optionMenu.Count; i++)
            {
                var item = optionMenu[i];
                var on = i == selected;
                var shape = (Shape)item[0];
                shape.Fill(on ? Palette.Paper : Palette.Ink.WithAlpha(0.95f)).Stroke(on ? Palette.Ink : Palette.Paper, 4)
                    .Shadow(on ? new Vector2(10, 10) : Vector2.zero, Palette.Green);
                Ui.Turn(shape, on ? -2f : 0f);
                Ui.Zoom(item, on ? 1.04f : 1f);
            }
        }
    }
}
