using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Building blocks shared by the menus: the slanted paper panel with its black heading tag, buttons,
    // and the key hint line at the bottom.
    public static class ModalParts
    {
        // Returns the panel; put content into it after the heading.
        public static VisualElement Panel(VisualElement parent, string heading, string tag, string classes = null)
        {
            var panel = Ui.El("sp-panel " + (classes ?? ""), parent);
            Shape.Backdrop(panel, Shape.Crooked(16), Palette.Paper).Stroke(Palette.Ink, 4).Shadow(new Vector2(16, 16), Palette.Ink);
            var head = Ui.El("sp-panel__head", panel);
            Shape.Backdrop(head, Shape.Slant(20), Palette.Ink).Shadow(new Vector2(8, 8), Palette.Green);
            Ui.Text(heading, "sp-panel__title", head, FontKind.Black);
            if (!string.IsNullOrEmpty(tag)) Ui.Text(tag, "sp-panel__tag", head, FontKind.MonoBold);
            return panel;
        }

        public static VisualElement Button(VisualElement parent, string text, string classes = null)
        {
            var b = Ui.El("sp-button " + (classes ?? ""), parent);
            Shape.Backdrop(b, Shape.Slant(14), Palette.Ink);
            Ui.Text(text, "sp-button__text", b, FontKind.Black);
            return b;
        }

        // Styler for MenuList: the selected button turns green with a hard shadow.
        public static void StyleButton(VisualElement item, bool on)
        {
            var shape = item.Q<Shape>();
            if (shape == null) return;
            shape.Fill(on ? Palette.Green : Palette.Ink).Shadow(on ? new Vector2(6, 6) : Vector2.zero, Palette.Ink);
        }

        // Styler for list rows on paper: the selected row gets an ink band. Buttons at the end of a
        // row list (恢复默认, 登记并开始) keep the button look.
        public static void StyleRow(VisualElement item, bool on)
        {
            if (item.ClassListContains("sp-button"))
            {
                StyleButton(item, on);
                return;
            }
            var shape = item.Q<Shape>();
            if (shape == null) return;
            shape.Fill(on ? Palette.Ink : Color.clear).Shadow(on ? new Vector2(6, 6) : Vector2.zero, Palette.Green);
        }

        public static VisualElement Row(VisualElement parent, string classes = null)
        {
            var row = Ui.El("sp-row " + (classes ?? ""), parent);
            Shape.Backdrop(row, Shape.Slant(12), Color.clear);
            return row;
        }

        public static Label Hints(VisualElement parent, string text) => Ui.Text(text, "sp-hints", parent, FontKind.MonoBold);
    }
}
