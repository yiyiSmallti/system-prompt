using System;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    public sealed class ConfirmModal : Modal
    {
        readonly Label message;
        readonly Label yesLabel;
        readonly Label noLabel;
        readonly MenuList buttons = new MenuList { Horizontal = true, Styler = ModalParts.StyleButton };
        Action onYes;
        Action onNo;

        public ConfirmModal(GameRoot game) : base(game, "sp-confirm", decorated: false)
        {
            var panel = ModalParts.Panel(Root, "确认", "CONFIRM", "sp-confirm__panel");
            message = Ui.Text("", "sp-confirm__message", panel, FontKind.Bold);
            var row = Ui.El("sp-confirm__buttons", panel);
            var yes = ModalParts.Button(row, "确定");
            var no = ModalParts.Button(row, "取消");
            yesLabel = yes.Q<Label>();
            noLabel = no.Q<Label>();
            buttons.Add(yes, () => Answer(true));
            buttons.Add(no, () => Answer(false));
        }

        public void Ask(string text, Action yes, Action no = null, string yesText = "确定", string noText = "取消", bool defaultNo = true)
        {
            message.text = text;
            yesLabel.text = yesText;
            noLabel.text = noText;
            onYes = yes;
            onNo = no;
            Open();
            buttons.Select(defaultNo ? 1 : 0, false);
        }

        void Answer(bool yes)
        {
            var action = yes ? onYes : onNo;
            onYes = onNo = null;
            Close();
            action?.Invoke();
        }

        public override bool CoversScreen => false;

        public override void HandleInput()
        {
            if (GameInput.Consume(Btn.Cancel))
            {
                Game.Audio.Sfx("cursor");
                Answer(false);
                return;
            }
            buttons.HandleInput();
        }
    }
}
