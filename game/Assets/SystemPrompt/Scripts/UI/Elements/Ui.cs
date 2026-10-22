using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Small factory helpers: the whole UI is built in code and styled by Resources/UI/SystemPrompt.uss.
    public static class Ui
    {
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        public static VisualElement El(string classes = null, VisualElement parent = null)
        {
            var e = new VisualElement();
            AddClasses(e, classes);
            parent?.Add(e);
            return e;
        }

        public static Label Text(string text, string classes = null, VisualElement parent = null, FontKind font = FontKind.Regular)
        {
            var l = new Label(text);
            AddClasses(l, classes);
            Fonts.Apply(l, font);
            parent?.Add(l);
            return l;
        }

        const string Opening = "「『（《【〔“‘";
        const string Closing = "。，、！？；：」』）》】〕”’…—～";

        // The text engine wraps Chinese between any two characters, so a 。 can land alone at the start of a
        // line. Each run of opening marks, the character after it and the closing marks after that are put in
        // a <nobr> so they wrap as one. Rich-text tags are copied through untouched.
        public static string GluePunctuation(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length + 32);
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] == '<')
                {
                    var close = text.IndexOf('>', i);
                    if (close < 0) close = text.Length - 1;
                    sb.Append(text, i, close - i + 1);
                    i = close + 1;
                    continue;
                }
                var start = i;
                while (i < text.Length && Opening.IndexOf(text[i]) >= 0) i++;
                if (i < text.Length && text[i] != '<' && !char.IsWhiteSpace(text[i]) && Closing.IndexOf(text[i]) < 0) i++;
                while (i < text.Length && Closing.IndexOf(text[i]) >= 0) i++;
                if (i == start) i++;
                if (i - start > 1) sb.Append("<nobr>").Append(text, start, i - start).Append("</nobr>");
                else sb.Append(text, start, i - start);
            }
            return sb.ToString();
        }

        public static Image Picture(Texture texture, ScaleMode mode, string classes = null, VisualElement parent = null)
        {
            var img = new Image { image = texture, scaleMode = mode, pickingMode = PickingMode.Ignore };
            AddClasses(img, classes);
            parent?.Add(img);
            return img;
        }

        // A window onto a 1024x1536 portrait, zoomed so the face fills it (contacts, Coop cards, party cards).
        // faceY is the face centre as a fraction of the portrait height.
        public static VisualElement FaceCrop(string textureKey, float width, float height, string classes = null, VisualElement parent = null, float zoom = 2.2f, float faceY = 0.19f)
        {
            var window = El(classes, parent);
            window.style.width = width;
            window.style.height = height;
            window.style.overflow = Overflow.Hidden;
            var imageWidth = width * zoom;
            var imageHeight = imageWidth * 1.5f;
            var image = Picture(Tex(textureKey), ScaleMode.ScaleToFit, "sp-facecrop__image", window);
            image.style.position = Position.Absolute;
            image.style.width = imageWidth;
            image.style.height = imageHeight;
            image.style.left = (width - imageWidth) / 2f;
            image.style.top = height / 2f - imageHeight * faceY;
            return window;
        }

        public static Texture2D Tex(string key)
        {
            if (textures.TryGetValue(key, out var t)) return t;
            t = Resources.Load<Texture2D>(key);
            if (t == null) Debug.LogError($"找不到图片 Resources/{key}");
            textures[key] = t;
            return t;
        }

        public static void AddClasses(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (var c in classes.Split(' '))
                if (c.Length > 0) e.AddToClassList(c);
        }

        public static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        public static bool Visible(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None;

        public static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0;
            e.style.top = 0;
            e.style.right = 0;
            e.style.bottom = 0;
        }

        public static void Offset(VisualElement e, float x, float y) => e.style.translate = new Translate(x, y);

        public static void Turn(VisualElement e, float degrees) => e.style.rotate = new Rotate(new Angle(degrees, AngleUnit.Degree));

        public static void Zoom(VisualElement e, float s) => e.style.scale = new Scale(new Vector2(s, s));

        public static void Opacity(VisualElement e, float a) => e.style.opacity = a;

        public static void TextColor(VisualElement e, Color c) => e.style.color = c;

        public static void Bg(VisualElement e, Color c) => e.style.backgroundColor = c;

        // Remove an element once its fade-out finishes.
        public static async Awaitable FadeOutAndRemove(VisualElement e, float seconds)
        {
            var start = e.resolvedStyle.opacity;
            await Anim.Tween(seconds, t => e.style.opacity = start * (1 - t));
            e.RemoveFromHierarchy();
        }
    }
}
