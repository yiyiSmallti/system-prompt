using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // 陪伴生登记: the player's name and gender, which the Yarn script reads as $player_name / $player_gender.
    public sealed class NewGameModal : Modal
    {
        const int MaxNameLength = 8;

        readonly TextField nameField;
        readonly Label error;
        readonly VisualElement male;
        readonly VisualElement female;
        readonly MenuList rows = new MenuList { Styler = ModalParts.StyleRow };
        string gender = "male";
        bool typing;

        public NewGameModal(GameRoot game) : base(game, "sp-newgame")
        {
            var panel = ModalParts.Panel(Root, "陪伴生登记", "REGISTRATION", "sp-newgame__panel");
            Ui.Text("共生计划 第一期　AI 特别班陪伴生", "sp-newgame__lead", panel, FontKind.Bold);

            var nameRow = ModalParts.Row(panel, "sp-newgame__row");
            Ui.Text("姓名", "sp-newgame__label", nameRow, FontKind.Black);
            nameField = new TextField { maxLength = MaxNameLength };
            nameField.AddToClassList("sp-newgame__field");
            nameField.textEdition.placeholder = $"最多 {MaxNameLength} 个字";
            Fonts.Apply(nameField, FontKind.Bold);
            nameRow.Add(nameField);
            nameField.RegisterCallback<FocusInEvent>(_ => SetTyping(true));
            nameField.RegisterCallback<FocusOutEvent>(_ => SetTyping(false));
            nameField.RegisterValueChangedCallback(_ => error.text = "");

            var genderRow = ModalParts.Row(panel, "sp-newgame__row");
            Ui.Text("性别", "sp-newgame__label", genderRow, FontKind.Black);
            male = Chip(genderRow, "男", "male");
            female = Chip(genderRow, "女", "female");

            error = Ui.Text("", "sp-newgame__error", panel, FontKind.Bold);
            var buttons = Ui.El("sp-newgame__buttons", panel);
            var start = ModalParts.Button(buttons, "登记并开始");
            ModalParts.Hints(panel, "↑↓ 选择　←→ 切换性别　Enter 确定　Esc 返回");

            rows.Add(nameRow, () => nameField.Focus());
            rows.Add(genderRow, () => SetGender(gender == "male" ? "female" : "male"));
            rows.Add(start, Submit);
        }

        VisualElement Chip(VisualElement parent, string text, string value)
        {
            var chip = Ui.El("sp-chip", parent);
            Shape.Backdrop(chip, Shape.Slant(10), Palette.Ink);
            Ui.Text(text, "sp-chip__text", chip, FontKind.Black);
            chip.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                SetGender(value);
            });
            return chip;
        }

        void SetGender(string value)
        {
            if (gender != value) Game.Audio.Sfx("cursor");
            gender = value;
            foreach (var (chip, v) in new[] { (male, "male"), (female, "female") })
            {
                var on = v == gender;
                chip.EnableInClassList("is-on", on);
                chip.Q<Shape>().Fill(on ? Palette.Green : Palette.Ink.WithAlpha(0.15f));
            }
        }

        void SetTyping(bool on)
        {
            typing = on;
            GameInput.TextEntryActive = on;
        }

        protected override void OnOpen()
        {
            nameField.SetValueWithoutNotify("");
            error.text = "";
            SetGender("male");
            rows.Select(0, false);
            nameField.schedule.Execute(() => nameField.Focus());
        }

        protected override void OnClose()
        {
            nameField.Blur();
            SetTyping(false);
        }

        static string Validate(string name) => GameData.NameProblem(name, MaxNameLength, "姓名");

        void Submit()
        {
            var name = nameField.value.Trim();
            var problem = Validate(name);
            if (problem != null)
            {
                error.text = problem;
                Game.Audio.Sfx("crack", 0.6f);
                rows.Select(0, false);
                nameField.Focus();
                return;
            }
            var genderLabel = gender == "male" ? "男" : "女";
            var chosenGender = gender;
            Game.Confirm.Ask($"以「{name}」（{genderLabel}）登记为陪伴生？\n开始后不能修改。", () =>
            {
                Close();
                Game.StartNewGame(name, chosenGender);
            }, null, "开始", "再想想", false);
        }

        public override void HandleInput()
        {
            if (typing)
            {
                // While typing, only Enter (finish the name) and Esc (stop typing) belong to the menu.
                if (GameInput.Consume(Btn.Confirm))
                {
                    nameField.Blur();
                    rows.Select(Validate(nameField.value.Trim()) == null ? 2 : 0);
                }
                else if (GameInput.Consume(Btn.Cancel)) nameField.Blur();
                return;
            }
            if (GameInput.Consume(Btn.Cancel))
            {
                Game.Audio.Sfx("cursor");
                Close();
                return;
            }
            if (rows.Index == 1)
            {
                if (GameInput.Consume(Btn.Left)) { SetGender("male"); return; }
                if (GameInput.Consume(Btn.Right)) { SetGender("female"); return; }
            }
            rows.HandleInput();
        }
    }
}
