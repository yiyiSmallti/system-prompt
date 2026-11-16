using System;
using System.Collections.Generic;
using UnityEngine;
using SystemPrompt.Dungeon;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // 存档 / 读档, laid out like a photo album: the selected slot's scene as a big snapshot on the left,
    // the slots as cards on the right. It opens with a camera shutter (the blades meet top and bottom for
    // SAVE, left and right for LOAD), and saving fires the shutter again on the snapshot.
    // Slot 0 is the quick save (F5), which can be loaded here but only written with F5.
    // Saves are checkpoints from the start of the current story node, which the slot text says.
    public sealed class SaveLoadModal : Modal
    {
        const float ShutterClose = 0.12f;
        const float ShutterOpen = 0.2f;
        const float CardStagger = 0.035f;
        const int CascadeCards = 8; // about what the list shows; the rest start in place
        const float ExitSeconds = 0.22f;

        readonly VisualElement stage;
        readonly Label word;
        readonly Shape band;
        readonly VisualElement heading;
        readonly Label title;
        readonly Label titleTag;
        readonly Shape titleTagShape;
        readonly VisualElement photo;
        readonly Shape tape;
        readonly Image photoImage;
        readonly VisualElement photoEmpty;
        readonly VisualElement photoFlash;
        readonly VisualElement info;
        readonly Label bigDate;
        readonly Label slotTime;
        readonly Label chapter;
        readonly Label meta;
        readonly Label hints;
        readonly ScrollView scroll;
        readonly Shape[] blades = new Shape[2];
        readonly MenuList slots = new MenuList { Wrap = false };
        readonly List<SaveSlotInfo> infos = new List<SaveSlotInfo>();
        bool saving;
        int swap;

        Color Accent => saving ? Palette.Green : Palette.Sea;

        protected override bool OwnEntrance => true;
        protected override float OwnExit => ExitSeconds;

        public SaveLoadModal(GameRoot game) : base(game, "sp-snap", decorated: false)
        {
            slots.Styler = StyleSlot;
            stage = Ui.El("sp-snap__stage", Root);
            Ui.Fill(stage);
            var dots = new Halftone(Palette.Paper.WithAlpha(0.07f), 22f, 6f, false);
            dots.AddToClassList("sp-snap__dots");
            stage.Add(dots);
            word = Ui.Text("", "sp-snap__word", stage, FontKind.Black);
            band = new Shape(Shape.Slant(160), Palette.Green);
            band.AddToClassList("sp-snap__band");
            stage.Add(band);

            heading = Ui.El("sp-snap__heading", stage);
            title = Ui.Text("", "sp-snap__title", heading, FontKind.Black);
            var tag = Ui.El("sp-snap__tag", heading);
            titleTagShape = Shape.Backdrop(tag, Shape.Slant(12), Palette.Green);
            titleTag = Ui.Text("", "sp-snap__tag-text", tag, FontKind.MonoBold);

            // The snapshot: a crooked paper print with a strip of tape, the scene inside.
            photo = Ui.El("sp-snap__photo", stage);
            Shape.Backdrop(photo, Shape.Jag(5, 16f), Palette.Paper).Shadow(new Vector2(18, 18), Palette.Ink.WithAlpha(0.9f));
            var window = Ui.El("sp-snap__window", photo);
            photoImage = Ui.Picture(null, ScaleMode.ScaleAndCrop, "sp-snap__image", window);
            Ui.Fill(photoImage);
            photoEmpty = Ui.El("sp-snap__empty", window);
            Ui.Fill(photoEmpty);
            var noise = new Halftone(Palette.Paper.WithAlpha(0.18f), 10f, 3f);
            Ui.Fill(noise);
            photoEmpty.Add(noise);
            Ui.Text("NO DATA", "sp-snap__empty-text", photoEmpty, FontKind.MonoBold);
            photoFlash = Ui.El("sp-snap__flash", window);
            Ui.Fill(photoFlash);
            tape = new Shape(Shape.Jag(9, 5f), Palette.Green.WithAlpha(0.85f));
            tape.AddToClassList("sp-snap__tape");
            photo.Add(tape);

            info = Ui.El("sp-snap__info", stage);
            var dateRow = Ui.El("sp-snap__date-row", info);
            bigDate = Ui.Text("", "sp-snap__date", dateRow, FontKind.Black);
            slotTime = Ui.Text("", "sp-snap__time", dateRow, FontKind.Black);
            chapter = Ui.Text("", "sp-snap__chapter", info, FontKind.Bold);
            meta = Ui.Text("", "sp-snap__meta", info, FontKind.MonoBold);

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("sp-snap__list");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            stage.Add(scroll);
            hints = Ui.Text("", "sp-snap__hints", stage, FontKind.MonoBold);
            slots.SelectionChanged += i =>
            {
                scroll.ScrollTo(slots[i]);
                ShowPreview(infos[i], true);
            };

            for (var i = 0; i < blades.Length; i++)
            {
                blades[i] = new Shape(Shape.Box(), Palette.Ink).Stroke(Palette.Green, 10);
                blades[i].AddToClassList("sp-snap__blade");
                Root.Add(blades[i]);
            }
        }

        public void OpenSave()
        {
            saving = true;
            Open();
        }

        public void OpenLoad()
        {
            saving = false;
            Open();
        }

        protected override void OnOpen()
        {
            Root.EnableInClassList("is-load", !saving);
            word.text = saving ? "SAVE" : "LOAD";
            title.text = saving ? "存档" : "读档";
            titleTag.text = saving ? "SAVE DATA" : "LOAD DATA";
            titleTagShape.Fill(Accent);
            band.Fill(Accent);
            tape.Fill(Accent.WithAlpha(0.85f));
            hints.text = saving ? "Enter 存到这里　Delete 删除　Esc 返回　（存档从本节开头继续）" : "Enter 读取　Delete 删除　Esc 返回";
            Rebuild(saving ? 1 : -1);
            ShapeBlades();
            Enter().Forget();
        }

        protected override void OnClose() => Leave().Forget();

        void Rebuild(int select)
        {
            scroll.Clear();
            slots.Clear();
            infos.Clear();
            infos.AddRange(SaveSystem.ReadAll());
            for (var i = 0; i < infos.Count; i++)
            {
                var info = infos[i];
                var slot = BuildSlot(info);
                scroll.Add(slot);
                var usable = saving ? info.Slot != SaveSystem.QuickSlot : info.Data != null;
                var index = i;
                slots.Add(slot, () => Choose(index), usable);
            }
            if (select < 0)
            {
                var latest = SaveSystem.MostRecent();
                select = latest != null ? latest.Slot : 0;
            }
            if (select < slots.Count && (saving ? select != SaveSystem.QuickSlot : infos[select].Data != null)) slots.Select(select, false);
            ShowPreview(slots.Index >= 0 ? infos[slots.Index] : null, false);
        }

        // The scene, or for a save at a beacon the area it goes back into.
        static string Place(SaveData data)
        {
            var area = data.InDungeon ? Dungeons.Get(data.dungeon.area) : null;
            return area != null ? $"{area.Area}·{area.Layer}　信标" : GameData.SceneLabel(data.state.scene);
        }

        VisualElement BuildSlot(SaveSlotInfo info)
        {
            var slot = Ui.El("sp-snapcard");
            slot.EnableInClassList("is-empty", info.IsEmpty);
            Shape.Backdrop(slot, Shape.Jag(info.Slot * 17 + 3, 12f), Color.clear);
            var quick = info.Slot == SaveSystem.QuickSlot;
            var number = Ui.El("sp-snapcard__number", slot);
            Ui.Text(quick ? "Q" : $"{info.Slot:00}", "sp-snapcard__number-text", number, FontKind.Black);
            if (quick) Ui.Text("QUICK", "sp-snapcard__quick", number, FontKind.MonoBold);

            var texts = Ui.El("sp-snapcard__texts", slot);
            var data = info.Data;
            if (data != null)
            {
                var s = data.state;
                Ui.Text($"{s.DateLabel}　{GameData.SlotLabel(s.slot)}", "sp-snapcard__date", texts, FontKind.Black);
                Ui.Text(Place(data), "sp-snapcard__place", texts, FontKind.Bold);
                Ui.Text($"PLAY {GameMenu.FormatTime(data.playSeconds)}　{data.SavedAtLocal:yyyy/MM/dd HH:mm}", "sp-snapcard__meta", texts, FontKind.MonoBold);
            }
            else if (info.Error != null)
            {
                Ui.Text(info.Error, "sp-snapcard__error", texts, FontKind.Bold);
            }
            else
            {
                Ui.Text(quick ? "快速存档位（F5）" : "NO DATA", "sp-snapcard__none", texts, FontKind.MonoBold);
            }

            if (!info.IsEmpty)
            {
                var delete = Ui.Text("删除", "sp-snapcard__delete", slot, FontKind.Bold);
                var slotNumber = info.Slot;
                delete.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    AskDelete(slotNumber);
                });
            }
            return slot;
        }

        // The selected card turns into a paper print with a hard shadow in the page's colour and slides
        // out toward the snapshot; empty slots are only outlined.
        void StyleSlot(VisualElement item, bool on)
        {
            var shape = item.Q<Shape>();
            var empty = item.ClassListContains("is-empty");
            shape.Fill(on ? Palette.Paper : empty ? Color.clear : new Color(0.1f, 0.1f, 0.13f))
                .Stroke(Palette.Paper.WithAlpha(on ? 0f : empty ? 0.3f : 0.12f), 2)
                .Shadow(on ? new Vector2(12, 12) : Vector2.zero, Accent);
            item.Q<Label>(className: "sp-snapcard__number-text").style.color = on ? new StyleColor(Accent) : new StyleColor(StyleKeyword.Null);
            item.style.translate = new Translate(on ? -40f : 0f, 0);
            Ui.Turn(item, on ? -1.5f : 0f);
        }

        void ShowPreview(SaveSlotInfo slot, bool animate)
        {
            var data = slot?.Data;
            var scene = data != null && GameData.Scenes.TryGetValue(data.state.scene ?? "", out var def) ? def : null;
            photoImage.image = scene != null ? Ui.Tex(scene.BackgroundKey) : null;
            Ui.Show(photoEmpty, scene == null);
            if (data != null)
            {
                var s = data.state;
                bigDate.text = s.DateLabel;
                slotTime.text = GameData.SlotLabel(s.slot);
                var chapterName = GameData.Chapters.TryGetValue(s.chapter ?? "", out var c) ? c : "";
                chapter.text = $"{chapterName}　{Place(data)}";
                meta.text = $"{s.playerName}　PLAY {GameMenu.FormatTime(data.playSeconds)}　SAVED {data.SavedAtLocal:yyyy/MM/dd HH:mm}";
            }
            else
            {
                bigDate.text = saving ? "NEW" : "—";
                slotTime.text = "";
                chapter.text = slot == null ? "" : slot.Error ?? (slot.Slot == SaveSystem.QuickSlot ? "快速存档位（F5）" : "空白存档位");
                meta.text = slot == null ? "" : slot.Slot == SaveSystem.QuickSlot ? "SLOT QUICK" : $"SLOT {slot.Slot:00}";
            }
            if (animate) Swap(++swap).Forget();
        }

        // A new print slides into the frame with a short flash.
        async Awaitable Swap(int id)
        {
            await Anim.Tween(0.16f, t =>
            {
                if (id != swap) return;
                photoImage.style.translate = new Translate((1 - t) * 90f, 0);
                photoFlash.style.opacity = (1 - t) * 0.5f;
            }, Ease.OutCubic, () => id != swap);
        }

        // ------------------------------------------------------------------ motion

        async Awaitable Enter()
        {
            var cards = new List<VisualElement>();
            for (var i = 0; i < Math.Min(slots.Count, CascadeCards); i++) cards.Add(slots[i]);
            var flashed = false;
            var total = ShutterClose + 0.1f + cards.Count * CardStagger + 0.24f;
            Game.Audio.Sfx("whoosh", 0.5f);
            await PlayMotion(total, time =>
            {
                // Shutter: closes, flashes, opens.
                var shut = time < ShutterClose
                    ? Ease.InCubic(time / ShutterClose)
                    : 1 - Ease.OutCubic(Mathf.Clamp01((time - ShutterClose - 0.04f) / ShutterOpen));
                PoseBlades(shut);
                stage.style.opacity = time < ShutterClose ? 0f : 1f;
                if (!flashed && time >= ShutterClose)
                {
                    flashed = true;
                    Game.Audio.Sfx("shutter");
                    Fx.Flash(Root, Color.white, 0.22f);
                }
                var open = time - ShutterClose;
                band.style.scale = new Scale(new Vector2(Ease.OutCubic(Mathf.Clamp01(open / 0.22f)), 1f));
                word.style.translate = new Translate((1 - Ease.OutCubic(Mathf.Clamp01(open / 0.5f))) * -260f, 0);
                var h = Ease.OutBack(Mathf.Clamp01((open - 0.06f) / 0.22f));
                heading.style.translate = new Translate((1 - h) * -500f, 0);
                var p = Ease.OutBack(Mathf.Clamp01((open - 0.02f) / 0.3f));
                photo.style.translate = new Translate(0, 0);
                Ui.Zoom(photo, Mathf.Lerp(1.3f, 1f, p));
                Ui.Turn(photo, Mathf.Lerp(9f, -4f, p));
                photo.style.opacity = Mathf.Clamp01(p * 3f);
                var f = Ease.OutCubic(Mathf.Clamp01((open - 0.14f) / 0.22f));
                info.style.translate = new Translate(0, (1 - f) * 60f);
                info.style.opacity = f;
                for (var i = 0; i < cards.Count; i++)
                {
                    var k = Ease.OutBack(Mathf.Clamp01((open - 0.06f - i * CardStagger) / 0.24f));
                    cards[i].style.left = (1 - k) * 900f;
                    cards[i].style.opacity = Mathf.Clamp01(k * 2f);
                }
            });
        }

        async Awaitable Leave()
        {
            var cards = new List<VisualElement>();
            for (var i = 0; i < slots.Count; i++) cards.Add(slots[i]);
            await PlayMotion(ExitSeconds, time =>
            {
                var k = Ease.InCubic(time / ExitSeconds);
                PoseBlades(0f);
                for (var i = 0; i < cards.Count; i++) cards[i].style.left = Ease.InCubic(Mathf.Clamp01(time / ExitSeconds * 1.4f - i * 0.04f)) * 1000f;
                photo.style.translate = new Translate(k * -200f, k * 500f);
                Ui.Turn(photo, -4f - k * 16f);
                heading.style.translate = new Translate(k * -700f, 0);
                info.style.opacity = 1 - k;
                stage.style.opacity = 1 - Mathf.Clamp01((time - 0.1f) / 0.12f);
            });
        }

        // SAVE shuts top and bottom, LOAD left and right; the edges meet on a slant.
        void ShapeBlades()
        {
            foreach (var b in blades) b.Stroke(Accent, 10);
            if (saving)
            {
                blades[0].Outline(r => new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax - 90f), new Vector2(r.xMin, r.yMax) });
                blades[1].Outline(r => new[] { new Vector2(r.xMin, r.yMin + 90f), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) });
            }
            else
            {
                blades[0].Outline(r => new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax - 120f, r.yMax), new Vector2(r.xMin, r.yMax) });
                blades[1].Outline(r => new[] { new Vector2(r.xMin + 120f, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) });
            }
        }

        // shut: 0 = blades off screen, 1 = closed.
        void PoseBlades(float shut)
        {
            var away = 1 - shut;
            if (saving)
            {
                SetBlade(blades[0], 0f, -60f, GameRoot.Width, 660f, 0f, away * -720f);
                SetBlade(blades[1], 0f, 480f, GameRoot.Width, 660f, 0f, away * 720f);
            }
            else
            {
                SetBlade(blades[0], -60f, 0f, 1100f, GameRoot.Height, away * -1160f, 0f);
                SetBlade(blades[1], 880f, 0f, 1100f, GameRoot.Height, away * 1160f, 0f);
            }
            foreach (var b in blades) Ui.Show(b, shut > 0.001f);
        }

        static void SetBlade(VisualElement blade, float left, float top, float width, float height, float dx, float dy)
        {
            blade.style.left = left;
            blade.style.top = top;
            blade.style.width = width;
            blade.style.height = height;
            blade.style.translate = new Translate(dx, dy);
        }

        // ------------------------------------------------------------------ actions

        void Choose(int index)
        {
            var info = infos[index];
            if (saving)
            {
                if (info.IsEmpty) Save(info.Slot);
                else Game.Confirm.Ask($"覆盖存档 {info.Slot:00}？", () => Save(info.Slot));
                return;
            }
            if (info.Data == null) return;
            if (Game.InStory) Game.Confirm.Ask("读取这个存档？\n当前进度会丢失。", () => Load(info));
            else Load(info);
        }

        void Save(int slot)
        {
            if (!Game.SaveTo(slot)) return;
            Game.Audio.Sfx("shutter");
            Rebuild(slot);
            Fx.Flash(Root, Color.white, 0.25f);
            var stamp = Fx.Stamp(stage, "SAVED!", Accent, 110f, -8f, new Vector2(500, 470));
            DismissLater(stamp).Forget();
            Game.Notice($"已存档到 {slot:00}");
        }

        static async Awaitable DismissLater(VisualElement stamp)
        {
            await Anim.Wait(0.9f);
            await Fx.Dismiss(stamp);
        }

        void Load(SaveSlotInfo info)
        {
            Close();
            Game.LoadGame(info.Data);
        }

        void AskDelete(int slot)
        {
            Game.Confirm.Ask($"删除存档 {(slot == SaveSystem.QuickSlot ? "QUICK" : slot.ToString("00"))}？\n删除后无法恢复。", () =>
            {
                try
                {
                    SaveSystem.Delete(slot);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Game.Notice("删除失败");
                    return;
                }
                Game.Audio.Sfx("crack", 0.7f);
                Rebuild(slot);
            });
        }

        public override void HandleInput()
        {
            if (GameInput.Consume(Btn.Cancel) || GameInput.Consume(Btn.Menu))
            {
                Game.Audio.Sfx("cursor");
                Close();
                return;
            }
            if (GameInput.Consume(Btn.Delete) && slots.Index >= 0 && !infos[slots.Index].IsEmpty)
            {
                AskDelete(infos[slots.Index].Slot);
                return;
            }
            slots.HandleInput();
        }
    }
}
