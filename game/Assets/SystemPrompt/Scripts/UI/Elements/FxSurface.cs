using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    public enum FxMask
    {
        None,
        Dissolve,  // on the noise's soft clouds
        Radial,    // a ring opening from the centre, in blocks
        Blocks,    // random blocks of Block px blinking out
        SweepDown, // a ragged line running down the frame
        SweepUp,
        Open,      // a ring opening from the centre with a soft, glowing rim, ragged by the noise's clouds
    }

    // A picture filtered by Resources/Shaders/ScreenFx.shader, redrawn every frame. UI Toolkit in Unity 6.0
    // has no filters or custom materials, so the shader draws into a RenderTexture with Graphics.Blit and an
    // Image shows it. The picture is a texture (Art/Transitions, a scene background) or a grab of the frame as
    // it was a moment ago (Grab). Released with its element.
    public sealed class FxSurface
    {
        static Shader shader;
        static Texture2D noise;

        public readonly Image Element;
        readonly Material material;
        readonly RenderTexture target;
        readonly Vector2 size;
        readonly float born = Time.unscaledTime;
        Texture picture;
        RenderTexture grab;
        Vector4 rect = new Vector4(0f, 0f, 1f, 1f), overRect = new Vector4(0f, 0f, 1f, 1f);
        Vector2 scroll, overScroll;

        // size: the element's size in frame px (the whole frame by default); the texture gets as many
        // screen pixels as the element covers.
        public FxSurface(VisualElement parent, Texture picture, string classes = null, Vector2? size = null)
        {
            if (shader == null) shader = Resources.Load<Shader>("Shaders/ScreenFx");
            if (noise == null) noise = Ui.Tex("Art/Transitions/noise");
            this.size = size ?? new Vector2(GameRoot.Width, GameRoot.Height);
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_Noise", noise);
            material.SetVector("_Res", this.size);
            var scale = Mathf.Min(1f, Mathf.Min(Screen.width / GameRoot.Width, Screen.height / GameRoot.Height));
            target = new RenderTexture(Mathf.Max(8, Mathf.RoundToInt(this.size.x * scale)), Mathf.Max(8, Mathf.RoundToInt(this.size.y * scale)), 0, RenderTextureFormat.ARGB32)
            {
                name = "FxSurface",
                wrapMode = TextureWrapMode.Clamp,
            };
            target.Create();
            Element = Ui.Picture(target, ScaleMode.StretchToFill, "sp-fxsurface " + (classes ?? ""), parent);
            if (size == null) Ui.Fill(Element);
            else
            {
                Element.style.width = this.size.x;
                Element.style.height = this.size.y;
            }
            Element.RegisterCallback<DetachFromPanelEvent>(_ => Release());
            Element.schedule.Execute(Render).Every(0);
            Picture(picture);
        }

        // A grab of the frame as it is drawn at the end of this frame (call before changing anything), shown
        // from the next frame on: what was on screen, ready to be broken up.
        public static async Awaitable<FxSurface> Grab(VisualElement parent, VisualElement frame, string classes = null)
        {
            await Awaitable.EndOfFrameAsync();
            if (parent.panel == null || frame.panel == null) return null; // closed meanwhile
            var rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { name = "Frame grab", wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            var fx = new FxSurface(parent, rt, classes) { grab = rt };
            // Only the frame's part of the window, which may be letterboxed. Panel space runs top-down,
            // texture space bottom-up, and on Metal the grab itself comes out upside down.
            var root = frame.panel.visualTree.layout;
            var box = frame.worldBound;
            var u = box.x / root.width;
            var w = box.width / root.width;
            var h = box.height / root.height;
            var v = 1f - box.yMax / root.height;
            fx.Rect(SystemInfo.graphicsUVStartsAtTop ? new Vector4(u, v + h, w, -h) : new Vector4(u, v, w, h));
            return fx;
        }

        // A texture filling the surface: cropped to its shape, centred.
        public FxSurface Picture(Texture texture)
        {
            picture = texture;
            if (texture == null || grab != null) return this;
            var want = size.x / size.y;
            var have = (float)texture.width / texture.height;
            Rect(have > want
                ? new Vector4((1f - want / have) / 2f, 0f, want / have, 1f)
                : new Vector4(0f, (1f - have / want) / 2f, 1f, have / want));
            return this;
        }

        public FxSurface Rect(Vector4 offsetScale)
        {
            rect = offsetScale;
            return this;
        }

        // Moves the picture through the surface, in texture widths per second (the textures repeat).
        public FxSurface Scroll(Vector2 perSecond)
        {
            scroll = perSecond;
            return this;
        }

        // An image added on top, e.g. light streaks, tiled by offsetScale and moving by perSecond.
        public FxSurface Overlay(Texture texture, float amount, Vector4? offsetScale = null, Vector2 perSecond = default)
        {
            material.SetTexture("_Over", texture);
            material.SetFloat("_OverAmount", amount);
            overRect = offsetScale ?? new Vector4(0f, 0f, 1f, 1f);
            overScroll = perSecond;
            return this;
        }

        public FxSurface Set(string property, float value)
        {
            material.SetFloat(property, value);
            return this;
        }

        public FxSurface Set(string property, Color value)
        {
            material.SetColor(property, value);
            return this;
        }

        public FxSurface Set(string property, Vector4 value)
        {
            material.SetVector(property, value);
            return this;
        }

        public FxSurface Mask(FxMask mask, float progress, Color edge, float edgeWidth = 0.06f)
        {
            material.SetFloat("_Mode", (float)mask);
            material.SetFloat("_Reveal", progress);
            material.SetColor("_Edge", edge);
            material.SetFloat("_EdgeWidth", edgeWidth);
            return this;
        }

        public FxSurface Progress(float progress)
        {
            material.SetFloat("_Reveal", progress);
            return this;
        }

        // Glitch slices that jump to new bands every `seconds`.
        public FxSurface Glitch(float pixels, float seconds = 0.06f)
        {
            material.SetFloat("_Slice", pixels);
            material.SetFloat("_Seed", Mathf.Floor(Time.unscaledTime / seconds) % 97f);
            return this;
        }

        public void Render()
        {
            if (target == null || !target.IsCreated()) return;
            var age = Time.unscaledTime - born;
            material.SetFloat("_FxTime", Time.unscaledTime);
            material.SetVector("_MainRect", rect + (Vector4)(scroll * age));
            material.SetVector("_OverRect", overRect + (Vector4)(overScroll * age));
            var previous = RenderTexture.active;
            Graphics.Blit(picture, target, material);
            RenderTexture.active = previous;
        }

        void Release()
        {
            target.Release();
            Object.Destroy(target);
            if (grab != null)
            {
                grab.Release();
                Object.Destroy(grab);
            }
            Object.Destroy(material);
        }
    }
}
