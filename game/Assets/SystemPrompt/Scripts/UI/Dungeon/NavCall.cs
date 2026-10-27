using System;
using System.Collections.Generic;
using SystemPrompt.Dungeon;
using SystemPrompt.Story;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // DeepSeek calling out from the top right while the player keeps going, like the navigator in P5: her
    // navigator bust (a hand at her mouth, CharacterDef.NavFaces) in a tilted frame by the corner map, a
    // speech bubble beside it. Story nodes the dungeon plays this
    // way (the gate opening, the exit showing, the maintenance warning...) are read in at once and queue up
    // here, so the hint runner is free again in a frame or two; each line types out, stays a moment and
    // gives way to the next, and the callout slides off when the queue is empty. A line waits while the
    // screen is hidden or the text box is up.
    public sealed class NavCall : ILinePresenter
    {
        public readonly VisualElement Root;
        readonly GameRoot game;
        readonly Func<bool> held;
        readonly VisualElement frame, bubble;
        readonly Shape frameShape, tagShape;
        readonly Image face;
        readonly Label name, text;
        readonly Queue<LineInfo> queue = new Queue<LineInfo>();
        int serial;
        bool playing;
        string navFace; // the bust's face, kept from line to line until one is tagged with another

        // The companion in the world, whose face and mouth follow the line.
        public SeaCompanion Companion;

        public NavCall(GameRoot game, VisualElement parent, Func<bool> held)
        {
            this.game = game;
            this.held = held;
            Root = Ui.El("sp-nav", parent);
            Root.pickingMode = PickingMode.Ignore;

            bubble = Ui.El("sp-nav__bubble", Root);
            Shape.Backdrop(bubble, Shape.Crooked(10), Palette.Paper).Stroke(Palette.Ink, 4).Shadow(new Vector2(10, 10), Palette.Ink.WithAlpha(0.85f));
            var tail = new Shape(Shape.TriangleRight(), Palette.Paper).Stroke(Palette.Ink, 4);
            tail.AddToClassList("sp-nav__tail");
            bubble.Add(tail);
            text = Ui.Text("", "sp-nav__text", bubble, FontKind.Black);

            frame = Ui.El("sp-nav__frame", Root);
            frameShape = Shape.Backdrop(frame, Shape.Crooked(16), Palette.Sea).Stroke(Palette.Ink, 5).Shadow(new Vector2(10, 10), Palette.Ink.WithAlpha(0.85f));
            var window = Ui.El("sp-nav__face", frame);
            face = Ui.Picture(null, ScaleMode.ScaleToFit, "sp-nav__face-image", window);
            Ui.Fill(face);
            Ui.Text("NAVI", "sp-nav__call", frame, FontKind.MonoBold);
            var tag = Ui.El("sp-nav__tag", frame);
            tagShape = Shape.Backdrop(tag, Shape.Slant(10), Palette.Paper).Stroke(Palette.Ink, 3);
            name = Ui.Text("", "sp-nav__tag-text", tag, FontKind.Black);
            Ui.Turn(frame, 4f);
            Ui.Turn(bubble, -2f);
            Ui.Show(Root, false);
        }

        // Whether a line is up or waiting.
        public bool Busy => playing || queue.Count > 0;

        // The line in the bubble once it has typed out (the end-to-end run checks it).
        public string Said { get; private set; }

        public void Clear()
        {
            serial++;
            queue.Clear();
            playing = false;
            Said = null;
            Ui.Show(Root, false);
            if (Companion != null) Companion.Talking = false;
        }

        // ------------------------------------------------------------------ lines (ILinePresenter)

        public Awaitable PresentLine(LineInfo line, Func<bool> nextRequested, Func<bool> hurryRequested)
        {
            queue.Enqueue(line);
            game.Records.MarkRead(line.Id);
            game.Story.Backlog.Add(new BacklogEntry { Speaker = line.Speaker, Text = line.Text, Kind = line.Kind });
            if (!playing) Play().Forget();
            return Anim.Wait(0f);
        }

        public async Awaitable<int> PresentOptions(string[] texts, bool[] available, Func<bool> cancelled)
        {
            Debug.LogWarning("右上角播报的剧本节点不支持选项，已跳过");
            await Awaitable.NextFrameAsync();
            return -1;
        }

        // ------------------------------------------------------------------ playing

        async Awaitable Play()
        {
            var run = serial;
            playing = true;
            navFace = null;
            var up = false;
            while (queue.Count > 0 && run == serial)
            {
                // The text box (or a battle) takes over: off the screen until it is done, then back in.
                if (held())
                {
                    if (up) await Out(run);
                    up = false;
                    while (held() && run == serial) await Awaitable.NextFrameAsync();
                    if (run != serial) return;
                }
                var line = queue.Dequeue();
                Dress(line);
                if (!up) await In(run);
                else await Bump(run);
                up = true;
                if (run != serial) return;
                await Say(line, run);
                if (run != serial) return;
                await Anim.Wait(1.3f + 0.07f * line.Text.Length, () => run != serial || held());
            }
            if (run != serial) return;
            if (up) await Out(run);
            if (run == serial) playing = false;
        }

        // The speaker's bust with the line's face (the last one when the line has none), colour and name. Once
        // she has awakened she is in her battle form down here, as in the story's latent-space scenes.
        void Dress(LineInfo line)
        {
            var who = GameData.Character(line.Speaker);
            var colour = who?.Color ?? Palette.Paper;
            frameShape.Fill(colour);
            tagShape.Fill(colour);
            name.text = line.Speaker ?? "";
            if (who != null && who.NavFaces.Length > 0)
            {
                if (line.Face != null && Array.IndexOf(who.NavFaces, line.Face) >= 0) navFace = line.Face;
                navFace ??= who.NavFaces[0];
                face.image = Ui.Tex(who.NavKey(game.State.FormOf(who.Id) != null ? "battle" : null, navFace));
            }
            else Debug.LogError($"{line.Speaker} 没有导航立绘（CharacterDef.NavFaces），不能在右上角播报");
            text.text = "";
            Said = null;
            if (line.Speaker == "DeepSeek" && Companion != null && line.Face != null) Companion.SetFace(line.Face);
        }

        async Awaitable Say(LineInfo line, int run)
        {
            var her = line.Speaker == "DeepSeek" ? Companion : null;
            if (her != null) her.Talking = true;
            game.Audio.Sfx("key", 0.35f);
            var cps = game.Settings.CharsPerSecond;
            var all = line.Text;
            var shown = float.IsInfinity(cps) ? all.Length : 0f;
            while (shown < all.Length && run == serial)
            {
                shown += Time.unscaledDeltaTime * cps;
                text.text = Ui.GluePunctuation(all.Substring(0, Mathf.Min(all.Length, (int)shown)));
                await Awaitable.NextFrameAsync();
            }
            text.text = Ui.GluePunctuation(all);
            if (run == serial) Said = all;
            if (her != null) her.Talking = false;
        }

        // The frame swings in from the right edge and the bubble pops out of it.
        async Awaitable In(int run)
        {
            Ui.Show(Root, true);
            game.Audio.Sfx("whoosh", 0.5f);
            Ui.Opacity(bubble, 0f);
            await Anim.Tween(0.24f, t =>
            {
                Ui.Offset(frame, Mathf.Lerp(520f, 0f, t), Mathf.Lerp(-40f, 0f, t));
                Ui.Turn(frame, Mathf.Lerp(24f, 4f, t));
                Ui.Opacity(frame, Mathf.Clamp01(t * 2f));
            }, Ease.OutBack, () => run != serial);
            await Anim.Tween(0.18f, t =>
            {
                Ui.Opacity(bubble, t);
                Ui.Zoom(bubble, Mathf.Lerp(0.5f, 1f, t));
            }, Ease.OutBack, () => run != serial);
        }

        async Awaitable Bump(int run)
        {
            await Anim.Tween(0.16f, t => Ui.Zoom(bubble, 1f + 0.08f * Mathf.Sin(t * Mathf.PI)), null, () => run != serial);
        }

        async Awaitable Out(int run)
        {
            await Anim.Tween(0.22f, t =>
            {
                Ui.Opacity(bubble, 1f - t);
                Ui.Offset(frame, Mathf.Lerp(0f, 520f, t), 0f);
                Ui.Opacity(frame, 1f - t);
            }, Ease.InCubic, () => run != serial);
            if (run != serial) return;
            Ui.Show(Root, false);
            Said = null;
            Companion?.SetFace(null);
        }
    }
}
