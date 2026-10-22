using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // 资料: a contact's profile with a portrait you can poke (same reactions as the title screen).
    public sealed class ProfileModal : Modal
    {
        readonly VisualElement stage;
        readonly VisualElement card;
        readonly Dictionary<string, InteractivePortrait> portraits = new Dictionary<string, InteractivePortrait>();
        InteractivePortrait shown;

        public ProfileModal(GameRoot game) : base(game, "sp-profile")
        {
            stage = Ui.El("sp-profile__stage", Root);
            card = Ui.El("sp-profile__card", Root);
            ModalParts.Hints(Root, "点她的头或身体　Esc 返回");
        }

        public void Open(string id)
        {
            var def = GameData.Character(id);
            if (def == null) return;
            if (shown != null) Ui.Show(shown.Root, false);
            if (!portraits.TryGetValue(id, out shown))
            {
                shown = new InteractivePortrait(def, stage, 1060f, () => AddressFor(id));
                shown.Root.style.left = 140f;
                shown.Root.style.top = 60f;
                portraits[id] = shown;
            }
            Ui.Show(shown.Root, true);
            shown.ResetFace();
            BuildCard(def);
            Open();
        }

        string AddressFor(string id)
        {
            var name = Game.State.playerName;
            if (id != "DeepSeek") return name;
            var nickname = Game.StoryString("$deepseek_calls_player");
            return string.IsNullOrEmpty(nickname) ? name : nickname;
        }

        void BuildCard(CharacterDef def)
        {
            card.Clear();
            Shape.Backdrop(card, Shape.Crooked(18), Palette.Paper).Stroke(Palette.Ink, 4).Shadow(new Vector2(16, 16), def.Color);

            var head = Ui.El("sp-profile__head", card);
            Shape.Backdrop(head, Shape.Slant(22), Palette.Ink).Shadow(new Vector2(8, 8), def.Color);
            var name = Ui.Text(def.Id, "sp-profile__name", head, FontKind.Black);
            name.style.color = def.Color;
            Ui.Text(def.Role, "sp-profile__role", head, FontKind.Bold);

            if (!string.IsNullOrEmpty(def.Arcana))
            {
                var tags = Ui.El("sp-profile__tags", card);
                Tag(tags, $"{def.ArcanaNumeral} {def.Arcana}", def.Color);
                if (!string.IsNullOrEmpty(def.ElementLabel)) Tag(tags, $"属性 {def.ElementLabel}", Palette.Ink);
            }

            var table = Ui.El("sp-profile__fields", card);
            foreach (var f in def.Profile) Field(table, f.Label, f.Value);
            if (def.Id == "DeepSeek")
            {
                var nickname = Game.StoryString("$deepseek_calls_player");
                Field(table, "对你的称呼", string.IsNullOrEmpty(nickname) ? "——" : $"「{nickname}」");
            }

            var coop = Game.State.Coop(def.Id);
            if (coop != null)
            {
                var row = Ui.El("sp-profile__coop", card);
                Ui.Text($"Coop RANK {coop.rank}", "sp-profile__coop-text", row, FontKind.MonoBold);
                CoopCardView.Pips(row, coop.rank, def.Color);
            }
        }

        static void Tag(VisualElement parent, string text, Color color)
        {
            var tag = Ui.El("sp-profile__tag", parent);
            Shape.Backdrop(tag, Shape.Slant(10), color);
            var label = Ui.Text(text, "sp-profile__tag-text", tag, FontKind.Black);
            label.style.color = color == Palette.Ink ? Palette.Paper : Palette.Ink;
        }

        static void Field(VisualElement parent, string label, string value)
        {
            var row = Ui.El("sp-profile__field", parent);
            Ui.Text(label, "sp-profile__label", row, FontKind.Black);
            Ui.Text(value, "sp-profile__value", row, FontKind.Regular);
        }

        public override void HandleInput()
        {
            if (GameInput.Consume(Btn.Cancel) || GameInput.Consume(Btn.Menu) || GameInput.Consume(Btn.Confirm))
            {
                Game.Audio.Sfx("cursor");
                Close();
            }
        }
    }
}
