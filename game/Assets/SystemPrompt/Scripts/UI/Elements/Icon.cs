using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    public enum IconKind { Auto, Skip, Log, Save, Load, Phone, Config, Hide, Menu }

    // A small line icon drawn with Painter2D on a 24x24 grid, scaled to the element. Its colour is the
    // --icon-color USS property, so hover and lit states recolour it from the stylesheet like text.
    public sealed class Icon : VisualElement
    {
        const float Grid = 24f;
        const float Line = 2.2f;
        static readonly CustomStyleProperty<Color> IconColor = new CustomStyleProperty<Color>("--icon-color");

        readonly IconKind kind;
        Color color = Color.white;

        public Icon(IconKind kind)
        {
            this.kind = kind;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ =>
            {
                if (customStyle.TryGetValue(IconColor, out var c) && c != color)
                {
                    color = c;
                    MarkDirtyRepaint();
                }
            });
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            var s = Mathf.Min(r.width, r.height) / Grid;
            if (s <= 0f) return;
            var o = new Vector2(r.x + (r.width - Grid * s) / 2f, r.y + (r.height - Grid * s) / 2f);
            Vector2 P(float x, float y) => o + new Vector2(x, y) * s;

            var p = mgc.painter2D;
            p.strokeColor = color;
            p.fillColor = color;
            p.lineWidth = Line * s;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            void Lines(params Vector2[] pts)
            {
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                p.Stroke();
            }
            void Outline(params Vector2[] pts)
            {
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                p.ClosePath();
                p.Stroke();
            }
            void Solid(params Vector2[] pts)
            {
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                p.ClosePath();
                p.Fill();
            }

            switch (kind)
            {
                case IconKind.Auto:
                {
                    // Play, inside a loop arrow.
                    Solid(P(9.5f, 7.8f), P(16.2f, 12f), P(9.5f, 16.2f));
                    const float from = -40f, to = 250f, radius = 9f;
                    p.BeginPath();
                    p.Arc(P(12f, 12f), radius * s, Angle.Degrees(from), Angle.Degrees(to));
                    p.Stroke();
                    var end = to * Mathf.Deg2Rad;
                    var at = new Vector2(12f + radius * Mathf.Cos(end), 12f + radius * Mathf.Sin(end));
                    var along = new Vector2(-Mathf.Sin(end), Mathf.Cos(end));
                    var outward = new Vector2(Mathf.Cos(end), Mathf.Sin(end));
                    var tip = at + along * 3.4f;
                    Solid(P(tip.x, tip.y), P(at.x + outward.x * 2.8f, at.y + outward.y * 2.8f), P(at.x - outward.x * 2.8f, at.y - outward.y * 2.8f));
                    break;
                }
                case IconKind.Skip:
                    Solid(P(3f, 6f), P(11f, 12f), P(3f, 18f));
                    Solid(P(11f, 6f), P(19f, 12f), P(11f, 18f));
                    Lines(P(21f, 6.5f), P(21f, 17.5f));
                    break;
                case IconKind.Log:
                    // A speech bubble with lines of text.
                    Outline(P(3f, 4f), P(21f, 4f), P(21f, 16f), P(11f, 16f), P(6.5f, 20.5f), P(7f, 16f), P(3f, 16f));
                    Lines(P(7f, 8.5f), P(17f, 8.5f));
                    Lines(P(7f, 12f), P(14f, 12f));
                    break;
                case IconKind.Save:
                    // A floppy disk.
                    Outline(P(3.5f, 3.5f), P(17f, 3.5f), P(20.5f, 7f), P(20.5f, 20.5f), P(3.5f, 20.5f));
                    Solid(P(8f, 3.5f), P(15f, 3.5f), P(15f, 8.5f), P(8f, 8.5f));
                    Outline(P(7.5f, 13f), P(16.5f, 13f), P(16.5f, 20.5f), P(7.5f, 20.5f));
                    break;
                case IconKind.Load:
                    // A folder with an arrow coming out.
                    Outline(P(3f, 6f), P(9f, 6f), P(11f, 8.5f), P(21f, 8.5f), P(21f, 19.5f), P(3f, 19.5f));
                    Lines(P(12f, 17f), P(12f, 11f));
                    Lines(P(9.2f, 13.6f), P(12f, 10.8f), P(14.8f, 13.6f));
                    break;
                case IconKind.Phone:
                    Outline(P(7f, 2.5f), P(17f, 2.5f), P(17f, 21.5f), P(7f, 21.5f));
                    Lines(P(10.5f, 18.5f), P(13.5f, 18.5f));
                    break;
                case IconKind.Config:
                {
                    // A gear: eight teeth around a hole.
                    p.BeginPath();
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * 45f;
                        foreach (var (da, rad) in new[] { (-17f, 7.4f), (-10f, 10.4f), (10f, 10.4f), (17f, 7.4f) })
                        {
                            var t = (a + da) * Mathf.Deg2Rad;
                            var q = P(12f + rad * Mathf.Cos(t), 12f + rad * Mathf.Sin(t));
                            if (i == 0 && da < -15f) p.MoveTo(q);
                            else p.LineTo(q);
                        }
                    }
                    p.ClosePath();
                    p.MoveTo(P(15.2f, 12f));
                    p.Arc(P(12f, 12f), 3.2f * s, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.ClosePath();
                    p.Fill(FillRule.OddEven);
                    break;
                }
                case IconKind.Hide:
                    // An eye, struck through.
                    p.BeginPath();
                    p.MoveTo(P(2f, 12f));
                    p.BezierCurveTo(P(7f, 4.5f), P(17f, 4.5f), P(22f, 12f));
                    p.BezierCurveTo(P(17f, 19.5f), P(7f, 19.5f), P(2f, 12f));
                    p.ClosePath();
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(P(12f, 12f), 2.8f * s, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    Lines(P(4.5f, 20f), P(19.5f, 4f));
                    break;
                case IconKind.Menu:
                    Lines(P(4.5f, 6.5f), P(19.5f, 6.5f));
                    Lines(P(4.5f, 12f), P(19.5f, 12f));
                    Lines(P(4.5f, 17.5f), P(19.5f, 17.5f));
                    break;
            }
        }
    }
}
