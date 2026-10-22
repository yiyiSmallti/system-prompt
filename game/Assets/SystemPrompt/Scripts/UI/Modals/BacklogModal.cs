using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // 回看 as a chat transcript of every line since the story (re)started, newest at the bottom: the
    // characters speak in bubbles beside their faces on the left, the player's lines and choices sit on
    // the right in green, narration runs down the middle as captions and system text as mono chips.
    // It opens by rewinding (the log races down into place under tracking bars and a ◀◀ REWIND tag) and
    // leaves fast-forwarding up.
    public sealed class BacklogModal : Modal
    {
        const int MaxEntries = 300;
        const float ScrollStep = 140f;
        const float RewindSeconds = 0.42f;
        const float ExitSeconds = 0.2f;

        readonly VisualElement stage;
        readonly VisualElement side;
        readonly Label count;
        readonly VisualElement feed;
        readonly ScrollView scroll;
        readonly VisualElement rail;
        readonly VisualElement thumb;
        readonly VisualElement rewind;
        readonly Label rewindText;
        readonly VisualElement[] bars = new VisualElement[2];

        protected override bool OwnEntrance => true;
        protected override float OwnExit => ExitSeconds;

        public BacklogModal(GameRoot game) : base(game, "sp-chatlog", decorated: false)
        {
            stage = Ui.El("sp-chatlog__stage", Root);
            Ui.Fill(stage);
            var dots = new Halftone(Palette.Ink.WithAlpha(0.07f), 20f, 5f);
            Ui.Fill(dots);
            stage.Add(dots);

            feed = Ui.El("sp-chatlog__feed", stage);
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("sp-chatlog__scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            feed.Add(scroll);
            rail = Ui.El("sp-chatlog__rail", stage);
            thumb = Ui.El("sp-chatlog__thumb", rail);
            scroll.verticalScroller.valueChanged += _ => PlaceThumb();
            scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => PlaceThumb());

            // The ink band down the left with the page's name running up it.
            side = Ui.El("sp-chatlog__side", stage);
            Shape.Backdrop(side, r => new[]
            {
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax - 110f, r.yMax), new Vector2(r.xMin, r.yMax),
            }, Palette.Ink).Shadow(new Vector2(16, 0), Palette.Green);
            Ui.Text("LOG", "sp-chatlog__word", side, FontKind.Black);
            var head = Ui.El("sp-chatlog__head", side);
            Ui.Text("回看", "sp-chatlog__title", head, FontKind.Black);
            Ui.Text("BACKLOG", "sp-chatlog__tag", head, FontKind.MonoBold);
            count = Ui.Text("", "sp-chatlog__count", side, FontKind.MonoBold);

            rewind = Ui.El("sp-chatlog__rewind", stage);
            Shape.Backdrop(rewind, Shape.Slant(14), Palette.Red).Shadow(new Vector2(6, 6), Palette.Ink);
            rewindText = Ui.Text("", "sp-chatlog__rewind-text", rewind, FontKind.MonoBold);
            Ui.Text("↑↓ / 滚轮 翻看　Esc 返回", "sp-chatlog__hints", stage, FontKind.MonoBold);

            for (var i = 0; i < bars.Length; i++)
            {
                bars[i] = Ui.El("sp-chatlog__bar", Root);
                bars[i].pickingMode = PickingMode.Ignore;
            }
        }

        protected override void OnOpen()
        {
            scroll.Clear();
            var log = Game.Story.Backlog;
            var first = Mathf.Max(0, log.Count - MaxEntries);
            for (var i = first; i < log.Count; i++) scroll.Add(Entry(log[i]));
            if (log.Count == 0) Ui.Text("还没有对话。", "sp-chatlog__empty", scroll, FontKind.Bold);
            count.text = $"{log.Count:000} LINES";
            // Jump to the newest line once the entries have been laid out.
            scroll.schedule.Execute(() => scroll.verticalScroller.value = scroll.verticalScroller.highValue).ExecuteLater(1);
            Enter().Forget();
        }

        protected override void OnClose() => Leave().Forget();

        // ------------------------------------------------------------------ entries

        VisualElement Entry(BacklogEntry e)
        {
            if (e.Choice) return ChoiceEntry(e);
            if (e.Speaker == null || e.Kind == LineKind.Narration) return Caption(e.Text);
            switch (e.Kind)
            {
                case LineKind.System:
                case LineKind.Screen:
                case LineKind.File:
                    return SystemEntry(e);
                case LineKind.Player:
                case LineKind.Inner:
                    return Mine(e);
                default:
                    return Theirs(e);
            }
        }

        // Speech, DeepSeek's thinking and the shadow: face on the left, bubble pointing at it.
        static VisualElement Theirs(BacklogEntry e)
        {
            var def = GameData.Character(e.Speaker);
            var color = def?.Color ?? GameData.MinorSpeakerColor;
            var kind = e.Kind == LineKind.Shadow ? " is-shadow" : e.Kind == LineKind.Thinking ? " is-thinking" : "";
            var row = Ui.El("sp-msg is-theirs" + kind);
            row.Add(Avatar(def, e.Speaker, color));
            var body = Ui.El("sp-msg__body", row);
            NameTag(body, e.Kind == LineKind.Thinking ? $"{e.Speaker} · 思考" : e.Speaker, color, Palette.Paper);
            var bubble = Ui.El("sp-msg__bubble", body);
            var shape = Shape.Backdrop(bubble, Bubble(true), Palette.Ink).Stroke(color, 3);
            if (e.Kind == LineKind.Shadow) shape.Fill(Palette.Cold).Stroke(Palette.Sea, 3);
            if (e.Kind == LineKind.Thinking) shape.Fill(Palette.Paper).Stroke(Palette.Sea, 2);
            var mono = e.Kind == LineKind.Shadow || e.Kind == LineKind.Thinking;
            Ui.Text(Ui.GluePunctuation(e.Text), "sp-msg__text", bubble, mono ? FontKind.Mono : FontKind.Regular).enableRichText = true;
            return row;
        }

        // The player's lines in green; inner thoughts as a paper bubble with a green edge.
        VisualElement Mine(BacklogEntry e)
        {
            var inner = e.Kind == LineKind.Inner;
            var row = Ui.El("sp-msg is-mine" + (inner ? " is-inner" : ""));
            row.Add(Avatar(null, string.IsNullOrEmpty(Game.State.playerName) ? "我" : Game.State.playerName, Palette.Green));
            var body = Ui.El("sp-msg__body", row);
            NameTag(body, inner ? "心声" : e.Speaker, Palette.Green, Palette.Ink);
            var bubble = Ui.El("sp-msg__bubble", body);
            Shape.Backdrop(bubble, Bubble(false), inner ? Palette.Paper : Palette.Green).Stroke(inner ? Palette.Green : Palette.Ink, 3);
            Ui.Text(Ui.GluePunctuation(e.Text), "sp-msg__text", bubble).enableRichText = true;
            return row;
        }

        static VisualElement ChoiceEntry(BacklogEntry e)
        {
            var row = Ui.El("sp-msg is-choice");
            var chip = Ui.El("sp-msg__picked", row);
            Shape.Backdrop(chip, Shape.Slant(16), Palette.Ink).Stroke(Palette.Green, 3).Shadow(new Vector2(8, 8), Palette.Green);
            Ui.Text("▶ 选择", "sp-msg__picked-tag", chip, FontKind.MonoBold);
            Ui.Text(e.Text, "sp-msg__picked-text", chip, FontKind.Black);
            return row;
        }

        static VisualElement Caption(string text)
        {
            var row = Ui.El("sp-msg is-caption");
            Ui.El("sp-msg__rule", row);
            Ui.Text(Ui.GluePunctuation(text), "sp-msg__caption", row, FontKind.Bold).enableRichText = true;
            Ui.El("sp-msg__rule", row);
            return row;
        }

        static VisualElement SystemEntry(BacklogEntry e)
        {
            var row = Ui.El("sp-msg is-system");
            var chip = Ui.El("sp-msg__system", row);
            Ui.Text(Ui.GluePunctuation($"[{e.Speaker}] {e.Text}"), "sp-msg__system-text", chip, FontKind.Mono).enableRichText = true;
            return row;
        }

        static void NameTag(VisualElement parent, string name, Color fill, Color text)
        {
            var tag = Ui.El("sp-msg__name", parent);
            Shape.Backdrop(tag, Shape.Slant(10), fill);
            Ui.Text(name, "sp-msg__name-text", tag, FontKind.Black).style.color = text;
        }

        // A face in a crooked frame of the speaker's colour; the initial when there is no portrait.
        static VisualElement Avatar(CharacterDef def, string name, Color color)
        {
            var frame = Ui.El("sp-msg__avatar");
            Shape.Backdrop(frame, Shape.Jag(name.Length * 13 + name[0], 7f), color);
            if (def != null && def.HasPortrait)
            {
                Ui.FaceCrop(def.PortraitKey(def.DefaultFace), 92f, 92f, "sp-msg__face", frame);
            }
            else
            {
                var tile = Ui.El("sp-msg__face is-initial", frame);
                Ui.Text(name.Substring(0, 1), "sp-msg__initial", tile, FontKind.Black);
            }
            return frame;
        }

        // A crooked speech bubble with its tail at the top on the avatar's side.
        static Func<Rect, Vector2[]> Bubble(bool tailLeft) => r =>
        {
            Vector2 P(float fromSide, float y) => new Vector2(tailLeft ? r.xMin + fromSide : r.xMax - fromSide, y);
            var far = r.width;
            return new[]
            {
                P(18f, r.yMin + 3f), P(far - 6f, r.yMin), P(far, r.yMax - 5f), P(24f, r.yMax),
                P(20f, r.yMin + 46f), P(0f, r.yMin + 30f), P(18f, r.yMin + 20f),
            };
        };

        void PlaceThumb()
        {
            var s = scroll.verticalScroller;
            var view = scroll.contentViewport.layout.height;
            var content = scroll.contentContainer.layout.height;
            var height = rail.layout.height;
            if (float.IsNaN(view) || float.IsNaN(content) || content <= 0 || height <= 0) return;
            var size = Mathf.Max(48f, height * Mathf.Clamp01(view / content));
            var range = s.highValue - s.lowValue;
            thumb.style.height = size;
            thumb.style.top = (height - size) * (range > 0 ? Mathf.Clamp01((s.value - s.lowValue) / range) : 1f);
        }

        // ------------------------------------------------------------------ motion

        async Awaitable Enter()
        {
            rewindText.text = "◀◀ REWIND";
            Game.Audio.Sfx("rise", 0.5f);
            await PlayMotion(RewindSeconds + 0.2f, time =>
            {
                stage.style.opacity = Mathf.Clamp01(time / 0.06f);
                var k = Ease.OutCubic(Mathf.Clamp01(time / RewindSeconds));
                var shake = time < RewindSeconds ? Mathf.Sin(time * 95f) * 12f * (1 - k) : 0f;
                feed.style.translate = new Translate(shake, (1 - k) * -1500f);
                side.style.translate = new Translate((1 - Ease.OutBack(Mathf.Clamp01(time / 0.26f))) * -520f, 0);
                rewind.style.opacity = time < RewindSeconds ? (Mathf.Repeat(time, 0.16f) < 0.1f ? 1f : 0.25f) : 1f;
                for (var i = 0; i < bars.Length; i++)
                {
                    bars[i].style.top = Mathf.Repeat(time * 3000f + i * 620f, 1400f) - 160f;
                    bars[i].style.opacity = time < RewindSeconds ? 0.75f : 0f;
                }
            });
        }

        async Awaitable Leave()
        {
            rewindText.text = "▶▶ FF";
            await PlayMotion(ExitSeconds, time =>
            {
                var k = Ease.InCubic(time / ExitSeconds);
                feed.style.translate = new Translate(0, k * -1300f);
                side.style.translate = new Translate(k * -520f, 0);
                stage.style.opacity = 1 - Mathf.Clamp01((time - 0.08f) / 0.12f);
                foreach (var bar in bars) bar.style.opacity = 0f;
            });
        }

        public override void HandleInput()
        {
            if (GameInput.Consume(Btn.Cancel) || GameInput.Consume(Btn.Log) || GameInput.Consume(Btn.Menu))
            {
                Game.Audio.Sfx("cursor");
                Close();
                return;
            }
            var s = scroll.verticalScroller;
            if (GameInput.Consume(Btn.Up)) s.value = Mathf.Max(s.lowValue, s.value - ScrollStep);
            if (GameInput.Consume(Btn.Down)) s.value = Mathf.Min(s.highValue, s.value + ScrollStep);
        }
    }
}
