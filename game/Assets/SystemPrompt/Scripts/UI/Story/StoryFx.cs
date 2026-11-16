using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace SystemPrompt.UI
{
    // Presentation for the Yarn commands: calendar card, scene change, cutscene picture, awakening, Coop card, chapter end.
    public sealed partial class StoryScreen
    {
        static readonly string[] EffortStops = { "低", "中", "高", "Max" };

        // Waits until at least `min` seconds have passed and the player advances, or `max` seconds pass.
        async Awaitable WaitBeat(float min, float max)
        {
            advanceRequested = false;
            var t = 0f;
            while (t < max)
            {
                await Awaitable.NextFrameAsync();
                if (Paused) continue;
                t += Time.unscaledDeltaTime;
                if (SkipWanted) return;
                if (t >= min && ConsumeAdvance()) return;
                if (t < min) advanceRequested = false;
            }
        }

        // <<calendar 月 日 时段>>
        public async Awaitable ShowCalendar(int month, int day, string slot)
        {
            RefreshHud();
            if (SkipWanted) return;
            HideAllLines();
            var card = Ui.El("fx-calendar", fx);
            Ui.Fill(card);
            var band = Ui.El("fx-calendar__band", card);
            Shape.Backdrop(band, Shape.Slant(220), Palette.Ink);
            var dots = new Halftone(Palette.Green.WithAlpha(0.55f), 18f, 6f);
            dots.AddToClassList("fx-calendar__dots");
            band.Add(dots);
            var date = new SplitText($"{month}/{day}", "fx-calendar__date", FontKind.Black);
            band.Add(date);
            var dow = Ui.El("fx-calendar__dow", band);
            Shape.Backdrop(dow, Shape.Slant(20), Palette.Green);
            Ui.Text(GameData.WeekdayLabel(month, day), "fx-calendar__dow-text", dow, FontKind.MonoBold);
            var slotPanel = Ui.El("fx-calendar__slot", band);
            Shape.Backdrop(slotPanel, Shape.Slant(20), Palette.Paper).Shadow(new Vector2(10, 10), Palette.Green);
            var slotText = GameData.SlotLabel(slot) + (slot == "late_night" ? "　02:59" : "");
            Ui.Text(slotText, "fx-calendar__slot-text", slotPanel, FontKind.Black);
            Ui.Text(GameData.WeatherOn(month, day), "fx-calendar__weather", band, FontKind.Bold);

            // The date's colours trail behind it on the way in and snap together on the slam.
            var calm = game.Settings.reduceFlash;
            game.Audio.Sfx("whoosh");
            await Anim.Tween(0.3f, t =>
            {
                band.style.translate = new Translate((1 - t) * -2400f, 0);
                date.Split(Mathf.Lerp(60f, 14f, t));
            });
            game.Audio.Sfx("slam", 0.7f);
            Anim.Tween(0.3f, t => date.Split(14f * (1 - t), calm ? 0f : 12f * (1 - t)), Ease.OutCubic).Forget();
            await WaitBeat(0.4f, 1.5f);
            await Anim.Tween(0.24f, t =>
            {
                band.style.translate = new Translate(t * 2400f, 0);
                date.Split(40f * t);
            }, Ease.InCubic);
            card.RemoveFromHierarchy();
        }

        // <<scene 场景ID>>
        public async Awaitable ChangeScene(string id)
        {
            if (!GameData.Scenes.TryGetValue(id, out var def))
            {
                Debug.LogError($"<<scene {id}>>：GameData.Scenes 里没有这个场景");
                return;
            }
            game.State.scene = id;
            HideAllLines();
            if (SkipWanted)
            {
                Stage.Clear();
                ShowScene(def);
            }
            else if (def.Latent)
            {
                Stage.Clear();
                await GlitchSwap(def);
            }
            else
            {
                // Persona-style location card: the old place (and whoever stood there) goes under the wipe.
                var e = epoch;
                await game.Transitions.Play(Wipe.Location, () =>
                {
                    if (e != epoch) return;
                    Stage.Clear();
                    ShowScene(def);
                    RefreshHud();
                }, def.Label, preview: Ui.Tex(def.BackgroundKey));
            }
            RefreshHud();
        }

        // ------------------------------------------------------------------ <<cg>>

        // The picture that is up, by its id in GameData.Cgs; null when the scene shows.
        public string Cg { get; private set; }
        // Whether the picture that is up is its moving version (false when skipped to, or it has none).
        public bool CgMoving => cgVideo != null && cgVideo.clip != null;
        // How far into its clip the picture is, in frames; -1 for a still.
        public long CgFrame => CgMoving ? cgVideo.frame : -1;

        VisualElement cgPicture;
        VideoPlayer cgVideo;
        int cgTake; // bumped whenever the picture changes, so a clip still loading for an old one is dropped

        // <<cg 图ID>> puts a full-screen picture over the scene and its portraits, <<cg off>> fades it away;
        // a scene change, a battle or the maze takes it down as well. A picture with a clip fades in on its first
        // frame, plays it once and holds its last frame, which is also the still shown when the story is being
        // skipped; either way it keeps drifting closer to its subject.
        public async Awaitable ShowCg(string id)
        {
            if (id == "off")
            {
                await HideCg();
                return;
            }
            if (!GameData.Cgs.TryGetValue(id, out var def))
            {
                Debug.LogError($"<<cg {id}>>：GameData.Cgs 里没有这张图");
                return;
            }
            var still = Ui.Tex(def.PictureKey);
            if (still == null) return;
            HideAllLines();
            var e = epoch;
            var take = ++cgTake;
            var skipping = SkipWanted;

            // The clip's first frame is decoded before anything shows and held while the picture fades in: the
            // clip starts once it can be seen (the shadow in 深渊之鲸 is gone within its first two seconds).
            Texture shown = still;
            var clip = skipping ? null : Resources.Load<VideoClip>(def.MotionKey);
            if (clip != null)
            {
                var video = CgVideo();
                video.Stop();
                ReleaseCgTarget();
                video.targetTexture = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "CG video" };
                video.clip = clip;
                video.Play();
                var waited = 0f;
                while (video.frame < 0 && waited < 2f)
                {
                    await Awaitable.NextFrameAsync();
                    waited += Time.unscaledDeltaTime;
                }
                if (e != epoch || take != cgTake) return;
                if (video.frame >= 0)
                {
                    video.Pause();
                    shown = video.targetTexture;
                }
                else
                {
                    Debug.LogWarning($"<<cg {id}>>：动态版两秒内没放出第一帧，改用静态图");
                    StopCgVideo();
                }
            }
            else StopCgVideo();

            var old = cgPicture;
            var picture = Ui.Picture(shown, ScaleMode.ScaleAndCrop, "sp-cg__picture", cgLayer);
            Ui.Fill(picture);
            picture.style.transformOrigin = new TransformOrigin(Length.Percent(def.Focus.x * 100f), Length.Percent(def.Focus.y * 100f));
            var born = Time.unscaledTime;
            picture.schedule.Execute(() => Ui.Zoom(picture, 1f + 0.07f * (1f - Mathf.Exp(-(Time.unscaledTime - born) / 16f)))).Every(16);
            cgPicture = picture;
            Cg = id;
            if (skipping)
            {
                old?.RemoveFromHierarchy();
                return;
            }

            picture.style.opacity = 0;
            if (def.Sfx != null) game.Audio.Sfx(def.Sfx, 0.7f);
            await Anim.Tween(0.7f, t => picture.style.opacity = t, Ease.Linear);
            old?.RemoveFromHierarchy();
            if (e == epoch && take == cgTake && shown != still) cgVideo.Play();
            // A moment with nothing over the picture before the next line's box comes up.
            if (e == epoch && take == cgTake) await WaitBeat(0.5f, 1.4f);
        }

        async Awaitable HideCg()
        {
            var old = cgPicture;
            if (old == null) return;
            HideAllLines();
            cgPicture = null;
            Cg = null;
            var take = ++cgTake;
            if (!SkipWanted) await Anim.Tween(0.6f, t => old.style.opacity = 1 - t, Ease.Linear);
            old.RemoveFromHierarchy();
            if (take == cgTake) StopCgVideo();
        }

        void ClearCg()
        {
            cgTake++;
            cgLayer.Clear();
            cgPicture = null;
            Cg = null;
            StopCgVideo();
        }

        VideoPlayer CgVideo()
        {
            if (cgVideo != null) return cgVideo;
            cgVideo = new GameObject("CG video").AddComponent<VideoPlayer>();
            cgVideo.transform.SetParent(game.transform, false);
            cgVideo.playOnAwake = false;
            cgVideo.isLooping = false;
            cgVideo.skipOnDrop = true;
            cgVideo.audioOutputMode = VideoAudioOutputMode.None;
            cgVideo.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            cgVideo.renderMode = VideoRenderMode.RenderTexture;
            return cgVideo;
        }

        void StopCgVideo()
        {
            if (cgVideo == null) return;
            cgVideo.Stop();
            cgVideo.clip = null;
            ReleaseCgTarget();
        }

        void ReleaseCgTarget()
        {
            var target = cgVideo.targetTexture;
            if (target == null) return;
            cgVideo.targetTexture = null;
            target.Release();
            Object.Destroy(target);
        }

        // The room "deleted like files, block by block" (剧本场景4). Between the blocks the room itself
        // glitches out: a grab of it, its colours split and its rows torn further the more is gone.
        async Awaitable GlitchSwap(SceneDef def)
        {
            const int cols = 16, rows = 9;
            var room = await FxSurface.Grab(fx, game.Frame);
            var grid = Ui.El("fx-blocks", fx);
            Ui.Fill(grid);
            var blocks = new List<VisualElement>();
            for (var i = 0; i < cols * rows; i++)
            {
                var b = Ui.El("fx-block", grid);
                b.style.left = Length.Percent(100f * (i % cols) / cols);
                b.style.top = Length.Percent(100f * (i / cols) / rows);
                b.style.width = Length.Percent(100f / cols + 0.2f);
                b.style.height = Length.Percent(100f / rows + 0.2f);
                b.style.opacity = 0;
                blocks.Add(b);
            }
            var order = new List<int>();
            for (var i = 0; i < blocks.Count; i++) order.Add(i);
            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var e = epoch;
            game.Audio.Sfx("glitch");
            var shown = 0;
            var calm = game.Settings.reduceFlash;
            await Anim.Tween(0.7f, t =>
            {
                var target = Mathf.RoundToInt(t * blocks.Count);
                for (; shown < target; shown++) blocks[order[shown]].style.opacity = 1;
                room?.Set("_Split", 30f * t).Glitch(calm ? 0f : 90f * t, 0.05f).Set("_Pixel", Mathf.Lerp(1f, 20f, t * t))
                    .Set("_Desat", 0.7f * t).Set("_Scan", 0.6f * t);
            }, Ease.Linear);
            room?.Element.RemoveFromHierarchy();
            if (e != epoch)
            {
                grid.RemoveFromHierarchy();
                return;
            }
            ShowScene(def);
            game.Audio.Sfx("water", 0.6f);
            var hiddenCount = 0;
            await Anim.Tween(0.6f, t =>
            {
                var target = Mathf.RoundToInt(t * blocks.Count);
                for (; hiddenCount < target; hiddenCount++) blocks[order[hiddenCount]].style.opacity = 0;
            }, Ease.Linear);
            grid.RemoveFromHierarchy();
        }

        // <<effort_max 角色 形态ID>>: the reasoning-effort slider goes 低 → 中 → 高 → Max, then the form is named.
        public async Awaitable Awakening(string who, string formId)
        {
            HideAllLines();
            Stage.Remove("影子" + who);
            var formName = GameData.AwakenedForms.TryGetValue(formId, out var n) ? n : formId;
            var def = GameData.Character(who);
            var color = def?.Color ?? Palette.Sea;

            var layer = Ui.El("fx-awaken", fx);
            Ui.Fill(layer);
            layer.style.opacity = 0;
            // Falling down the data sea behind it all, in her colour, speed lines and light streaks rushing
            // through.
            var sea = new FxSurface(layer, Ui.Tex("Art/Transitions/data_sea"), "fx-awaken__sea");
            sea.Rect(Transitions.SeaRect).Set("_Tint", Color.Lerp(Color.white, color, 0.4f) * 0.75f).Set("_Scan", 0.35f).Set("_Vignette", 0.9f).Set("_Blur", 0.25f)
                .Set("_Fly", 0.6f).Set("_Streaks", 0.7f).Set("_StreakSpeed", 1.1f).Set("_StreakColor", Color.Lerp(Color.white, color, 0.5f))
                .Overlay(Ui.Tex("Art/Transitions/beams"), 0.5f, new Vector4(0f, 0f, 1f, 1f), new Vector2(-0.8f, 0f));
            var born = Time.unscaledTime;
            sea.Element.schedule.Execute(() => sea.Set("_Spin", (Time.unscaledTime - born) * 0.1f)).Every(16);
            var dots = new Halftone(color.WithAlpha(0.35f), 22f, 8f, false);
            Ui.Fill(dots);
            layer.Add(dots);

            var panel = Ui.El("fx-effort", layer);
            Ui.Text("思考强度", "fx-effort__label", panel, FontKind.MonoBold);
            var track = Ui.El("fx-effort__track", panel);
            var fill = Ui.El("fx-effort__fill", track);
            var knob = new Shape(Shape.Diamond(), Palette.Paper).Stroke(Palette.Ink, 4).Shadow(new Vector2(5, 5), Palette.Ink);
            knob.AddToClassList("fx-effort__knob");
            track.Add(knob);
            var labels = Ui.El("fx-effort__stops", panel);
            var stopLabels = new List<Label>();
            foreach (var s in EffortStops) stopLabels.Add(Ui.Text(s, "fx-effort__stop", labels, FontKind.Black));

            await Anim.Tween(0.3f, t => layer.style.opacity = t, Ease.Linear);
            for (var i = 0; i < EffortStops.Length; i++)
            {
                var from = i == 0 ? 0f : (i - 1) / 3f;
                var to = i / 3f;
                await Anim.Tween(i == 0 ? 0.01f : 0.32f, t =>
                {
                    var p = Mathf.Lerp(from, to, t) * 100f;
                    fill.style.width = Length.Percent(p);
                    knob.style.left = Length.Percent(p);
                }, Ease.InOutCubic);
                for (var k = 0; k < stopLabels.Count; k++) stopLabels[k].EnableInClassList("is-on", k <= i);
                game.Audio.Sfx(i == EffortStops.Length - 1 ? "slam" : "cursor");
                if (i < EffortStops.Length - 1) await Anim.Wait(0.18f);
            }

            var stamp = Fx.Stamp(layer, "强度 Max", Palette.Green, 120f, -12f, new Vector2(1180, 330));
            Fx.Flash(fx, Color.white);
            Fx.Shake(Root);
            await Anim.Wait(0.7f);

            // The awakened portrait and the form name.
            var hero = Ui.Picture(Ui.Tex("Art/Portraits/deepseek_battle"), ScaleMode.ScaleToFit, "fx-awaken__hero", layer);
            if (who != "DeepSeek") hero.image = def != null ? Ui.Tex(def.PortraitKey(def.DefaultFace)) : null;
            var nameRoot = Fx.Stamp(layer, formName, Palette.Paper, 170f, -6f, new Vector2(660, 760));
            game.Audio.Sfx("rise");
            game.Audio.Sfx("water", 0.7f);
            Fx.Flash(fx, color);
            await Anim.Tween(0.35f, t =>
            {
                hero.style.opacity = t;
                hero.style.translate = new Translate((1 - t) * 500f, 0);
            });
            Stage.Refresh();
            await WaitAdvance(0.8f, 3f);
            await Fx.Dismiss(stamp);
            await Fx.Dismiss(nameRoot);
            await Ui.FadeOutAndRemove(layer, 0.3f);
        }

        // <<coop_rank 角色 等级>>
        public async Awaitable CoopCard(string who, int rank)
        {
            HideAllLines();
            var def = GameData.Character(who);
            if (def == null)
            {
                Debug.LogError($"<<coop_rank {who} {rank}>>：没有这个角色");
                return;
            }
            var layer = Ui.El("fx-coop", fx);
            Ui.Fill(layer);
            var card = CoopCardView.Build(def, rank, layer);
            card.AddToClassList("fx-coop__card");
            game.Audio.Sfx("levelup");
            await Anim.Tween(0.4f, t =>
            {
                layer.style.opacity = Mathf.Clamp01(t * 2f);
                Ui.Turn(card, Mathf.Lerp(-30f, -4f, t));
                Ui.Zoom(card, Mathf.Lerp(0.3f, 1f, t));
            }, Ease.OutBack);
            var stamp = Fx.Stamp(layer, $"RANK {rank}", Palette.Green, 92f, -10f, new Vector2(1330, 640));
            game.Audio.Sfx("slam");
            await WaitAdvance(0.6f, 3f);
            await Fx.Dismiss(stamp);
            await Ui.FadeOutAndRemove(layer, 0.25f);
        }

        // <<stat_up 能力ID 数值>>: 「细心 +1」 slammed over the scene, the line it follows still showing, and the
        // phone pulsing where the abilities are listed (持有物).
        public async Awaitable StatUp(string label, int by)
        {
            var layer = Ui.El("fx-stat", fx);
            Ui.Fill(layer);
            layer.pickingMode = PickingMode.Ignore;
            game.Audio.Sfx("levelup", 0.7f);
            var stamp = Fx.Stamp(layer, $"{label} +{by}", Palette.Green, 76f, -8f, new Vector2(1380, 300));
            PulsePhone();
            await WaitAdvance(0.4f, 1.6f);
            await Fx.Dismiss(stamp);
            layer.RemoveFromHierarchy();
        }

        // <<deadline_set 期限ID 月 日>>: the countdown slams into the HUD.
        public async Awaitable DeadlineSet()
        {
            RefreshHud();
            game.Audio.Sfx("slam", 0.8f);
            await Anim.Tween(0.3f, t =>
            {
                Ui.Zoom(deadline, Mathf.Lerp(2f, 1f, t));
                deadline.style.opacity = t;
            }, Ease.OutBack);
        }

        public void PulsePhone()
        {
            RefreshHud();
            game.Audio.Sfx("coin", 0.6f);
            Anim.Tween(0.5f, t => Ui.Zoom(phoneButton, 1f + Mathf.Sin(t * Mathf.PI * 3f) * 0.2f * (1 - t)), Ease.Linear).Forget();
        }

        // <<chapter_end 章节ID>>
        public async Awaitable ChapterEnd(string chapterId)
        {
            HideAllLines();
            var title = GameData.Chapters.TryGetValue(chapterId, out var t) ? t : chapterId;
            var layer = Ui.El("fx-chapter", fx);
            Ui.Fill(layer);
            layer.style.opacity = 0;
            Ui.Text(title, "fx-chapter__title", layer, FontKind.Black);
            var d = game.State.NextDeadline();
            if (d != null)
            {
                var hud = GameData.Deadlines.TryGetValue(d.id, out var label) ? label.Hud : d.id;
                Ui.Text($"{hud}还有 {game.State.DaysUntil(d)} 天", "fx-chapter__sub", layer, FontKind.MonoBold);
            }
            await Anim.Tween(0.8f, k => layer.style.opacity = k, Ease.Linear);
            var stamp = Fx.Stamp(layer, "完", Palette.Green, 220f, -8f, new Vector2(1380, 560));
            game.Audio.Sfx("slam");
            await WaitAdvance(1f, 4f);
            await Fx.Dismiss(stamp);
        }
    }
}
