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
}
