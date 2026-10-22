using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // The arcana card shown on a Coop rank up (<<coop_rank>>) and in the phone's Coop tab.
    public static class CoopCardView
    {
        public const int MaxRank = 10;

        static readonly Dictionary<string, string> ArcanaEnglish = new Dictionary<string, string>
        {
            ["法王"] = "THE HIEROPHANT",
            ["女教皇"] = "THE HIGH PRIESTESS",
            ["隐者"] = "THE HERMIT",
        };

        public static VisualElement Build(CharacterDef def, int rank, VisualElement parent)
        {
            var card = Ui.El("sp-coopcard", parent);
            Shape.Backdrop(card, Shape.Box(), Palette.Ink).Stroke(def.Color, 8).Shadow(new Vector2(18, 18), Palette.Green);
            var dots = new Halftone(def.Color.WithAlpha(0.3f), 18f, 5f, false);
            dots.AddToClassList("sp-coopcard__dots");
            card.Add(dots);

            var numeral = Ui.Text(def.ArcanaNumeral ?? "", "sp-coopcard__numeral", card, FontKind.Black);
            numeral.style.color = def.Color;
            Ui.Text(def.Arcana ?? "", "sp-coopcard__arcana", card, FontKind.Black);
            Ui.Text(ArcanaEnglish.TryGetValue(def.Arcana ?? "", out var en) ? en : "", "sp-coopcard__english", card, FontKind.MonoBold);

            var face = Ui.FaceCrop(def.PortraitKey(def.DefaultFace), 400f, 300f, "sp-coopcard__face", card);
            face.style.borderTopColor = face.style.borderBottomColor = face.style.borderLeftColor = face.style.borderRightColor = def.Color;

            var name = Ui.El("sp-coopcard__name", card);
            Shape.Backdrop(name, Shape.Slant(14), Palette.Paper).Shadow(new Vector2(6, 6), def.Color);
            Ui.Text($"Coop：{def.Id}", "sp-coopcard__name-text", name, FontKind.Black);

            Ui.Text($"RANK {rank}", "sp-coopcard__rank", card, FontKind.MonoBold);
            Pips(card, rank, def.Color, "sp-coopcard__pips");
            return card;
        }

        // Ten diamonds, filled up to `rank`.
        public static VisualElement Pips(VisualElement parent, int rank, Color color, string classes = "sp-pips")
        {
            var row = Ui.El(classes + " sp-pips", parent);
            for (var i = 1; i <= MaxRank; i++)
            {
                var on = i <= rank;
                var pip = new Shape(Shape.Diamond(), on ? color : Color.clear).Stroke(on ? Palette.Ink : Palette.Gray, 2);
                pip.AddToClassList("sp-pip");
                row.Add(pip);
            }
            return row;
        }
    }
}
