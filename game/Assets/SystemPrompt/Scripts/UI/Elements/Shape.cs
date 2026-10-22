using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // A filled polygon with an optional outline and hard drop shadow, drawn with Painter2D.
    // USS has no skew, so every slanted Persona-style panel is one of these behind its content.
    public class Shape : VisualElement
    {
        Func<Rect, Vector2[]> outline;
        Color fill;
        Color stroke = Color.clear;
        float strokeWidth;
        Vector2 shadowOffset;
        Color shadow = Palette.Ink;

        public Shape(Func<Rect, Vector2[]> outline, Color fill)
        {
            this.outline = outline;
            this.fill = fill;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public Shape Outline(Func<Rect, Vector2[]> value) { outline = value; MarkDirtyRepaint(); return this; }
        public Shape Fill(Color value) { fill = value; MarkDirtyRepaint(); return this; }
        public Shape Stroke(Color value, float width) { stroke = value; strokeWidth = width; MarkDirtyRepaint(); return this; }
        public Shape Shadow(Vector2 offset, Color color) { shadowOffset = offset; shadow = color; MarkDirtyRepaint(); return this; }

        // Fills its parent; content goes in the parent after this element.
        public static Shape Backdrop(VisualElement parent, Func<Rect, Vector2[]> outline, Color fill)
        {
            var s = new Shape(outline, fill);
            Ui.Fill(s);
            parent.Insert(0, s);
            return s;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width <= 0 || r.height <= 0) return;
            var pts = outline(r);
            var p = mgc.painter2D;
            if (shadowOffset != Vector2.zero && shadow.a > 0) Polygon(p, pts, shadowOffset, shadow, true);
            if (fill.a > 0) Polygon(p, pts, Vector2.zero, fill, true);
            if (strokeWidth > 0 && stroke.a > 0)
            {
                p.lineWidth = strokeWidth;
                p.lineJoin = LineJoin.Miter;
                Polygon(p, pts, Vector2.zero, stroke, false);
            }
        }

        static void Polygon(Painter2D p, Vector2[] pts, Vector2 offset, Color color, bool fillIt)
        {
            p.BeginPath();
            p.MoveTo(pts[0] + offset);
            for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i] + offset);
            p.ClosePath();
            if (fillIt)
            {
                p.fillColor = color;
                p.Fill();
            }
            else
            {
                p.strokeColor = color;
                p.Stroke();
            }
        }

        // ---- outlines

        public static Func<Rect, Vector2[]> Box() => r => new[]
        {
            new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax),
        };

        // Parallelogram leaning right by `slant` pixels.
        public static Func<Rect, Vector2[]> Slant(float slant) => r => new[]
        {
            new Vector2(r.xMin + slant, r.yMin), new Vector2(r.xMax, r.yMin),
            new Vector2(r.xMax - slant, r.yMax), new Vector2(r.xMin, r.yMax),
        };

        // The PV's slightly crooked speech box: no two edges parallel.
        public static Func<Rect, Vector2[]> Crooked(float amount) => r => new[]
        {
            new Vector2(r.xMin, r.yMin + amount * 0.6f), new Vector2(r.xMax - amount * 0.4f, r.yMin),
            new Vector2(r.xMax, r.yMax - amount * 0.5f), new Vector2(r.xMin + amount * 0.5f, r.yMax),
        };

        // A P5 text box: every corner pushed in by a different amount, so no two edges run parallel.
        public static Func<Rect, Vector2[]> Jag(int seed, float amount) => r =>
        {
            var rng = new System.Random(seed);
            float J() => amount * (0.25f + 0.75f * (float)rng.NextDouble());
            return new[]
            {
                new Vector2(r.xMin + J(), r.yMin + J()), new Vector2(r.xMax - J(), r.yMin + J()),
                new Vector2(r.xMax - J(), r.yMax - J()), new Vector2(r.xMin + J(), r.yMax - J()),
            };
        };

        // Spiky burst behind slammed words (对抗样本！/ONE MORE!).
        public static Func<Rect, Vector2[]> Burst(int spikes, float innerRatio, int seed) => r =>
        {
            var rng = new System.Random(seed);
            var c = r.center;
            var pts = new List<Vector2>();
            for (var i = 0; i < spikes * 2; i++)
            {
                var angle = Mathf.PI * i / spikes;
                var outer = i % 2 == 0;
                var k = outer ? 1f - (float)rng.NextDouble() * 0.25f : innerRatio * (0.85f + (float)rng.NextDouble() * 0.3f);
                pts.Add(c + new Vector2(Mathf.Cos(angle) * r.width * 0.5f * k, Mathf.Sin(angle) * r.height * 0.5f * k));
            }
            return pts.ToArray();
        };

        // A dry-brush paint stroke laid down left to right: a pointed landing, wavy edges, and the end
        // breaking up into streaks of different lengths (the pause menu's splashes).
        public static Func<Rect, Vector2[]> Brush(int seed) => r =>
        {
            const int Body = 24, Streaks = 11;
            var rng = new System.Random(seed);
            float Next() => (float)rng.NextDouble();
            var bodyEnd = r.xMin + r.width * 0.8f;
            var mid = r.center.y;
            var half = r.height * 0.5f;
            var phaseTop = Next() * 6f;
            var phaseBottom = Next() * 6f;
            float Thick(float u) => Mathf.Lerp(0.12f, 1f, Ease.OutCubic(Mathf.Clamp01(u / 0.22f)));
            float Wave(float u, float phase) => Mathf.Sin(u * 9f + phase) * 0.07f + (Next() - 0.5f) * 0.06f;
            var pts = new List<Vector2>();
            for (var i = 0; i <= Body; i++)
            {
                var u = (float)i / Body;
                pts.Add(new Vector2(Mathf.Lerp(r.xMin, bodyEnd, u), mid - half * (Thick(u) + Wave(u, phaseTop))));
            }
            // Streaks of uneven width, each with a blunt end, separated by notches cut back into the body.
            var cuts = new float[Streaks + 1];
            for (var i = 1; i <= Streaks; i++) cuts[i] = cuts[i - 1] + 0.4f + Next();
            for (var i = 0; i < Streaks; i++)
            {
                var top = mid - half + cuts[i] / cuts[Streaks] * half * 2f;
                var slot = (cuts[i + 1] - cuts[i]) / cuts[Streaks] * half * 2f;
                var reach = bodyEnd + r.width * (0.015f + 0.2f * Next() * Next());
                pts.Add(new Vector2(reach - r.width * 0.01f, top + slot * 0.1f));
                pts.Add(new Vector2(reach, top + slot * 0.4f));
                pts.Add(new Vector2(reach - r.width * 0.015f, top + slot * 0.7f));
                pts.Add(new Vector2(bodyEnd - r.width * (0.01f + 0.07f * Next()), top + slot * 0.88f));
            }
            for (var i = Body; i >= 0; i--)
            {
                var u = (float)i / Body;
                pts.Add(new Vector2(Mathf.Lerp(r.xMin, bodyEnd, u), mid + half * (Thick(u) + Wave(u, phaseBottom))));
            }
            return pts.ToArray();
        };

        // Downward triangle used as the "next line" marker.
        public static Func<Rect, Vector2[]> TriangleDown() => r => new[]
        {
            new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.center.x, r.yMax),
        };

        public static Func<Rect, Vector2[]> TriangleRight() => r => new[]
        {
            new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.center.y), new Vector2(r.xMin, r.yMax),
        };

        public static Func<Rect, Vector2[]> Diamond() => r => new[]
        {
            new Vector2(r.center.x, r.yMin), new Vector2(r.xMax, r.center.y), new Vector2(r.center.x, r.yMax), new Vector2(r.xMin, r.center.y),
        };
    }

    // CRT scanlines: a thin dark line every `step` pixels (the settings terminal).
    public class Scanlines : VisualElement
    {
        readonly Color color;
        readonly float step;

        public Scanlines(Color color, float step)
        {
            this.color = color;
            this.step = step;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width <= 0 || r.height <= 0) return;
            var p = mgc.painter2D;
            p.fillColor = color;
            for (var y = r.yMin; y < r.yMax; y += step)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(r.xMin, y));
                p.LineTo(new Vector2(r.xMax, y));
                p.LineTo(new Vector2(r.xMax, y + step * 0.4f));
                p.LineTo(new Vector2(r.xMin, y + step * 0.4f));
                p.ClosePath();
                p.Fill();
            }
        }
    }

    // Comic halftone: a dot grid whose dots grow toward one side.
    public class Halftone : VisualElement
    {
        readonly Color color;
        readonly float step;
        readonly float maxRadius;
        readonly bool growRight;
        Func<Rect, Vector2[]> clip;

        public Halftone(Color color, float step = 14f, float maxRadius = 4f, bool growRight = true)
        {
            this.color = color;
            this.step = step;
            this.maxRadius = maxRadius;
            this.growRight = growRight;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        // Draws only the dots that fit wholly inside this convex outline (e.g. the slanted panel the dots
        // sit on), so none spill past a slanted edge.
        public Halftone Clip(Func<Rect, Vector2[]> outline)
        {
            clip = outline;
            MarkDirtyRepaint();
            return this;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width <= 0 || r.height <= 0) return;
            var bounds = clip?.Invoke(r);
            var winding = bounds == null ? 0f : Mathf.Sign(SignedArea(bounds));
            var p = mgc.painter2D;
            p.fillColor = color;
            var row = 0;
            for (var y = r.yMin + step * 0.5f; y < r.yMax; y += step, row++)
            {
                var x0 = r.xMin + (row % 2 == 0 ? step * 0.5f : step);
                for (var x = x0; x < r.xMax; x += step)
                {
                    var k = (x - r.xMin) / r.width;
                    var rad = maxRadius * (growRight ? k : 1 - k);
                    if (rad < 0.4f) continue;
                    if (bounds != null && !Inside(bounds, winding, new Vector2(x, y), rad)) continue;
                    p.BeginPath();
                    p.MoveTo(new Vector2(x, y - rad));
                    p.LineTo(new Vector2(x + rad, y));
                    p.LineTo(new Vector2(x, y + rad));
                    p.LineTo(new Vector2(x - rad, y));
                    p.ClosePath();
                    p.Fill();
                }
            }
        }

        static float SignedArea(Vector2[] pts)
        {
            var sum = 0f;
            for (var i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum;
        }

        // At least `margin` inside every edge.
        static bool Inside(Vector2[] pts, float winding, Vector2 c, float margin)
        {
            for (var i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i], e = pts[(i + 1) % pts.Length] - a;
                if ((e.x * (c.y - a.y) - e.y * (c.x - a.x)) * winding / e.magnitude < margin) return false;
            }
            return true;
        }
    }
}
