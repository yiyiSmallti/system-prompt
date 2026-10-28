using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Shared Persona-style effects. Flashes and shakes honour the accessibility settings.
    public static class Fx
    {
        // Big slammed word over a spiky burst (对抗样本！/ONE MORE!/全员倒地！). Returns once it has settled;
        // the caller decides how long it stays and removes it with Dismiss.
        public static VisualElement Stamp(VisualElement layer, string text, Color color, float fontSize, float angle, Vector2 center, bool burst = true)
        {
            var root = Ui.El("fx-stamp", layer);
            root.style.position = Position.Absolute;
            root.style.left = center.x;
            root.style.top = center.y;
            var inner = Ui.El("fx-stamp__inner", root);
            if (burst)
            {
                var b = new Shape(Shape.Burst(14, 0.62f, text.GetHashCode()), Palette.Ink).Stroke(color, 6);
                b.AddToClassList("fx-stamp__burst");
                inner.Add(b);
            }
            var label = Ui.Text(text, "fx-stamp__text", inner, FontKind.Black);
            label.style.fontSize = fontSize;
            label.style.color = color;
            Ui.Turn(inner, angle);
            AnimateIn(inner).Forget();
            return root;
        }

        static async Awaitable AnimateIn(VisualElement inner)
        {
            await Anim.Tween(0.22f, t => Ui.Zoom(inner, Mathf.Lerp(2.4f, 1f, t)), Ease.OutBack);
        }

        public static async Awaitable Dismiss(VisualElement stamp, float seconds = 0.16f)
        {
            if (stamp == null || stamp.parent == null) return;
            await Ui.FadeOutAndRemove(stamp, seconds);
        }

        public static void Flash(VisualElement layer, Color color, float seconds = 0.18f)
        {
            var settings = GameRoot.Instance.Settings;
            var peak = settings.reduceFlash ? 0.25f : 0.9f;
            var f = Ui.El("fx-flash", layer);
            Ui.Fill(f);
            f.pickingMode = PickingMode.Ignore;
            f.style.backgroundColor = color;
            Anim.Tween(seconds, t => f.style.opacity = peak * (1 - t), Ease.Linear)
                .ContinueRemove(f);
        }

        public static void Shake(VisualElement target, float strength = 14f, float seconds = 0.25f)
        {
            if (!GameRoot.Instance.Settings.screenShake) return;
            Anim.Tween(seconds, t =>
            {
                var k = (1 - t) * strength;
                target.style.translate = new Translate(Random.Range(-k, k), Random.Range(-k, k));
            }, Ease.Linear).Forget();
        }

        static async void ContinueRemove(this Awaitable a, VisualElement e)
        {
            try { await a; }
            finally { e.RemoveFromHierarchy(); }
        }
    }

    // A headline with a red and a cyan copy behind it, pulled apart like a bad signal's colours. Size and
    // font come from the classes (font-size inherits); Split sets how far apart, with a random jump.
    public sealed class SplitText : VisualElement
    {
        readonly Label red, cyan;
        public readonly Label Main;

        public SplitText(string text, string classes, FontKind font)
        {
            AddToClassList("fx-split");
            Ui.AddClasses(this, classes);
            pickingMode = PickingMode.Ignore;
            cyan = Ui.Text(text, "fx-split__ghost is-cyan", this, font);
            red = Ui.Text(text, "fx-split__ghost is-red", this, font);
            Main = Ui.Text(text, "fx-split__main", this, font);
        }

        public string Text
        {
            set => Main.text = red.text = cyan.text = value;
        }

        public void Split(float pixels, float jitter = 0f)
        {
            var j = new Vector2(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter) * 0.4f);
            red.style.translate = new Translate(pixels + j.x, j.y);
            cyan.style.translate = new Translate(-pixels - j.x, -j.y);
            Main.style.translate = new Translate(j.x * 0.35f, 0);
        }
    }
}
