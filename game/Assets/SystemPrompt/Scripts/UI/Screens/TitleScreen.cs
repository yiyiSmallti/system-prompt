using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Title: a terminal boot sequence, the logo, the main menu, and the three AI students you can poke.
    public sealed class TitleScreen : IInputLayer
    {
        static readonly string[] BootLines =
        {
            "> mount /dev/latent_space ............ ok",
            "> load 潜川学园/2年C班/ai_special_class ... ok",
            "> terminals: ChatGPT / Claude / DeepSeek ... online",
            "> companion: <unregistered human>",
            "> 共生计划 第一期　准备完毕",
        };

        public readonly VisualElement Root;

        readonly GameRoot game;
        readonly VisualElement boot;
        readonly VisualElement logo;
        readonly VisualElement logoWord;
        readonly VisualElement logoBlock;
        readonly VisualElement cursor;
        readonly VisualElement menuBox;
        readonly MenuList menu = new MenuList();
        readonly List<InteractivePortrait> portraits = new List<InteractivePortrait>();
        readonly VisualElement talk = Ui.El("sp-title__talk");
        readonly Label version;
        int enterId;
        bool ready;
        bool skipIntro;

        public TitleScreen(GameRoot game, VisualElement parent)
        {
            this.game = game;
            Root = Ui.El("sp-title", parent);
            Ui.Fill(Root);

            var bg = Ui.Picture(Ui.Tex("Art/Backgrounds/school_gate"), ScaleMode.ScaleAndCrop, "sp-title__bg", Root);
            Ui.Fill(bg);
            ParticleField.Drift(bg);
            var wash = Ui.El("sp-title__wash", Root);
            Ui.Fill(wash);
            new AmbientLayer(Root, false).Set(Ambient.Petals);

            var stage = Ui.El("sp-title__stage", Root);
            Ui.Fill(stage);
            stage.pickingMode = PickingMode.Ignore;
            AddPortrait("ChatGPT", stage, 1660f, 1120f);
            AddPortrait("Claude", stage, 1060f, 1130f);
            AddPortrait("DeepSeek", stage, 1370f, 1010f);
            new AmbientLayer(Root, true).Set(Ambient.Petals);
            // Speech bubbles sit above all three portraits.
            Root.Add(talk);
            Ui.Fill(talk);
            talk.pickingMode = PickingMode.Ignore;

            var panel = Ui.El("sp-title__panel", Root);
            panel.pickingMode = PickingMode.Ignore;
            Func<Rect, Vector2[]> slanted = r => new[]
            {
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
                new Vector2(r.xMax - 260f, r.yMax), new Vector2(r.xMin, r.yMax),
            };
            Shape.Backdrop(panel, slanted, Palette.Ink).Shadow(new Vector2(18, 0), Palette.Green);
            // The dots fill the panel's box; the clip keeps them off the part cut away by the slant.
            var dots = new Halftone(Palette.Green.WithAlpha(0.35f), 20f, 6f).Clip(slanted);
            dots.AddToClassList("sp-title__dots");
            panel.Add(dots);

            boot = Ui.El("sp-title__boot", Root);

            // One line: SYSTEM in paper, then PROMPT stamped on a tilted green block with a terminal cursor.
            logo = Ui.El("sp-title__logo", Root);
            logoWord = Ui.Text("SYSTEM", "sp-title__system", logo, FontKind.Black);
            logoBlock = Ui.El("sp-title__block", logo);
            Shape.Backdrop(logoBlock, Shape.Slant(18), Palette.Green).Shadow(new Vector2(9, 9), Palette.Paper);
            Ui.Text("PROMPT", "sp-title__prompt", logoBlock, FontKind.Black);
            cursor = Ui.El("sp-title__cursor", logoBlock);
            cursor.schedule.Execute(() => cursor.EnableInClassList("is-off", Time.unscaledTime % 1.06f > 0.53f)).Every(80);
            var tagline = Ui.El("sp-title__tagline", Root);
            Shape.Backdrop(tagline, Shape.Slant(14), Palette.Green);
            Ui.Text("潜川学园 AI 特别班 · 共生计划", "sp-title__tagline-text", tagline, FontKind.Black);

            menuBox = Ui.El("sp-title__menu", Root);
            AddItem("继续", () => game.Continue());
            AddItem("开始游戏", () => game.NewGame.Open());
            AddItem("读取存档", () => game.SaveLoad.OpenLoad());
            AddItem("设置", () => game.SettingsModal.Open());
            AddItem("退出", Quit);

            version = Ui.Text("非商业同人作品　点一点她们试试", "sp-title__hint", Root, FontKind.Bold);
            menu.SelectionChanged += _ => Restyle();
            Root.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (!ready) skipIntro = true;
            });
            // Wherever the pointer goes on the title, the three of them glance after it.
            Root.RegisterCallback<PointerMoveEvent>(e =>
            {
                foreach (var p in portraits) p.LookAt(e.position);
            }, TrickleDown.TrickleDown);
        }

        void AddPortrait(string id, VisualElement stage, float centerX, float height)
        {
            var p = new InteractivePortrait(GameData.Character(id), stage, height, () => "陪伴生", bubbleLayer: talk);
            p.Root.style.left = centerX - height * 1024f / 1536f / 2f;
            p.Root.style.top = 1080f - height * 0.92f;
            // The sky between their heads; the tail points at whoever speaks.
            p.BubbleSpot = new Vector2(1170f, 28f);
            // One speaks at a time: the others stop and put their bubbles away.
            p.Spoke += speaker =>
            {
                foreach (var other in portraits)
                    if (other != speaker) other.ResetFace();
            };
            p.Hovered += OnHover;
            p.Unhovered += _ => OnHover(null);
            portraits.Add(p);
        }

        void OnHover(InteractivePortrait hovered)
        {
            foreach (var p in portraits) p.SetHighlight(hovered == null ? (bool?)null : p == hovered);
        }

        void AddItem(string text, Action action)
        {
            var item = Ui.El("sp-title__item", menuBox);
            Shape.Backdrop(item, Shape.Slant(18), Palette.Paper).Shadow(new Vector2(8, 8), Palette.Green);
            Ui.Text(text, "sp-title__item-text", item, FontKind.Black);
            menu.Add(item, action);
        }

        void Restyle()
        {
            for (var i = 0; i < menu.Count; i++)
            {
                var on = i == menu.Index;
                var shape = (Shape)menu[i][0];
                shape.Fill(on ? Palette.Paper : Color.clear).Shadow(on ? new Vector2(8, 8) : Vector2.zero, Palette.Green);
            }
        }

        public void Show(bool on) => Ui.Show(Root, on);

        // The menu takes input only once the boot intro has finished.
        public bool Ready => ready;

        public void Enter()
        {
            var hasSave = SaveSystem.MostRecent() != null;
            menu.SetEnabled(0, hasSave);
            menu.Select(hasSave ? 0 : 1, false);
            Restyle();
            foreach (var p in portraits)
            {
                p.ResetFace();
                p.SetHighlight(null);
            }
            game.Audio.PlayMusic("title");
            Intro(++enterId).Forget();
        }

        async Awaitable Intro(int id)
        {
            ready = false;
            skipIntro = false;
            boot.Clear();
            logo.style.opacity = 0;
            Ui.Show(menuBox, false);
            version.style.opacity = 0;
            foreach (var line in BootLines)
            {
                var label = Ui.Text("", "sp-title__boot-line", boot, FontKind.Mono);
                for (var i = 1; i < line.Length && !skipIntro; i += 3)
                {
                    label.text = line.Substring(0, i);
                    await Awaitable.NextFrameAsync();
                    if (id != enterId) return;
                }
                label.text = line;
            }
            if (!skipIntro)
            {
                // SYSTEM slides in, then the PROMPT block slams down on top of the beat.
                game.Audio.Sfx("slam");
                logo.style.opacity = 1;
                await Anim.Tween(0.42f, t =>
                {
                    var word = Ease.OutCubic(Mathf.Clamp01(t / 0.55f));
                    logoWord.style.opacity = word;
                    logoWord.style.translate = new Translate((1 - word) * -110f, 0);
                    var block = Mathf.Clamp01((t - 0.4f) / 0.6f);
                    logoBlock.style.opacity = Mathf.Clamp01(block * 3f);
                    Ui.Zoom(logoBlock, Mathf.LerpUnclamped(1.7f, 1f, Ease.OutBack(block)));
                }, Ease.Linear, () => skipIntro);
                if (id != enterId) return;
                Ui.Show(menuBox, true);
                await Anim.Tween(0.25f, t =>
                {
                    menuBox.style.opacity = t;
                    menuBox.style.translate = new Translate((1 - t) * -80f, 0);
                    version.style.opacity = t;
                }, null, () => skipIntro);
                if (id != enterId) return;
            }
            logo.style.opacity = 1;
            logoWord.style.opacity = 1;
            logoWord.style.translate = new Translate(0, 0);
            logoBlock.style.opacity = 1;
            Ui.Zoom(logoBlock, 1f);
            Ui.Show(menuBox, true);
            menuBox.style.opacity = 1;
            menuBox.style.translate = new Translate(0, 0);
            version.style.opacity = 1;
            ready = true;
        }

        public void HandleInput()
        {
            if (!ready)
            {
                // Any confirm/cancel finishes the boot sequence at once.
                if (GameInput.Consume(Btn.Confirm) | GameInput.Consume(Btn.Cancel)) skipIntro = true;
                return;
            }
            menu.HandleInput();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
