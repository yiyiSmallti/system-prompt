using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // The phone: 日程 (calendar with the deadline), 通讯录 (contacts → profiles), Coop, 持有物 (party, items, abilities).
    // ←→ switch apps, ↑↓ pick inside an app.
    public sealed class PhoneModal : Modal
    {
        public const int CalendarApp = 0, ContactsApp = 1, CoopApp = 2, InventoryApp = 3;
        static readonly string[] AppNames = { "日程", "通讯录", "Coop", "持有物" };
        static readonly string[] WeekHeader = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

        static readonly Dictionary<string, string> SlotClock = new Dictionary<string, string>
        {
            ["morning"] = "07:40", ["class"] = "10:30", ["lunchtime"] = "12:30",
            ["after_school"] = "16:20", ["evening"] = "19:30", ["late_night"] = "02:59",
        };

        readonly VisualElement phone;
        readonly Label clock;
        readonly Label statusDate;
        readonly Label appTitle;
        readonly VisualElement content;
        readonly MenuList dock = new MenuList { Horizontal = true, Styler = StyleDock };
        readonly MenuList list = new MenuList { Styler = ModalParts.StyleRow, Wrap = false };
        int app;
        int calendarMonth;
        int unseenApp = -1;

        // Something new in an app (a contact, a Coop rank, an ability): the HUD shows a badge and
        // the phone opens on that app.
        public bool HasUnseen => unseenApp >= 0;

        public PhoneModal(GameRoot game) : base(game, "sp-phone")
        {
            phone = Ui.El("sp-phone__device", Root);
            Shape.Backdrop(phone, Shape.Box(), Palette.Ink).Stroke(Palette.Green, 4).Shadow(new Vector2(20, 20), Palette.Ink);
            var screen = Ui.El("sp-phone__screen", phone);
            var status = Ui.El("sp-phone__status", screen);
            clock = Ui.Text("", "sp-phone__clock", status, FontKind.MonoBold);
            statusDate = Ui.Text("", "sp-phone__status-date", status, FontKind.MonoBold);
            var header = Ui.El("sp-phone__header", screen);
            Shape.Backdrop(header, Shape.Slant(16), Palette.Green);
            appTitle = Ui.Text("", "sp-phone__title", header, FontKind.Black);
            content = Ui.El("sp-phone__content", screen);
            var dockBar = Ui.El("sp-phone__dock", screen);
            for (var i = 0; i < AppNames.Length; i++)
            {
                var item = Ui.El("sp-phone__app", dockBar);
                Shape.Backdrop(item, Shape.Slant(10), Color.clear);
                Ui.Text(AppNames[i], "sp-phone__app-text", item, FontKind.Black);
                dock.Add(item, null);
            }
            dock.SelectionChanged += ShowApp;
            ModalParts.Hints(Root, "←→ 切换　↑↓ 选择　Enter 查看　Esc 关闭");
        }

        protected override bool OwnEntrance => true; // the phone flies up from below (OnOpen)

        public void MarkUnseen(int appIndex) => unseenApp = appIndex;

        public void ResetSeen() => unseenApp = -1;

        protected override void OnOpen()
        {
            var s = Game.State;
            clock.text = SlotClock.TryGetValue(s.slot ?? "", out var c) ? c : "--:--";
            statusDate.text = $"{s.month}/{s.day} {GameData.WeekdayLabel(s.month, s.day)}";
            calendarMonth = s.month;
            dock.Select(HasUnseen ? unseenApp : CalendarApp, false);
            unseenApp = -1;
            Game.Audio.Sfx("glitch", 0.4f);
            Anim.Tween(0.22f, t =>
            {
                phone.style.translate = new Translate(0, (1 - t) * 700f);
                Ui.Turn(phone, Mathf.Lerp(8f, -3f, t));
            }, Ease.OutBack).Forget();
        }

        protected override void OnClose()
        {
            if (Game.InStory) Game.Story.RefreshHud();
        }

        static void StyleDock(VisualElement item, bool on)
        {
            item.Q<Shape>().Fill(on ? Palette.Green : Color.clear);
        }

        void ShowApp(int index)
        {
            app = index;
            appTitle.text = AppNames[index];
            content.Clear();
            list.Clear();
            switch (index)
            {
                case 0: BuildCalendar(); break;
                case 1: BuildContacts(); break;
                case 2: BuildCoops(); break;
                default: BuildInventory(); break;
            }
        }

        // ------------------------------------------------------------------ 日程

        void BuildCalendar()
        {
            var s = Game.State;
            var month = calendarMonth;
            var head = Ui.El("sp-cal__head", content);
            Ui.Text($"{GameData.Year}", "sp-cal__year", head, FontKind.MonoBold);
            Ui.Text($"{month}月", "sp-cal__month", head, FontKind.Black);
            Ui.Text("↑↓ 翻月", "sp-cal__hint", head, FontKind.MonoBold);

            var grid = Ui.El("sp-cal__grid", content);
            foreach (var w in WeekHeader) Ui.Text(w, "sp-cal__dow", grid, FontKind.MonoBold);
            var first = new DateTime(GameData.Year, month, 1);
            for (var i = 0; i < (int)first.DayOfWeek; i++) Ui.El("sp-cal__cell is-blank", grid);
            var days = DateTime.DaysInMonth(GameData.Year, month);
            for (var d = 1; d <= days; d++)
            {
                var cell = Ui.El("sp-cal__cell", grid);
                var past = month < s.month || (month == s.month && d < s.day);
                var today = month == s.month && d == s.day;
                cell.EnableInClassList("is-past", past);
                cell.EnableInClassList("is-today", today);
                if (today) Shape.Backdrop(cell, Shape.Diamond(), Palette.Green);
                Ui.Text(d.ToString(), "sp-cal__day", cell, FontKind.Black);
                if (past)
                {
                    var cross = new Shape(r => new[] { new Vector2(r.xMin + 6, r.yMax - 6), new Vector2(r.xMin + 9, r.yMax - 6), new Vector2(r.xMax - 6, r.yMin + 9), new Vector2(r.xMax - 6, r.yMin + 6) }, Palette.Red.WithAlpha(0.7f));
                    Ui.Fill(cross);
                    cell.Add(cross);
                }
                foreach (var dl in s.deadlines)
                {
                    if (dl.month != month || dl.day != d) continue;
                    cell.AddToClassList("is-deadline");
                    Ui.Text("!", "sp-cal__mark", cell, FontKind.Black);
                }
            }

            var agenda = Ui.El("sp-cal__agenda", content);
            Ui.Text($"今天　{s.DateLabel}　{GameData.SlotLabel(s.slot)}　{GameData.SceneLabel(s.scene)}", "sp-cal__line", agenda, FontKind.Bold);
            foreach (var dl in s.deadlines)
            {
                var title = GameData.Deadlines.TryGetValue(dl.id, out var l) ? l.Title : dl.id;
                var left = s.DaysUntil(dl);
                var line = Ui.Text($"{dl.month}/{dl.day}　{title}　{(left >= 0 ? $"还有 {left} 天" : "已过")}", "sp-cal__line is-deadline", agenda, FontKind.Black);
                line.style.color = Palette.Red;
            }
            if (s.deadlines.Count == 0) Ui.Text("没有安排。", "sp-cal__line is-dim", agenda, FontKind.Bold);
        }

        // ------------------------------------------------------------------ 通讯录

        void BuildContacts()
        {
            var s = Game.State;
            if (s.contacts.Count == 0)
            {
                Ui.Text("还没有联系人。", "sp-phone__empty", content, FontKind.Bold);
                return;
            }
            foreach (var id in s.contacts)
            {
                var def = GameData.Character(id);
                if (def == null) continue;
                var row = ModalParts.Row(content, "sp-contact");
                Ui.FaceCrop(def.PortraitKey(def.DefaultFace), 96f, 96f, "sp-contact__avatar", row);
                var texts = Ui.El("sp-contact__texts", row);
                var name = Ui.Text(def.Id, "sp-contact__name", texts, FontKind.Black);
                name.style.color = def.Color;
                Ui.Text(def.Role, "sp-contact__role", texts, FontKind.Bold);
                var coop = s.Coop(id);
                if (coop != null) Ui.Text($"{def.ArcanaNumeral} {def.Arcana}　RANK {coop.rank}", "sp-contact__coop", row, FontKind.MonoBold);
                list.Add(row, () => Game.Profile.Open(id));
            }
        }

        // ------------------------------------------------------------------ Coop

        void BuildCoops()
        {
            var s = Game.State;
            if (s.coops.Count == 0)
            {
                Ui.Text("还没有建立 Coop。", "sp-phone__empty", content, FontKind.Bold);
                return;
            }
            foreach (var coop in s.coops)
            {
                var def = GameData.Character(coop.character);
                if (def == null) continue;
                var row = ModalParts.Row(content, "sp-cooprow");
                var numeral = Ui.Text(def.ArcanaNumeral, "sp-cooprow__numeral", row, FontKind.Black);
                numeral.style.color = def.Color;
                var texts = Ui.El("sp-cooprow__texts", row);
                Ui.Text($"{def.Arcana}　{def.Id}", "sp-cooprow__name", texts, FontKind.Black);
                var next = GameData.PointsToNextRank(coop.rank);
                Ui.Text(next > 0 ? $"RANK {coop.rank}　下一级 {coop.points}/{next}" : $"RANK {coop.rank}　MAX", "sp-cooprow__rank", texts, FontKind.MonoBold);
                CoopCardView.Pips(texts, coop.rank, def.Color);
                var id = coop.character;
                list.Add(row, () => Game.Profile.Open(id));
            }
        }

        // ------------------------------------------------------------------ 持有物

        void BuildInventory()
        {
            var s = Game.State;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("sp-inv");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            content.Add(scroll);

            Section(scroll, "队伍");
            foreach (var id in s.party)
            {
                var m = s.Member(id);
                var stats = Battle.CombatStats.ForCharacter(id, m.level);
                var name = id == GameState.PlayerId ? s.playerName : id;
                Ui.Text($"{name}　Lv{m.level}　HP {m.hp}/{stats.MaxHp}　SP {m.sp}/{stats.MaxSp}", "sp-inv__line", scroll, FontKind.MonoBold);
            }

            Section(scroll, "陪伴生能力");
            Ui.Text(string.Join("　", Array.ConvertAll(GameData.Stats, st => $"{st.Label} {s.Stat(st.Id)}")), "sp-inv__line", scroll, FontKind.MonoBold);

            Section(scroll, "道具");
            if (s.inventory.Count == 0) Ui.Text("没有道具。", "sp-inv__line is-dim", scroll, FontKind.Bold);
            foreach (var stack in s.inventory)
            {
                var item = GameData.Items.TryGetValue(stack.id, out var def) ? def : null;
                Ui.Text($"{item?.Name ?? stack.id} ×{stack.count}", "sp-inv__name", scroll, FontKind.Black);
                if (item != null) Ui.Text(item.Description, "sp-inv__desc", scroll, FontKind.Regular);
            }

            Section(scroll, "能力");
            if (s.abilities.Count == 0) Ui.Text("还没有能力。", "sp-inv__line is-dim", scroll, FontKind.Bold);
            foreach (var id in s.abilities)
            {
                var ability = GameData.Abilities.TryGetValue(id, out var a) ? a : null;
                Ui.Text(ability != null ? $"{ability.Name}（{ability.Owner}）" : id, "sp-inv__name", scroll, FontKind.Black);
                if (ability != null) Ui.Text(ability.Description, "sp-inv__desc", scroll, FontKind.Regular);
            }

            Section(scroll, "token");
            Ui.Text($"{s.tokens:N0} token", "sp-inv__line", scroll, FontKind.MonoBold);

            Section(scroll, "数据珍珠");
            Ui.Text($"{s.pearls.Count} / {Dungeon.Dungeons.PearlTotal}", "sp-inv__line", scroll, FontKind.MonoBold);
        }

        static void Section(VisualElement parent, string title)
        {
            var head = Ui.El("sp-inv__section", parent);
            Shape.Backdrop(head, Shape.Slant(10), Palette.Ink);
            Ui.Text(title, "sp-inv__section-text", head, FontKind.Black);
        }

        public override void HandleInput()
        {
            if (GameInput.Consume(Btn.Cancel) || GameInput.Consume(Btn.Phone) || GameInput.Consume(Btn.Menu))
            {
                Game.Audio.Sfx("cursor");
                Close();
                return;
            }
            if (GameInput.Consume(Btn.Left)) { dock.Move(-1); return; }
            if (GameInput.Consume(Btn.Right)) { dock.Move(1); return; }
            if (app == 0)
            {
                var delta = GameInput.Consume(Btn.Up) ? -1 : GameInput.Consume(Btn.Down) ? 1 : 0;
                if (delta != 0)
                {
                    calendarMonth = Mathf.Clamp(calendarMonth + delta, 1, 12);
                    Game.Audio.Sfx("cursor");
                    ShowApp(0);
                }
                return;
            }
            list.HandleInput();
        }
    }
}
