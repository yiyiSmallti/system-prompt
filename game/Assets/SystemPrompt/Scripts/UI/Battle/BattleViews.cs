using SystemPrompt.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Noise bodies are drawn, not painted: 标题党型 is a twitching block flashing red and yellow,
    // 转发型 a bundle of forking arrows that keeps leaving copies of itself (design/systems.md 噪声体图鉴).
    public sealed class NoiseBody : VisualElement
    {
        static readonly Color Yellow = Palette.Hex("#F5C542");

        readonly string kind;
        int seed;
        public bool Frozen;

        public NoiseBody(string kind)
        {
            this.kind = kind;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            schedule.Execute(() =>
            {
                if (Frozen) return;
                seed++;
                MarkDirtyRepaint();
            }).Every(110);
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width <= 0) return;
            var p = mgc.painter2D;
            var rng = new System.Random(seed);
            float J(float amount) => ((float)rng.NextDouble() * 2f - 1f) * amount;
            var jitter = Frozen ? 0f : 6f;

            if (kind == "repost")
            {
                // Afterimages first, then the real arrows on top.
                for (var copy = 2; copy >= 0; copy--)
                {
                    var offset = new Vector2(copy * -26f + J(jitter), copy * 10f + J(jitter));
                    var alpha = copy == 0 ? 1f : 0.28f / copy;
                    for (var i = 0; i < 7; i++)
                    {
                        var angle = -70f + i * 23f + J(4f);
                        var length = r.width * (0.36f + 0.08f * (i % 3));
                        Arrow(p, r.center + offset, angle, length, (i % 2 == 0 ? Palette.Paper : Palette.Gray).WithAlpha(alpha));
                    }
                }
                Blob(p, r.center, r.width * 0.16f, Frozen ? Palette.Cold : Palette.Dim, J);
                return;
            }

            // 标题党型
            var body = new Rect(r.x + r.width * 0.12f + J(jitter), r.y + r.height * 0.12f + J(jitter), r.width * 0.76f, r.height * 0.76f);
            Quad(p, body, Frozen ? Palette.Cold : Palette.Dim, J, 4f);
            for (var i = 0; i < 14; i++)
            {
                var w = body.width * (0.1f + (float)rng.NextDouble() * 0.25f);
                var h = body.height * (0.06f + (float)rng.NextDouble() * 0.14f);
                var x = body.x + (float)rng.NextDouble() * (body.width - w);
                var y = body.y + (float)rng.NextDouble() * (body.height - h);
                var c = Frozen ? Palette.Sea.WithAlpha(0.5f) : (rng.Next(2) == 0 ? Palette.Red : Yellow);
                Quad(p, new Rect(x, y, w, h), c, J, 0f);
            }
        }

        static void Quad(Painter2D p, Rect q, Color c, System.Func<float, float> j, float wobble)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(q.xMin + j(wobble), q.yMin + j(wobble)));
            p.LineTo(new Vector2(q.xMax + j(wobble), q.yMin + j(wobble)));
            p.LineTo(new Vector2(q.xMax + j(wobble), q.yMax + j(wobble)));
            p.LineTo(new Vector2(q.xMin + j(wobble), q.yMax + j(wobble)));
            p.ClosePath();
            p.Fill();
        }

        static void Blob(Painter2D p, Vector2 c, float radius, Color color, System.Func<float, float> j)
        {
            p.fillColor = color;
            p.BeginPath();
            for (var i = 0; i < 10; i++)
            {
                var a = Mathf.PI * 2f * i / 10f;
                var rr = radius * (0.85f + Mathf.Abs(j(0.25f)));
                var pt = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                if (i == 0) p.MoveTo(pt);
                else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
        }

        static void Arrow(Painter2D p, Vector2 from, float degrees, float length, Color color)
        {
            var dir = new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
            var side = new Vector2(-dir.y, dir.x);
            var tip = from + dir * length;
            var neck = from + dir * (length - 28f);
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(from + side * 6f);
            p.LineTo(neck + side * 6f);
            p.LineTo(neck + side * 18f);
            p.LineTo(tip);
            p.LineTo(neck - side * 18f);
            p.LineTo(neck - side * 6f);
            p.LineTo(from - side * 6f);
            p.ClosePath();
            p.Fill();
        }
    }

    public sealed class EnemyView
    {
        public readonly Combatant Unit;
        public readonly VisualElement Root;
        public readonly NoiseBody Body;
        readonly VisualElement hpFill;
        readonly Label weakness;
        readonly VisualElement weakTag;
        readonly VisualElement cursor;
        readonly VisualElement downTag;
        readonly Label[] words;

        public Vector2 Center;

        public EnemyView(Combatant unit, VisualElement parent, Vector2 center)
        {
            Unit = unit;
            Center = center;
            Root = Ui.El("sp-enemy", parent);
            Root.style.left = center.x - 140f;
            Root.style.top = center.y - 200f;

            var plate = Ui.El("sp-enemy__plate", Root);
            Shape.Backdrop(plate, Shape.Slant(12), Palette.Ink).Shadow(new Vector2(6, 6), Palette.Green);
            Ui.Text(unit.Name, "sp-enemy__name", plate, FontKind.Black);
            var bar = Ui.El("sp-enemy__hp", plate);
            hpFill = Ui.El("sp-enemy__hp-fill", bar);
            weakTag = Ui.El("sp-enemy__weak", Root);
            Shape.Backdrop(weakTag, Shape.Slant(10), Palette.Paper).Stroke(Palette.Ink, 2);
            weakness = Ui.Text("", "sp-enemy__weak-text", weakTag, FontKind.Black);

            var bodyBox = Ui.El("sp-enemy__body", Root);
            Body = new NoiseBody(unit.Kind);
            Ui.Fill(Body);
            bodyBox.Add(Body);
            words = unit.Kind == "repost"
                ? new[] { Ui.Text("转发保平安", "sp-enemy__word", bodyBox, FontKind.Black) }
                : new[] { Ui.Text("震惊！", "sp-enemy__word", bodyBox, FontKind.Black), Ui.Text("点击领取", "sp-enemy__word is-small", bodyBox, FontKind.Black) };

            downTag = Ui.Text("DOWN", "sp-enemy__down", Root, FontKind.Black);
            Ui.Show(downTag, false);
            cursor = new Shape(Shape.TriangleDown(), Palette.Green).Stroke(Palette.Ink, 3);
            cursor.AddToClassList("sp-enemy__cursor");
            Root.Add(cursor);
            Ui.Show(cursor, false);

            Root.schedule.Execute(Twitch).Every(140);
            Refresh();
        }

        void Twitch()
        {
            if (Body.Frozen) return;
            foreach (var w in words) w.style.translate = new Translate(Random.Range(-6f, 6f), Random.Range(-4f, 4f));
        }

        public void Refresh()
        {
            hpFill.style.width = Length.Percent(100f * Unit.Hp / Mathf.Max(1, Unit.MaxHp));
            weakness.text = $"弱点：{Unit.WeaknessLabel}";
            weakTag.EnableInClassList("is-true", Unit.WeaknessLabel == Unit.TrueWeakness);
        }

        public void SetTargeted(bool on)
        {
            Ui.Show(cursor, on);
            Root.EnableInClassList("is-targeted", on);
        }

        public void SetDown(bool down)
        {
            Ui.Show(downTag, down);
            Body.Frozen = down;
            var from = down ? 0f : 1f;
            Anim.Tween(0.25f, t =>
            {
                var k = Mathf.Abs(from - t);
                Body.parent.style.rotate = new Rotate(new Angle(k * -62f, AngleUnit.Degree));
                Body.parent.style.translate = new Translate(k * -30f, k * 70f);
            }, Ease.OutBack).Forget();
        }

        public async Awaitable Die()
        {
            Ui.Show(cursor, false);
            await Anim.Tween(0.35f, t =>
            {
                Root.style.opacity = 1 - t;
                Ui.Zoom(Body.parent, 1f + t * 0.6f);
            });
            Ui.Show(Root, false);
        }

        // 标题党型's label lies: 火 → ？？？ (turn 1), then it shatters into the true weakness (turn 2).
        public async Awaitable Flicker(string label)
        {
            for (var i = 0; i < 4; i++)
            {
                weakTag.style.opacity = i % 2 == 0 ? 0.2f : 1f;
                await Anim.Wait(0.06f);
            }
            Unit.WeaknessLabel = label;
            weakTag.style.opacity = 1;
            Refresh();
        }

        public async Awaitable Shatter()
        {
            await Anim.Tween(0.25f, t =>
            {
                weakTag.style.translate = new Translate(Random.Range(-8f, 8f) * (1 - t), t * 40f);
                weakTag.style.opacity = 1 - t;
            }, Ease.Linear);
            Unit.WeaknessLabel = Unit.TrueWeakness;
            Refresh();
            await Anim.Tween(0.25f, t =>
            {
                weakTag.style.translate = new Translate(0, (1 - t) * -30f);
                weakTag.style.opacity = t;
                Ui.Zoom(weakTag, Mathf.Lerp(1.8f, 1f, t));
            }, Ease.OutBack);
        }
    }

    public sealed class PartyCard
    {
        public readonly Combatant Unit;
        public readonly VisualElement Root;
        readonly VisualElement hpFill;
        readonly VisualElement spFill;
        readonly Label hpText;
        readonly Label spText;
        readonly Label status;

        public PartyCard(Combatant unit, VisualElement parent)
        {
            Unit = unit;
            Root = Ui.El("sp-partycard", parent);
            Shape.Backdrop(Root, Shape.Slant(26), Palette.Ink.WithAlpha(0.94f)).Stroke(Palette.Paper, 3).Shadow(new Vector2(10, 10), Palette.Green);

            if (unit.Id == "DeepSeek")
            {
                Ui.FaceCrop("Art/Portraits/deepseek_battle", 150f, 150f, "sp-partycard__face", Root, 2.4f, 0.2f);
            }
            else
            {
                // No portrait for the player character yet: the name's first character on a halftone tile.
                var face = Ui.El("sp-partycard__face is-player", Root);
                var dots = new Halftone(Palette.Ink.WithAlpha(0.35f), 12f, 4f);
                Ui.Fill(dots);
                face.Add(dots);
                Ui.Text(string.IsNullOrEmpty(unit.Name) ? "我" : unit.Name.Substring(0, 1), "sp-partycard__initial", face, FontKind.Black);
            }

            var info = Ui.El("sp-partycard__info", Root);
            Ui.Text(unit.Name, "sp-partycard__name", info, FontKind.Black);
            hpText = Bar(info, "HP", out hpFill, "is-hp");
            spText = Bar(info, "SP", out spFill, "is-sp");
            status = Ui.Text("", "sp-partycard__status", Root, FontKind.Black);
            Refresh();
        }

        static Label Bar(VisualElement parent, string label, out VisualElement fill, string modifier)
        {
            var row = Ui.El("sp-partycard__row " + modifier, parent);
            Ui.Text(label, "sp-partycard__label", row, FontKind.MonoBold);
            var bar = Ui.El("sp-partycard__bar", row);
            fill = Ui.El("sp-partycard__fill", bar);
            return Ui.Text("", "sp-partycard__value", row, FontKind.MonoBold);
        }

        public void Refresh()
        {
            hpFill.style.width = Length.Percent(100f * Unit.Hp / Mathf.Max(1, Unit.MaxHp));
            spFill.style.width = Length.Percent(Unit.MaxSp == 0 ? 0 : 100f * Unit.Sp / Unit.MaxSp);
            hpText.text = $"{Unit.Hp}/{Unit.MaxHp}";
            spText.text = $"{Unit.Sp}/{Unit.MaxSp}";
            status.text = Unit.Charged ? "×2.5" : Unit.Guarding ? "防御" : "";
            Ui.Show(status, status.text.Length > 0);
        }

        public void SetActive(bool on)
        {
            Root.EnableInClassList("is-active", on);
            Root.style.translate = new Translate(0, on ? -26f : 0f);
        }

        public void Shake()
        {
            Anim.Tween(0.3f, t => Root.style.translate = new Translate(Random.Range(-10f, 10f) * (1 - t), Root.ClassListContains("is-active") ? -26f : 0f), Ease.Linear).Forget();
        }
    }
}
