using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Pause menu during the story and battles, after Persona 4 Revival's: a paper line cuts across, a green
    // paint stroke swipes in from the left, the companion's close-up slides in on the right, and every item
    // assembles from letters that fly in one by one and come to rest a little crooked.
    // Opening a page (存档, 设置…) scatters the letters and dives into the close-up while the page's band
    // sweeps in; when the page closes the camera pulls back out and the letters land again.
    public sealed class GameMenu : Modal
    {
        const int SaveIndex = 1;
        const float ArtTurn = -12f;
        const float DiveZoom = 3.4f;
        const float ItemStagger = 0.035f;
        const float LetterStagger = 0.03f;
        const float LandSeconds = 0.24f;
        const float ExitSeconds = 0.22f;
        const int MaxLetters = 4;

        sealed class Glyph
        {
            public Label Label;
            public float Turn, Scale, Lift; // resting pose
            public Vector3 From;            // scattered pose: offset x, y and turn
        }

        readonly MenuList menu = new MenuList { Styler = StyleItem };
        readonly List<List<Glyph>> glyphs = new List<List<Glyph>>();
        readonly List<Label> tags = new List<Label>();
        readonly VisualElement art;
        readonly Image artImage;
        readonly Image artShadow;
        readonly Shape splash;
        readonly Shape splashSmall;
        readonly VisualElement slash;
        readonly VisualElement info;
        readonly Label date;
        readonly Label place;
        readonly Label chapter;
        readonly Label playTime;
        readonly Label deadline;
        int motion;
        bool moving;
        bool dived;

        // Diving into a page or pulling back out of one.
        public bool Switching { get; private set; }

        protected override bool OwnEntrance => true;
        protected override float OwnExit => ExitSeconds;

        public GameMenu(GameRoot game) : base(game, "sp-gamemenu", decorated: false)
        {
            art = Ui.El("sp-gamemenu__art", Root);
            artShadow = Ui.Picture(null, ScaleMode.ScaleToFit, "sp-gamemenu__art-image is-shadow", art);
            artShadow.tintColor = Palette.Ink;
            artImage = Ui.Picture(null, ScaleMode.ScaleToFit, "sp-gamemenu__art-image", art);

            splashSmall = new Shape(Shape.Brush(23), Palette.Sea);
            splashSmall.AddToClassList("sp-gamemenu__splash");
            splashSmall.AddToClassList("is-small");
            Root.Add(splashSmall);
            splash = new Shape(Shape.Brush(7), Palette.Green);
            splash.AddToClassList("sp-gamemenu__splash");
            Root.Add(splash);
            slash = Ui.El("sp-gamemenu__slash", Root);

            var items = Ui.El("sp-gamemenu__items", Root);
            Add(items, "继续", "RESUME", Close);
            Add(items, "存档", "SAVE", () => Dive(() => Game.SaveLoad.OpenSave()));
            Add(items, "读档", "LOAD", () => Dive(() => Game.SaveLoad.OpenLoad()));
            Add(items, "手机", "PHONE", () => Dive(() => Game.Phone.Open()));
            Add(items, "设置", "CONFIG", () => Dive(() => Game.SettingsModal.Open()));
            Add(items, "回看", "LOG", () => Dive(() => Game.Backlog.Open()));
            Add(items, "回到标题", "TITLE", () => Game.Confirm.Ask("回到标题画面？\n上次存档之后的进度会丢失。", () => Game.ShowTitle()));
            menu.SelectionChanged += i => Pop(i).Forget();

            info = Ui.El("sp-gamemenu__info", Root);
            Shape.Backdrop(info, Shape.Slant(30), Palette.Ink.WithAlpha(0.94f)).Shadow(new Vector2(14, 14), Palette.Green);
            date = Ui.Text("", "sp-gamemenu__date", info, FontKind.Black);
            place = Ui.Text("", "sp-gamemenu__place", info, FontKind.Bold);
            chapter = Ui.Text("", "sp-gamemenu__chapter", info, FontKind.Bold);
            deadline = Ui.Text("", "sp-gamemenu__deadline", info, FontKind.MonoBold);
            playTime = Ui.Text("", "sp-gamemenu__time", info, FontKind.MonoBold);
            ModalParts.Hints(Root, "↑↓ 选择　Enter 确定　Esc 返回");
        }

        void Add(VisualElement parent, string text, string tag, Action action)
        {
            var index = parent.childCount;
            var item = Ui.El("sp-gamemenu__item", parent);
            item.style.marginLeft = index * 26f;
            Shape.Backdrop(item, Shape.Slant(26), Color.clear).AddToClassList("sp-gamemenu__item-band");
            var word = Ui.El("sp-gamemenu__word", item);
            var letters = new List<Glyph>();
            for (var j = 0; j < text.Length; j++)
            {
                // Ransom-note rest pose, fixed per letter: a few degrees off, the first letter of each item larger.
                var h = (index * 7 + j * 13) % 5 - 2;
                letters.Add(new Glyph
                {
                    Label = Ui.Text(text[j].ToString(), "sp-gamemenu__item-text", word, FontKind.Black),
                    Turn = h * 2.6f,
                    Lift = ((index + j * 3) % 3 - 1) * 4f,
                    Scale = j == 0 ? 1.14f : 1f - Mathf.Abs(h) * 0.03f,
                });
            }
            glyphs.Add(letters);
            tags.Add(Ui.Text(tag, "sp-gamemenu__item-tag", item, FontKind.MonoBold));
            menu.Add(item, action);
        }

        static void StyleItem(VisualElement item, bool on)
        {
            var shape = item.Q<Shape>();
            shape.Fill(on ? Palette.Paper : Color.clear).Shadow(on ? new Vector2(10, 10) : Vector2.zero, Palette.Green);
            Ui.Turn(item, on ? -3f : 0f);
        }

        protected override void OnOpen()
        {
            var s = Game.State;
            date.text = $"{s.DateLabel}　{GameData.SlotLabel(s.slot)}";
            place.text = GameData.SceneLabel(s.scene);
            chapter.text = GameData.Chapters.TryGetValue(s.chapter ?? "", out var c) ? c : "";
            var d = s.NextDeadline();
            deadline.text = d == null ? "" : $"{(GameData.Deadlines.TryGetValue(d.id, out var l) ? l.Hud : d.id)}还有 {s.DaysUntil(d)} 天";
            playTime.text = "PLAY " + FormatTime(Game.PlaySeconds);
            menu.SetEnabled(SaveIndex, Game.CanSave);
            dived = false;
            Switching = false;
            ShowFeatured();
            Game.Audio.Sfx("whoosh", 0.6f);
            Enter(++motion).Forget();
            menu.Select(0, false);
        }

        protected override void OnClose()
        {
            Switching = false;
            Leave(++motion).Forget();
        }

        // A confirm box closing changes nothing; a page closing pulls back out of the dive.
        protected override void OnRegain()
        {
            if (!dived) return;
            dived = false;
            Surface(++motion).Forget();
        }

        // The companion in the party, else whoever the player met last.
        void ShowFeatured()
        {
            var s = Game.State;
            CharacterDef featured = null;
            foreach (var id in s.party)
                if (featured == null && GameData.Character(id) is CharacterDef d && d.HasPortrait) featured = d;
            for (var i = s.met.Count - 1; featured == null && i >= 0; i--)
                if (GameData.Character(s.met[i]) is CharacterDef d && d.HasPortrait) featured = d;
            Ui.Show(art, featured != null);
            if (featured == null) return;
            var picture = Ui.Tex(featured.PortraitKey(featured.DefaultFace));
            artImage.image = picture;
            artShadow.image = picture;
            splashSmall.Fill(featured.Color);
        }

        public static string FormatTime(float seconds)
        {
            var t = (int)seconds;
            return $"{t / 3600:00}:{t / 60 % 60:00}:{t % 60:00}";
        }

        public override void HandleInput()
        {
            if (dived) return; // the page is about to open on top
            if (GameInput.Consume(Btn.Cancel) || GameInput.Consume(Btn.Menu))
            {
                Game.Audio.Sfx("cursor");
                Close();
                return;
            }
            menu.HandleInput();
        }

        // ------------------------------------------------------------------ motion

        async Awaitable Enter(int id)
        {
            Scatter();
            var total = LandEnd(0.06f);
            await Play(id, total, time =>
            {
                slash.style.scale = new Scale(new Vector2(Ease.OutCubic(Mathf.Clamp01(time / 0.14f)), 1f));
                slash.style.opacity = 1 - Mathf.Clamp01((time - 0.22f) / 0.24f);
                PoseSplashes(Ease.OutCubic(Mathf.Clamp01(time / 0.24f)));
                var a = Ease.OutCubic(Mathf.Clamp01(time / 0.32f));
                PoseArt(1 - a, 1f, a);
                LandGlyphs(time, 0.06f);
                PoseInfo(Ease.OutCubic(Mathf.Clamp01((time - 0.12f) / 0.26f)));
            });
        }

        async Awaitable Leave(int id)
        {
            Scatter();
            await Play(id, ExitSeconds, time =>
            {
                LeaveGlyphs(time);
                var k = 1 - Ease.InCubic(time / ExitSeconds);
                PoseSplashes(k);
                PoseInfo(k);
                PoseArt(1 - k, 1f, k);
            });
        }

        // Scatter the menu and push into the close-up; the page opens a moment in so its band sweeps
        // across the zoom.
        void Dive(Action openPage)
        {
            if (dived) return;
            dived = true;
            Switching = true;
            DiveIn(++motion, openPage).Forget();
        }

        async Awaitable DiveIn(int id, Action openPage)
        {
            Scatter();
            var opened = false;
            await Play(id, 0.34f, time =>
            {
                LeaveGlyphs(time);
                var k = 1 - Ease.InCubic(Mathf.Clamp01(time / 0.18f));
                PoseSplashes(k);
                PoseInfo(k);
                PoseArt(0f, Mathf.Lerp(1f, DiveZoom, Ease.InOutCubic(time / 0.34f)), 1 - Mathf.Clamp01((time - 0.22f) / 0.12f));
                if (opened || time < 0.1f) return;
                opened = true;
                openPage();
            });
        }

        async Awaitable Surface(int id)
        {
            Switching = true;
            Scatter();
            var total = LandEnd(0.1f);
            await Play(id, total, time =>
            {
                PoseArt(0f, Mathf.Lerp(DiveZoom, 1f, Ease.OutCubic(Mathf.Clamp01(time / 0.34f))), Mathf.Clamp01(time / 0.12f));
                PoseSplashes(Ease.OutCubic(Mathf.Clamp01((time - 0.06f) / 0.22f)));
                LandGlyphs(time, 0.1f);
                PoseInfo(Ease.OutCubic(Mathf.Clamp01((time - 0.14f) / 0.24f)));
            });
            if (id == motion) Switching = false;
        }

        // Runs step(seconds elapsed) each frame until done or until another motion takes over.
        async Awaitable Play(int id, float seconds, Action<float> step)
        {
            moving = true;
            await Anim.Tween(seconds, t =>
            {
                if (id == motion) step(t * seconds);
            }, Ease.Linear, () => id != motion);
            if (id == motion) moving = false;
        }

        // The selected item's letters jump and settle when the cursor lands on it.
        async Awaitable Pop(int index)
        {
            if (moving) return;
            var letters = glyphs[index];
            var id = motion;
            await Anim.Tween(0.18f, t =>
            {
                if (id != motion || moving) return;
                var k = Ease.OutBack(t);
                for (var j = 0; j < letters.Count; j++)
                {
                    var g = letters[j];
                    Ui.Zoom(g.Label, Mathf.LerpUnclamped(g.Scale * 1.3f, g.Scale, k));
                    Ui.Turn(g.Label, g.Turn + (1 - k) * (j % 2 == 0 ? -14f : 14f));
                }
            }, Ease.Linear);
        }

        void PoseSplashes(float k)
        {
            splash.style.scale = new Scale(new Vector2(k, 1f));
            splashSmall.style.scale = new Scale(new Vector2(Mathf.Clamp01(k * 1.3f - 0.3f), 1f));
        }

        void PoseInfo(float k)
        {
            info.style.translate = new Translate((1 - k) * 320f, 0);
            info.style.opacity = k;
        }

        // slide: 0 = in place, 1 = off to the right. zoom pushes in on the face.
        void PoseArt(float slide, float zoom, float opacity)
        {
            art.style.translate = new Translate(slide * 420f, 0);
            Ui.Turn(art, ArtTurn - slide * 10f);
            Ui.Zoom(art, zoom);
            art.style.opacity = opacity;
        }

        // Fresh flight paths for the next landing or leaving.
        void Scatter()
        {
            foreach (var letters in glyphs)
            foreach (var g in letters)
            {
                var direction = UnityEngine.Random.insideUnitCircle.normalized;
                var distance = UnityEngine.Random.Range(220f, 520f);
                g.From = new Vector3(direction.x * distance, direction.y * distance, UnityEngine.Random.Range(-140f, 140f));
            }
        }

        float LandEnd(float start) => start + (glyphs.Count - 1) * ItemStagger + (MaxLetters - 1) * LetterStagger + LandSeconds;

        void LandGlyphs(float time, float start)
        {
            for (var i = 0; i < glyphs.Count; i++)
            {
                var itemTime = time - start - i * ItemStagger;
                for (var j = 0; j < glyphs[i].Count; j++)
                    PoseGlyph(glyphs[i][j], Ease.OutBack(Mathf.Clamp01((itemTime - j * LetterStagger) / LandSeconds)));
                PoseItem(i, Ease.OutCubic(Mathf.Clamp01((itemTime - 0.08f) / 0.2f)));
            }
        }

        void LeaveGlyphs(float time)
        {
            for (var i = 0; i < glyphs.Count; i++)
            {
                var itemTime = time - i * 0.012f;
                for (var j = 0; j < glyphs[i].Count; j++)
                    PoseGlyph(glyphs[i][j], 1 - Ease.InCubic(Mathf.Clamp01((itemTime - j * 0.01f) / 0.16f)));
                PoseItem(i, 1 - Mathf.Clamp01(itemTime / 0.1f));
            }
        }

        // k: 0 = scattered, 1 = resting (OutBack overshoots past 1 a little).
        static void PoseGlyph(Glyph g, float k)
        {
            var away = 1 - k;
            g.Label.style.translate = new Translate(g.From.x * away, g.From.y * away + g.Lift * k);
            Ui.Turn(g.Label, g.From.z * away + g.Turn * k);
            Ui.Zoom(g.Label, Mathf.LerpUnclamped(1.9f, g.Scale, k));
            g.Label.style.opacity = Mathf.Clamp01(k * 2.5f);
        }

        // The tag and the selected item's paper band follow the letters in.
        void PoseItem(int i, float k)
        {
            tags[i].style.opacity = k;
            tags[i].style.translate = new Translate((1 - k) * -50f, 0);
            menu[i].Q<Shape>().style.scale = new Scale(new Vector2(k, 1f));
        }
    }
}
