using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // <<nickname_ask 角色>>: the player types what they will call a character. It sits over the dialogue box
    // so her portrait stays in view, and it cannot be dismissed: the story waits for a name.
    public sealed class NicknameModal : Modal
    {
        const int MaxLength = 6;
        // What she already weighed aloud, whatever the capitals typed.
        static readonly string[] Known = { "DeepSeek", "D老师", "小D" };

        readonly Label lead;
        readonly TextField field;
        readonly Label error;
        readonly MenuList rows = new MenuList { Styler = ModalParts.StyleRow };
        AwaitableCompletionSource<string> answer;
        string who;
        bool typing;

        public NicknameModal(GameRoot game) : base(game, "sp-nickname", decorated: false)
        {
            var panel = ModalParts.Panel(Root, "起个称呼", "NICKNAME", "sp-nickname__panel");
            lead = Ui.Text("", "sp-newgame__lead", panel, FontKind.Bold);

            var row = ModalParts.Row(panel, "sp-newgame__row");
            Ui.Text("称呼", "sp-newgame__label", row, FontKind.Black);
            field = new TextField { maxLength = MaxLength };
            field.AddToClassList("sp-newgame__field");
            field.textEdition.placeholder = $"最多 {MaxLength} 个字";
            Fonts.Apply(field, FontKind.Bold);
            row.Add(field);
            field.RegisterCallback<FocusInEvent>(_ => SetTyping(true));
            field.RegisterCallback<FocusOutEvent>(_ => SetTyping(false));
            field.RegisterValueChangedCallback(_ => error.text = "");

            error = Ui.Text("", "sp-newgame__error", panel, FontKind.Bold);
            var buttons = Ui.El("sp-newgame__buttons", panel);
            var done = ModalParts.Button(buttons, "就叫这个");

            rows.Add(row, () => field.Focus());
            rows.Add(done, Submit);
        }

        // Completes with the name once the player has confirmed it. A load or a return to the title closes
        // the dialog instead, and the abandoned story's wait then never ends.
        public Awaitable<string> Ask(string character)
        {
            who = character;
            lead.text = $"给 {character} 起一个称呼，以后就这么叫她";
            answer = new AwaitableCompletionSource<string>();
            Open();
            return answer.Awaitable;
        }

        public override bool CoversScreen => false;

        void SetTyping(bool on)
        {
            typing = on;
            GameInput.TextEntryActive = on;
        }

        protected override void OnOpen()
        {
            field.SetValueWithoutNotify("");
            error.text = "";
            rows.Select(0, false);
            field.schedule.Execute(() => field.Focus());
        }

        protected override void OnClose()
        {
            field.Blur();
            SetTyping(false);
        }

        string Typed()
        {
            var name = field.value.Trim();
            foreach (var known in Known)
                if (string.Equals(name, known, StringComparison.OrdinalIgnoreCase)) return known;
            return name;
        }

        string Validate(string name) => GameData.NameProblem(name, MaxLength, "称呼", who);

        // Shows what is wrong with the typed name and puts the cursor back in the field; true when it is fine.
        bool Accept(string name)
        {
            var problem = Validate(name);
            if (problem == null) return true;
            error.text = problem;
            Game.Audio.Sfx("crack", 0.6f);
            rows.Select(0, false);
            field.Focus();
            return false;
        }

        void Submit()
        {
            var name = Typed();
            if (!Accept(name)) return;
            Game.Confirm.Ask($"以后叫 {who}「{name}」？\n定下来就不能改了。", () =>
            {
                var pending = answer;
                answer = null;
                Close();
                pending?.SetResult(name);
            }, null, "就这个", "再想想", false);
        }

        public override void HandleInput()
        {
            if (typing)
            {
                // While typing, only Enter (finish the name) and Esc (stop typing) belong to the dialog.
                if (GameInput.Consume(Btn.Confirm))
                {
                    if (!Accept(Typed())) return;
                    field.Blur();
                    rows.Select(1);
                }
                else if (GameInput.Consume(Btn.Cancel)) field.Blur();
                return;
            }
            GameInput.Consume(Btn.Cancel); // no way out without a name
            rows.HandleInput();
        }
    }
}
