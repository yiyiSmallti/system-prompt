using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    public enum Wipe
    {
        Slash,     // title ↔ story, loading, back from battle
        Location,  // story scene change, with the place name
        Battle,    // into a battle
    }

    // Persona-style screen changes on a layer above everything else. Cover slams panels over the frame,
    // the caller swaps what is underneath while nothing shows, Reveal clears the panels away.
    // GameRoot ignores input while one runs.
    public sealed class Transitions
    {
        // Slash: one train of slanted panels (green, paper, a wide ink cover, paper, green) crossing left
        // to right. Every piece leans by Slant px and butts against the next one along that edge.
        const float Slant = 360f;
        const float TrainTop = -40f;
        const float TrainHeight = 1160f;
        static readonly (float width, Color color)[] Train =
        {
            (480f, Palette.Green), (420f, Palette.Paper), (2680f, Palette.Ink), (420f, Palette.Paper), (480f, Palette.Green),
        };

        readonly GameRoot game;
        readonly VisualElement layer;
        VisualElement cover;
        VisualElement train;
        VisualElement detail;
        VisualElement[] halves;
        VisualElement seam;
        float trainCoverX;
        float trainWidth;
        int busy;

        public bool Busy => busy > 0;
        public Wipe Style { get; private set; }

        public Transitions(GameRoot game, VisualElement frame)
        {
            this.game = game;
            layer = Ui.El("sp-transition", frame);
            Ui.Fill(layer);
            layer.pickingMode = PickingMode.Ignore;
        }

        // Cover, swap, reveal; `after` runs once the new screen is fully in view.
        public async Awaitable Play(Wipe wipe, Action swap, string caption = null, Action after = null)
        {
            await Cover(wipe, caption);
            try
            {
                swap?.Invoke();
            }
            finally
            {
                await Reveal();
            }
            after?.Invoke();
        }

        public async Awaitable Cover(Wipe wipe, string caption = null)
        {
            while (busy > 0) await Awaitable.NextFrameAsync();
            busy++;
            Style = wipe;
            layer.pickingMode = PickingMode.Position; // swallow clicks while covered
            cover = Ui.El("sp-wipe is-" + wipe.ToString().ToLowerInvariant(), layer);
            Ui.Fill(cover);
            cover.pickingMode = PickingMode.Ignore;
            if (wipe == Wipe.Battle) await BattleIn();
            else await SlashIn(wipe, caption);
        }

        public async Awaitable Reveal()
        {
            if (cover == null) return;
            try
            {
                if (Style == Wipe.Battle) await BattleOut();
                else await SlashOut();
            }
            finally
            {
                cover.RemoveFromHierarchy();
                cover = train = detail = seam = null;
                halves = null;
                busy--;
                if (busy == 0) layer.pickingMode = PickingMode.Ignore;
            }
        }

        // ------------------------------------------------------------------ slash

        async Awaitable SlashIn(Wipe wipe, string caption)
        {
            train = Ui.El("sp-wipe__train", cover);
            train.style.top = TrainTop;
            train.style.height = TrainHeight;
            var x = 0f;
            var coverPieceX = 0f;
            foreach (var (width, color) in Train)
            {
                var piece = new Shape(Shape.Slant(Slant), color);
                piece.AddToClassList("sp-wipe__piece");
                piece.style.left = x;
                piece.style.width = width;
                train.Add(piece);
                if (color == Palette.Ink) coverPieceX = x;
                x += width - Slant;
            }
            trainWidth = x + Slant;
            train.style.width = trainWidth;
            // Covered when the ink piece's top-left corner is left of the frame and its bottom-right right of it.
            trainCoverX = -Slant - 20f - coverPieceX;

            detail = Ui.El("sp-wipe__detail", cover);
            Ui.Fill(detail);
            detail.style.opacity = 0;
            var dots = new Halftone(Palette.Green.WithAlpha(0.22f), 22f, 6f);
            Ui.Fill(dots);
            detail.Add(dots);
            if (wipe == Wipe.Location)
            {
                var card = Ui.El("sp-wipe__place", detail);
                Ui.Text("LOCATION", "sp-wipe__place-tag", card, FontKind.MonoBold);
                Ui.Text(caption ?? "", "sp-wipe__place-name", card, FontKind.Black);
            }
            else
            {
                var loading = Ui.El("sp-wipe__loading", detail);
                var mark = new Shape(Shape.Diamond(), Palette.Green);
                mark.AddToClassList("sp-wipe__loading-mark");
                loading.Add(mark);
                Ui.Text("NOW LOADING", "sp-wipe__loading-text", loading, FontKind.MonoBold);
                mark.schedule.Execute(() => Ui.Turn(mark, Time.unscaledTime * 360f % 360f)).Every(16);
            }

            game.Audio.Sfx("whoosh", 0.8f);
            var from = -trainWidth;
            await Anim.Tween(0.34f, t => train.style.translate = new Translate(Mathf.Lerp(from, trainCoverX, t), 0));
            await Anim.Tween(0.08f, t => detail.style.opacity = t, Ease.Linear);
            await Anim.Wait(wipe == Wipe.Location ? 0.45f : 0.12f);
        }

        async Awaitable SlashOut()
        {
            await Anim.Tween(0.08f, t => detail.style.opacity = 1 - t, Ease.Linear);
            var to = GameRoot.Width + 10f;
            await Anim.Tween(0.32f, t => train.style.translate = new Translate(Mathf.Lerp(trainCoverX, to, t), 0), Ease.InCubic);
        }

        // ------------------------------------------------------------------ battle

        async Awaitable BattleIn()
        {
            game.Audio.Sfx("crack", 0.9f);
            Fx.Flash(cover, Palette.Cold, 0.16f);
            var sea = Burst(Palette.Sea, Palette.Cold, 11);
            var ink = Burst(Palette.Ink, Palette.Sea, 17);
            Ui.Zoom(ink, 0f);
            await Anim.Tween(0.34f, t =>
            {
                Ui.Zoom(sea, Mathf.Lerp(0f, 8f, Mathf.Clamp01(t * 1.25f)));
                Ui.Turn(sea, t * 50f);
                Ui.Zoom(ink, Mathf.Lerp(0f, 8f, Mathf.Clamp01(t * 1.25f - 0.25f)));
                Ui.Turn(ink, -t * 30f);
            });

            // The bursts leave gaps at the corners: swap them for two solid halves split on the diagonal.
            halves = new VisualElement[]
            {
                Half(r => new[]
                {
                    new Vector2(r.xMin - 50f, r.yMax + 28f), new Vector2(r.xMax + 50f, r.yMin - 28f), new Vector2(r.xMin - 50f, r.yMin - 50f),
                }),
                Half(r => new[]
                {
                    new Vector2(r.xMax + 50f, r.yMin - 28f), new Vector2(r.xMax + 50f, r.yMax + 50f), new Vector2(r.xMin - 50f, r.yMax + 28f),
                }),
            };
            sea.RemoveFromHierarchy();
            ink.RemoveFromHierarchy();

            // Along the split: hides the anti-aliased seam while covered, flashes when the halves part.
            seam = Ui.El("sp-wipe__seam", cover);
            seam.style.rotate = new Rotate(new Angle(-Mathf.Atan2(GameRoot.Height, GameRoot.Width) * Mathf.Rad2Deg, AngleUnit.Degree));

            detail = Ui.El("sp-wipe__detail", cover);
            Ui.Fill(detail);
            var dots = new Halftone(Palette.Sea.WithAlpha(0.25f), 22f, 6f, false);
            Ui.Fill(dots);
            detail.Add(dots);
            var card = Ui.El("sp-wipe__encounter", detail);
            Ui.Text("ENCOUNTER", "sp-wipe__encounter-title", card, FontKind.Black);
            Ui.Text("NOISE DETECTED // 噪声体出现", "sp-wipe__encounter-sub", card, FontKind.MonoBold);
            game.Audio.Sfx("slam", 0.8f);
            await Anim.Tween(0.16f, t =>
            {
                Ui.Zoom(card, Mathf.Lerp(1.8f, 1f, t));
                card.style.opacity = t;
            }, Ease.OutBack);
            await Anim.Wait(0.32f);
        }

        async Awaitable BattleOut()
        {
            await Anim.Tween(0.08f, t => detail.style.opacity = 1 - t, Ease.Linear);
            seam.AddToClassList("is-lit");
            game.Audio.Sfx("whoosh", 0.8f);
            // Each half leaves along the diagonal's normal, away from the split.
            var normal = new Vector2(GameRoot.Height, GameRoot.Width).normalized;
            await Anim.Tween(0.36f, t =>
            {
                var d = normal * (t * 1400f);
                halves[0].style.translate = new Translate(-d.x, -d.y);
                halves[1].style.translate = new Translate(d.x, d.y);
                seam.style.opacity = 1 - t;
            }, Ease.InCubic);
        }

        Shape Burst(Color fill, Color stroke, int seed)
        {
            var b = new Shape(Shape.Burst(16, 0.62f, seed), fill).Stroke(stroke, 8);
            b.AddToClassList("sp-wipe__burst");
            cover.Add(b);
            return b;
        }

        VisualElement Half(Func<Rect, Vector2[]> outline)
        {
            var half = new Shape(outline, Palette.Ink);
            half.AddToClassList("sp-wipe__half");
            Ui.Fill(half);
            cover.Add(half);
            return half;
        }
    }
}
