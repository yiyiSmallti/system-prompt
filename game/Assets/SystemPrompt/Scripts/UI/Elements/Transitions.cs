using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace SystemPrompt.UI
{
    public enum Wipe
    {
        Slash,     // title ↔ story, loading, back from battle
        Location,  // story scene change, with the place name
        Battle,    // into a battle
        Dive,      // story → the latent-space maze
        Surface,   // the maze → story
        Lost,      // a lost battle → title
        Shutdown,  // the chapter's end → title, quitting
        Boot,      // the game starting (reveal only)
    }

    // Persona-style screen changes on a layer above everything else. Cover slams panels over the frame,
    // the caller swaps what is underneath while nothing shows, Reveal clears the panels away.
    // GameRoot ignores input while one runs; Confirm or Cancel after the first moment hurries it (three
    // times the speed, no pauses).
    //
    // One language throughout: the screen being left is grabbed and broken up through the shader filters
    // (FxSurface: RGB split, glitch slices, pixel blocks, zoom blur, CRT collapse), the cover is ink with
    // one of the generated pictures in Art/Transitions under scanlines, and the reveal takes the cover
    // away with a lit edge. Reality uses the P5 slash with a circuit board in the cover; the latent space
    // dives into the data sea and surfaces through a terminal; battles shatter; losing loses the signal;
    // the end of a chapter switches off like a CRT and the title boots back up through static.
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

        const string Art = "Art/Transitions/";

        // The data sea (16:9, like the frame) a little closer, with the bottom of its well in the middle, so
        // the fall heads straight down it.
        public static readonly Vector4 SeaRect = new Vector4(0.085f, 0.01f, 0.85f, 0.85f);

        readonly GameRoot game;
        readonly VisualElement frame;
        readonly VisualElement layer;
        // The fall down the data sea's well (Art/Transitions/dive.mp4, rendered by Tools/dive_video.py), a
        // seamless loop decoded from the start so a dive opens on a frame rather than black; paused between dives.
        readonly VideoPlayer diveVideo;
        VisualElement cover;
        VisualElement train;
        VisualElement detail;
        VisualElement[] halves;
        VisualElement seam;
        FxSurface back;   // the picture under the cover's text (circuit, shards, data sea, terminal)
        FxSurface grab;   // the screen being left
        SplitText title;
        float trainCoverX;
        float trainWidth;
        float started;
        bool hurried;
        int busy;

        public bool Busy => busy > 0;
        public Wipe Style { get; private set; }
        public bool Hurried => hurried; // Confirm or Cancel caught during the wipe running, or the last one

        public Transitions(GameRoot game, VisualElement frame)
        {
            this.game = game;
            this.frame = frame;
            layer = Ui.El("sp-transition", frame);
            Ui.Fill(layer);
            layer.pickingMode = PickingMode.Ignore;

            var clip = Resources.Load<VideoClip>(Art + "dive");
            diveVideo = new GameObject("Dive video").AddComponent<VideoPlayer>();
            diveVideo.transform.SetParent(game.transform, false);
            diveVideo.playOnAwake = false;
            diveVideo.isLooping = true;
            diveVideo.skipOnDrop = true;
            diveVideo.audioOutputMode = VideoAudioOutputMode.None;
            diveVideo.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            diveVideo.renderMode = VideoRenderMode.RenderTexture;
            diveVideo.targetTexture = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "Dive video" };
            diveVideo.clip = clip;
            diveVideo.Prepare();
        }

        // Cover, swap, reveal; `after` runs once the new screen is fully in view. preview: a picture of
        // where a Location wipe goes, decoded in a window under the place name.
        public async Awaitable Play(Wipe wipe, Action swap, string caption = null, Action after = null, Texture preview = null)
        {
            await Cover(wipe, caption, preview);
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

        public async Awaitable Cover(Wipe wipe, string caption = null, Texture preview = null)
        {
            while (busy > 0) await Awaitable.NextFrameAsync();
            Begin(wipe);
            switch (wipe)
            {
                case Wipe.Battle: await BattleIn(); break;
                case Wipe.Dive: await DiveIn(caption); break;
                case Wipe.Surface: await SurfaceIn(); break;
                case Wipe.Lost: await LostIn(); break;
                case Wipe.Shutdown: await ShutdownIn(); break;
                case Wipe.Boot: await BootIn(); break;
                default: await SlashIn(wipe, caption, preview); break;
            }
        }

        // arriving: moves what comes into view along with the reveal, by its progress (the dive's camera
        // coming down into the maze).
        public async Awaitable Reveal(Action<float> arriving = null)
        {
            if (cover == null) return;
            try
            {
                switch (Style)
                {
                    case Wipe.Battle: await BattleOut(); break;
                    case Wipe.Dive: await DiveOut(arriving); break;
                    case Wipe.Surface: await SurfaceOut(); break;
                    case Wipe.Lost:
                    case Wipe.Shutdown:
                    case Wipe.Boot: await Decode(); break;
                    default: await SlashOut(); break;
                }
            }
            finally
            {
                cover.RemoveFromHierarchy();
                cover = train = detail = seam = null;
                halves = null;
                back = grab = null;
                title = null;
                if (diveVideo.isPlaying) diveVideo.Pause();
                busy--;
                if (busy == 0) layer.pickingMode = PickingMode.Ignore;
            }
        }

        // The game starting: static over the title, then the title decodes through it.
        public async Awaitable Boot()
        {
            await Cover(Wipe.Boot);
            await Reveal();
        }

        void Begin(Wipe wipe)
        {
            busy++;
            Style = wipe;
            started = Time.unscaledTime;
            hurried = false;
            layer.pickingMode = PickingMode.Position; // swallow clicks while covered
            cover = Ui.El("sp-wipe is-" + wipe.ToString().ToLowerInvariant(), layer);
            Ui.Fill(cover);
            cover.pickingMode = PickingMode.Ignore;
        }

        // ------------------------------------------------------------------ pacing

        // GameRoot calls this every frame while a wipe runs, so a press is caught even on a frame where the
        // wipe itself is busy grabbing the screen or waiting for the swap.
        public void HandleInput()
        {
            if (!hurried && Time.unscaledTime - started > 0.2f && (GameInput.Pressed(Btn.Confirm) || GameInput.Pressed(Btn.Cancel)))
                hurried = true;
        }

        // One frame's worth of progress. A long frame (the maze being built under the cover) counts as a
        // short one, so the reveal after it is still seen.
        float Step() => Mathf.Min(Time.unscaledDeltaTime, 0.1f) * (hurried ? 3f : 1f);

        async Awaitable Run(float seconds, Action<float> step, Func<float, float> ease = null)
        {
            ease ??= Ease.OutCubic;
            var t = 0f;
            while (t < seconds)
            {
                step(ease(Mathf.Clamp01(t / seconds)));
                await Awaitable.NextFrameAsync();
                t += Step();
            }
            step(1f);
        }

        async Awaitable Hold(float seconds)
        {
            var t = 0f;
            while (t < seconds && !hurried)
            {
                await Awaitable.NextFrameAsync();
                t += Step();
            }
        }

        bool Calm => GameRoot.Instance.Settings.reduceFlash;

        // The screen as it is now, on the cover; it shows from the next frame, the moment it is taken.
        Awaitable<FxSurface> GrabScreen() => FxSurface.Grab(cover, frame, "sp-wipe__grab");

        FxSurface Picture(string name, VisualElement parent, string classes = null)
        {
            var fx = new FxSurface(parent, Ui.Tex(Art + name), classes);
            fx.Element.pickingMode = PickingMode.Ignore;
            return fx;
        }

        SplitText Headline(VisualElement parent, string text, string classes)
        {
            var h = new SplitText(text, classes, FontKind.Black);
            parent.Add(h);
            return h;
        }

        // ------------------------------------------------------------------ slash

        async Awaitable SlashIn(Wipe wipe, string caption, Texture preview)
        {
            grab = await GrabScreen();
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

            // Under the text once covered: the circuit board sliding past over a finer, slower one, packets of
            // light racing along its traces and bits of data drifting up.
            detail = Ui.El("sp-wipe__detail", cover);
            Ui.Fill(detail);
            detail.style.opacity = 0;
            var circuit = Ui.Tex(Art + "circuit");
            back = Picture("circuit", detail, "sp-wipe__back");
            back.Rect(new Vector4(0f, 0f, GameRoot.Width / 1400f, GameRoot.Height / 1400f)).Scroll(new Vector2(0.09f, 0.035f))
                .Set("_Tint", new Color(0.42f, 0.62f, 0.5f)).Set("_Pulse", 1.6f).Set("_Scan", 0.6f).Set("_Vignette", 0.9f)
                .Set("_Current", 4f).Set("_CurrentColor", new Color(0.7f, 1f, 0.85f))
                .Set("_Bits", 1.2f).Set("_BitsSpeed", 240f).Set("_BitsColor", Palette.Green)
                .Overlay(circuit, 0.2f, new Vector4(0f, 0f, GameRoot.Width / 640f, GameRoot.Height / 640f), new Vector2(-0.03f, 0.012f));
            var dots = new Halftone(Palette.Green.WithAlpha(0.16f), 22f, 6f);
            Ui.Fill(dots);
            detail.Add(dots);
            VisualElement window = null;
            FxSurface decode = null;
            if (wipe == Wipe.Location)
            {
                var card = Ui.El("sp-wipe__place", detail);
                Ui.Text("LOCATION", "sp-wipe__place-tag", card, FontKind.MonoBold);
                Ui.Text(caption ?? "", "sp-wipe__place-name", card, FontKind.Black);
                if (preview != null)
                {
                    // Where we are going, coming through the static as the cover holds.
                    window = Ui.El("sp-wipe__preview", detail);
                    decode = new FxSurface(window, preview, "sp-wipe__preview-picture", new Vector2(1180f, 300f));
                    decode.Set("_Pixel", 48f).Set("_Static", 0.5f).Set("_Scan", 0.6f).Set("_Split", 10f);
                    Ui.Text("DECODING //", "sp-wipe__preview-tag", window, FontKind.MonoBold);
                }
            }
            var loading = Ui.El("sp-wipe__loading", detail);
            var log = Ui.Text("> sync.scene ............", "sp-wipe__loading-log", loading, FontKind.Mono);
            var row = Ui.El("sp-wipe__loading-row", loading);
            var mark = new Shape(Shape.Diamond(), Palette.Green);
            mark.AddToClassList("sp-wipe__loading-mark");
            row.Add(mark);
            title = Headline(row, "NOW LOADING", "sp-wipe__loading-text");
            var bar = Ui.El("sp-wipe__loading-bar", loading);
            var fill = Ui.El("sp-wipe__loading-fill", bar);
            mark.schedule.Execute(() =>
            {
                Ui.Turn(mark, Time.unscaledTime * 360f % 360f);
                title?.Split(3f, Calm ? 0f : 3f);
            }).Every(16);

            game.Audio.Sfx("whoosh", 0.8f);
            var from = -trainWidth;
            await Run(0.34f, t =>
            {
                train.style.translate = new Translate(Mathf.Lerp(from, trainCoverX, t), 0);
                // The old screen tears and loses its colour as the train crosses it.
                grab.Set("_Split", 22f * t).Glitch(Calm ? 0f : 70f * t * t).Set("_Scan", 0.5f * t).Set("_Desat", 0.6f * t);
            });
            grab.Element.RemoveFromHierarchy();
            await Run(0.08f, t => detail.style.opacity = t, Ease.Linear);
            var hold = wipe == Wipe.Location ? 0.45f : 0.3f;
            await Run(hold, t =>
            {
                fill.style.width = Length.Percent(Mathf.Lerp(8f, 72f, t));
                decode?.Set("_Pixel", Mathf.Lerp(48f, 1f, t)).Set("_Static", 0.5f * (1 - t)).Set("_Split", 10f * (1 - t)).Set("_Scan", Mathf.Lerp(0.6f, 0.25f, t));
            }, Ease.Linear);
            log.text = "> sync.scene ............ ok";
            fill.style.width = Length.Percent(100f);
        }

        async Awaitable SlashOut()
        {
            await Run(0.08f, t => detail.style.opacity = 1 - t, Ease.Linear);
            var to = GameRoot.Width + 10f;
            await Run(0.32f, t => train.style.translate = new Translate(Mathf.Lerp(trainCoverX, to, t), 0), Ease.InCubic);
        }

        // ------------------------------------------------------------------ battle

        async Awaitable BattleIn()
        {
            grab = await GrabScreen();
            game.Audio.Sfx("crack", 0.9f);
            Fx.Flash(cover, Palette.Cold, 0.16f);
            var sea = Burst(Palette.Sea, Palette.Cold, 11);
            var ink = Burst(Palette.Ink, Palette.Sea, 17);
            Ui.Zoom(ink, 0f);
            await Run(0.34f, t =>
            {
                Ui.Zoom(sea, Mathf.Lerp(0f, 8f, Mathf.Clamp01(t * 1.25f)));
                Ui.Turn(sea, t * 50f);
                Ui.Zoom(ink, Mathf.Lerp(0f, 8f, Mathf.Clamp01(t * 1.25f - 0.25f)));
                Ui.Turn(ink, -t * 30f);
                // The maze is sucked into the burst: zoom blur, the colours fanning apart, gone blue.
                grab.Set("_Blur", 0.9f * t).Set("_Zoom", 1f + 0.35f * t).Set("_Split", 30f * t)
                    .Set("_Tint", Color.Lerp(Color.white, new Color(0.6f, 0.8f, 1.3f), t));
            });
            grab.Element.RemoveFromHierarchy();

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

            // Behind ENCOUNTER: glass shards flying past the camera without end, speed lines shooting out,
            // light streaks rushing sideways.
            detail = Ui.El("sp-wipe__detail", cover);
            Ui.Fill(detail);
            back = Picture("shards", detail, "sp-wipe__back");
            back.Set("_Tint", new Color(0.55f, 0.75f, 1.15f)).Set("_Blur", 0.25f).Set("_Scan", 0.35f).Set("_Vignette", 0.8f)
                .Set("_Fly", 1.8f).Set("_FlyAdd", 1f)
                .Set("_Streaks", 1.3f).Set("_StreakSpeed", 2.4f).Set("_StreakColor", new Color(0.75f, 0.9f, 1f))
                .Overlay(Ui.Tex(Art + "beams"), 0.9f, new Vector4(0f, 0f, 1f, 1f), new Vector2(-1.4f, 0f));
            var dots = new Halftone(Palette.Sea.WithAlpha(0.22f), 22f, 6f, false);
            Ui.Fill(dots);
            detail.Add(dots);
            var card = Ui.El("sp-wipe__encounter", detail);
            title = Headline(card, "ENCOUNTER", "sp-wipe__encounter-title");
            Ui.Text("NOISE DETECTED // 噪声体出现", "sp-wipe__encounter-sub", card, FontKind.MonoBold);
            game.Audio.Sfx("slam", 0.8f);
            await Run(0.16f, t =>
            {
                Ui.Zoom(card, Mathf.Lerp(1.8f, 1f, t));
                card.style.opacity = t;
                title.Split(Mathf.Lerp(40f, 7f, t), Calm ? 0f : 16f * (1 - t));
            }, Ease.OutBack);
            await Run(0.32f, t => title.Split(7f, Calm ? 0f : 4f), Ease.Linear);
        }

        async Awaitable BattleOut()
        {
            await Run(0.08f, t => detail.style.opacity = 1 - t, Ease.Linear);
            seam.AddToClassList("is-lit");
            game.Audio.Sfx("whoosh", 0.8f);
            // Each half leaves along the diagonal's normal, away from the split.
            var normal = new Vector2(GameRoot.Height, GameRoot.Width).normalized;
            await Run(0.36f, t =>
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

        // ------------------------------------------------------------------ dive / surface

        // Story → maze: the room breaks into blocks over the data sea, which pulls the camera down its
        // shaft; DIVE and the area's name hold, then the sea opens from the centre onto the maze.
        async Awaitable DiveIn(string caption)
        {
            grab = await GrabScreen();
            // Falling down the data sea's well, with speed lines and bits of data shooting up past.
            diveVideo.Play();
            back = new FxSurface(cover, diveVideo.targetTexture, "sp-wipe__back");
            back.Element.SendToBack();
            back.Set("_Scan", 0.25f).Set("_Vignette", 0.6f).Set("_Split", 3f)
                .Set("_Streaks", 0.6f).Set("_StreakSpeed", 1.6f).Set("_StreakColor", new Color(0.6f, 0.85f, 1f))
                .Set("_Bits", 0.5f).Set("_BitsSpeed", 420f).Set("_BitsColor", new Color(0.55f, 0.85f, 1f));
            game.Audio.Sfx("glitch", 0.7f);
            var splashed = false;
            await Run(0.55f, t =>
            {
                var k = t * t;
                grab.Set("_Pixel", Mathf.Lerp(1f, 36f, k)).Set("_Split", 18f * t).Set("_Desat", 0.5f * t)
                    .Set("_Tint", Color.Lerp(Color.white, new Color(0.6f, 0.85f, 1.2f), t))
                    .Mask(FxMask.Blocks, Mathf.Clamp01((t - 0.25f) / 0.75f), Palette.Green, 0.12f).Set("_Block", 72f);
                back.Set("_Zoom", Mathf.Lerp(1f, 1.1f, k)).Set("_Spin", 0.15f * t).Set("_Blur", 0.03f * t); // the video blurs its own motion
                if (!splashed && t > 0.45f)
                {
                    splashed = true;
                    game.Audio.Sfx("water", 0.7f);
                }
            }, Ease.Linear);
            grab.Element.RemoveFromHierarchy();

            detail = Ui.El("sp-wipe__detail sp-wipe__dive", cover);
            Ui.Fill(detail);
            title = Headline(detail, "DIVE", "sp-wipe__dive-title");
            Ui.Text("LATENT SPACE // CONNECTING", "sp-wipe__dive-sub", detail, FontKind.MonoBold);
            var place = Ui.El("sp-wipe__dive-place", detail);
            Shape.Backdrop(place, Shape.Slant(26), Palette.Paper).Shadow(new Vector2(10, 10), Palette.Sea);
            Ui.Text(caption ?? "", "sp-wipe__dive-place-text", place, FontKind.Black);
            var depth = Ui.Text("", "sp-wipe__dive-depth", detail, FontKind.MonoBold);
            await Run(0.5f, t =>
            {
                var slam = Mathf.Clamp01(t / 0.3f);
                Ui.Zoom(title, Mathf.Lerp(1.6f, 1f, Ease.OutBack(slam)));
                title.style.opacity = slam;
                title.Split(Mathf.Lerp(34f, 6f, slam), Calm ? 0f : 10f * (1 - slam) + 2f);
                place.style.translate = new Translate((1 - Ease.OutCubic(Mathf.Clamp01((t - 0.15f) / 0.4f))) * -900f, 0);
                depth.text = $"DEPTH ▼ {Mathf.RoundToInt(Mathf.Lerp(0f, 3072f, t)):0000} m";
                back.Set("_Zoom", Mathf.Lerp(1.1f, 1.2f, t)).Set("_Spin", 0.15f + 0.12f * t).Set("_Blur", 0.03f + 0.02f * t);
            }, Ease.Linear);
        }

        // The fall goes on through the bottom of the well into the maze: the well rushes past and opens from
        // its light outward with a soft, glowing rim, while the maze's camera comes down from high above.
        async Awaitable DiveOut(Action<float> arriving)
        {
            game.Audio.Sfx("rise", 0.6f);
            await Run(0.9f, t =>
            {
                detail.style.opacity = 1 - Mathf.Clamp01(t * 3f);
                Ui.Zoom(detail, 1f + t * 0.3f);
                back.Set("_Zoom", Mathf.Lerp(1.2f, 2.6f, t * t)).Set("_Spin", 0.27f + 0.15f * t)
                    .Mask(FxMask.Open, Ease.InOutCubic(t), Palette.Sea, 0.3f);
                arriving?.Invoke(t);
            }, Ease.Linear);
        }

        // Maze → story: the maze goes green and is wiped away upward onto a terminal log raining down in
        // columns (rising past it), SURFACE holds, and the log is wiped off top to bottom by a scan line.
        async Awaitable SurfaceIn()
        {
            grab = await GrabScreen();
            back = Picture("terminal", cover, "sp-wipe__back");
            back.Element.SendToBack();
            back.Rect(new Vector4(0f, 0f, 1f, 1f)).Set("_Rain", 0.5f).Set("_RainColumns", 56f)
                .Set("_Tint", new Color(0.85f, 1.25f, 0.95f)).Set("_Scan", 0.6f).Set("_Vignette", 0.8f)
                .Set("_Bits", 0.6f).Set("_BitsSpeed", -380f).Set("_BitsColor", new Color(0.6f, 1f, 0.75f));
            game.Audio.Sfx("rise", 0.7f);
            await Run(0.45f, t =>
            {
                grab.Set("_Desat", t).Set("_Tint", Color.Lerp(Color.white, new Color(0.5f, 1.15f, 0.7f), t))
                    .Set("_Scan", 0.7f * t).Set("_Split", 12f * t).Set("_Pixel", Mathf.Lerp(1f, 12f, t * t))
                    .Mask(FxMask.SweepUp, Mathf.Clamp01((t - 0.2f) / 0.8f), Palette.Paper, 0.02f);
            }, Ease.Linear);
            grab.Element.RemoveFromHierarchy();

            detail = Ui.El("sp-wipe__detail sp-wipe__surface", cover);
            Ui.Fill(detail);
            title = Headline(detail, "SURFACE", "sp-wipe__dive-title");
            Ui.Text("LOG OUT // 退出潜空间", "sp-wipe__dive-sub", detail, FontKind.MonoBold);
            var depth = Ui.Text("", "sp-wipe__dive-depth", detail, FontKind.MonoBold);
            await Run(0.35f, t =>
            {
                var slam = Mathf.Clamp01(t / 0.5f);
                Ui.Zoom(title, Mathf.Lerp(1.5f, 1f, Ease.OutBack(slam)));
                title.style.opacity = slam;
                title.Split(Mathf.Lerp(28f, 5f, slam), Calm ? 0f : 8f * (1 - slam) + 2f);
                depth.text = $"DEPTH ▲ {Mathf.RoundToInt(Mathf.Lerp(3072f, 0f, t)):0000} m";
                back.Glitch(Calm ? 0f : 30f * (1 - t));
            }, Ease.Linear);
        }

        async Awaitable SurfaceOut()
        {
            game.Audio.Sfx("whoosh", 0.6f);
            await Run(0.42f, t =>
            {
                detail.style.opacity = 1 - Mathf.Clamp01(t * 3f);
                back.Mask(FxMask.SweepDown, t, Palette.Green, 0.025f);
            }, Ease.InOutCubic);
        }

        // ------------------------------------------------------------------ signal lost / shutdown / boot

        // A lost battle: the field tears apart in red static under SIGNAL LOST, then switches off.
        async Awaitable LostIn()
        {
            grab = await GrabScreen();
            var corrupt = Ui.Tex(Art + "corrupt");
            game.Audio.Sfx("glitch");
            detail = Ui.El("sp-wipe__detail sp-wipe__lost", cover);
            Ui.Fill(detail);
            title = Headline(detail, "SIGNAL LOST", "sp-wipe__lost-title");
            Ui.Text("CONNECTION TERMINATED // 连接已断开", "sp-wipe__lost-sub", detail, FontKind.MonoBold);
            var cracked = false;
            await Run(0.75f, t =>
            {
                var beat = Mathf.Floor(Time.unscaledTime / 0.06f);
                var flicker = 0.25f + 0.7f * Mathf.Repeat(Mathf.Sin(beat * 12.9898f) * 43758.5453f, 1f);
                grab.Glitch(Calm ? 10f : 140f * t, 0.05f).Set("_Split", 40f * t).Set("_Static", 0.45f * t).Set("_Desat", 0.6f * t)
                    .Set("_Tint", Color.Lerp(Color.white, new Color(1.25f, 0.55f, 0.6f), t))
                    .Overlay(corrupt, (Calm ? 0.3f : flicker) * Mathf.Clamp01(t * 2f));
                title.style.opacity = Mathf.Clamp01(t * 5f);
                title.Split(Mathf.Lerp(30f, 10f, t), Calm ? 0f : 18f);
                if (!cracked && t > 0.5f)
                {
                    cracked = true;
                    game.Audio.Sfx("crack", 0.8f);
                }
            }, Ease.Linear);
            await Run(0.4f, t =>
            {
                detail.style.opacity = 1 - Mathf.Clamp01(t * 3f);
                grab.Set("_Collapse", t).Glitch(Calm ? 0f : 140f * (1 - t), 0.05f).Overlay(corrupt, 0.5f * (1 - t));
            }, Ease.InCubic);
            await Hold(0.25f);
        }

        // The chapter's end: the picture switches off like an old monitor.
        async Awaitable ShutdownIn()
        {
            grab = await GrabScreen();
            game.Audio.Sfx("whoosh", 0.5f);
            await Run(0.4f, t => grab.Set("_Collapse", t).Set("_Split", 8f * t).Set("_Scan", 0.5f * t), Ease.InCubic);
            await Hold(0.3f);
        }

        // Startup: snow on a dead screen, then the title decodes through it (Decode).
        async Awaitable BootIn()
        {
            back = Snow();
            // The first frames load fonts and shaders and can each take a good part of a second.
            for (var i = 0; i < 3; i++) await Awaitable.NextFrameAsync();
            await Hold(0.3f);
        }

        // Static over a faint circuit board.
        FxSurface Snow()
        {
            var snow = Picture("circuit", cover, "sp-wipe__back");
            snow.Rect(new Vector4(0f, 0f, GameRoot.Width / 1400f, GameRoot.Height / 1400f))
                .Set("_Tint", new Color(0.45f, 0.95f, 0.65f)).Set("_Static", 0.7f).Set("_Scan", 0.7f).Set("_Vignette", 1f)
                .Set("_Current", 1.5f).Set("_CurrentColor", new Color(0.6f, 1f, 0.8f))
                .Mask(FxMask.Blocks, 0f, Palette.Green, 0.1f).Set("_Block", 80f);
            return snow;
        }

        // The snow cleared away in random blocks with lit edges: the new screen comes in like a signal
        // locking on.
        async Awaitable Decode()
        {
            back ??= Snow();
            grab?.Element.RemoveFromHierarchy();
            game.Audio.Sfx("glitch", 0.5f);
            await Run(0.6f, t => back.Progress(t).Glitch(Calm ? 0f : 30f * t), Ease.Linear);
        }
    }
}
