using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // A portrait the player can poke (title screen, phone profile): head pats and body pokes get a
    // face and a line from CharacterDef, and poking too often gets the TooManyTouches line. It moves on its
    // own (Portraits.CreateAnimated) and can follow the pointer with its eyes (LookAt).
    // Given a bubble layer (the title, where several portraits stand side by side), the speech bubble goes
    // there instead of inside the portrait, so no other portrait covers it; it then sits at BubbleSpot with
    // its tail aimed at her face.
    public sealed class InteractivePortrait
    {
        const float TailLength = 54f;
        const float TailWidth = 34f;

        const int TooManyCount = 6;
        const float TooManyWindow = 5f;
        const float ReactionSeconds = 2.8f;

        static readonly Color Dimmed = new Color(0.72f, 0.74f, 0.8f, 1f);

        public readonly VisualElement Root;
        public readonly CharacterDef Def;
        public event Action<InteractivePortrait> Hovered;
        public event Action<InteractivePortrait> Unhovered;
        public event Action<InteractivePortrait> Spoke;
        // Top left of the bubble in the bubble layer.
        public Vector2 BubbleSpot;

        readonly PortraitView view;
        readonly VisualElement bubble;
        readonly Label bubbleText;
        readonly Func<string> address;
        readonly bool floating;
        Vector2 tailAim; // bubble-local point the tail points at
        readonly float[] recentTouches = new float[TooManyCount];
        int touchCursor;
        int headIndex;
        int bodyIndex;
        int reactionId;
        float squash;
        float phase;

        // `address` is what the character calls the player in the TooManyTouches line ({0}).
        public InteractivePortrait(CharacterDef def, VisualElement parent, float height, Func<string> address, VisualElement bubbleLayer = null)
        {
            Def = def;
            this.address = address;
            phase = UnityEngine.Random.value * 10f;
            Root = Ui.El("sp-touch", parent);
            Root.pickingMode = PickingMode.Ignore;
            Root.style.position = Position.Absolute;
            Root.style.height = height;
            Root.style.width = height * 1024f / 1536f;

            view = Portraits.CreateAnimated(def, def.DefaultFace);
            view.Element.AddToClassList("sp-touch__image");
            Root.Add(view.Element);

            var head = Ui.El("sp-touch__head", Root);
            var body = Ui.El("sp-touch__body", Root);
            head.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); Touch(true); });
            body.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); Touch(false); });
            foreach (var zone in new[] { head, body })
            {
                zone.RegisterCallback<PointerEnterEvent>(_ => Hovered?.Invoke(this));
                zone.RegisterCallback<PointerLeaveEvent>(_ => Unhovered?.Invoke(this));
            }

            floating = bubbleLayer != null;
            bubble = Ui.El(floating ? "sp-touch__bubble is-floating" : "sp-touch__bubble", bubbleLayer ?? Root);
            Shape.Backdrop(bubble, floating ? BubbleWithTail : Shape.Crooked(10), Palette.Paper).Stroke(Palette.Ink, 3).Shadow(new Vector2(6, 6), def.Color);
            bubbleText = Ui.Text("", "sp-touch__bubble-text", bubble, FontKind.Bold);
            Ui.Show(bubble, false);

            // The bubble may sit over a body: clicks go through it to the hit zones.
            bubble.pickingMode = PickingMode.Ignore;
            bubble.Query().ForEach(e => e.pickingMode = PickingMode.Ignore);

            Root.schedule.Execute(Animate).Every(33);
        }

        public void SetHighlight(bool? on)
        {
            view.Tint = on == false ? Dimmed : Color.white;
        }

        public void LookAt(Vector2 panelPosition) => view.LookAt(panelPosition);

        public void ResetFace()
        {
            reactionId++;
            view.SetFace(Def.DefaultFace);
            view.Talk(0f);
            Ui.Show(bubble, false);
        }

        void Touch(bool isHead)
        {
            var now = Time.unscaledTime;
            recentTouches[touchCursor] = now;
            touchCursor = (touchCursor + 1) % TooManyCount;
            var tooMany = Array.TrueForAll(recentTouches, t => t > 0 && now - t < TooManyWindow);

            TouchReaction reaction;
            if (tooMany && Def.TooManyTouches != null)
            {
                reaction = Def.TooManyTouches;
                Array.Clear(recentTouches, 0, recentTouches.Length);
            }
            else
            {
                var list = isHead ? Def.HeadTouches : Def.BodyTouches;
                if (list.Length == 0) return;
                var i = isHead ? headIndex++ : bodyIndex++;
                reaction = list[i % list.Length];
            }

            var audio = GameRoot.Instance.Audio;
            audio.Sfx(isHead ? "coin" : "key", 0.7f);
            squash = 1f;
            view.Poke();
            React(reaction).Forget();
        }

        async Awaitable React(TouchReaction reaction)
        {
            var id = ++reactionId;
            view.SetFace(reaction.Face);
            bubbleText.text = string.Format(reaction.Line, address());
            view.Talk(Mathf.Min(bubbleText.text.Length / 14f, 2.4f));
            if (floating) PlaceBubble();
            Ui.Show(bubble, true);
            Spoke?.Invoke(this);
            await Anim.Tween(0.16f, t =>
            {
                bubble.style.opacity = t;
                Ui.Zoom(bubble, Mathf.Lerp(0.7f, 1f, t));
            }, Ease.OutBack);
            await Anim.Wait(ReactionSeconds);
            if (id != reactionId) return;
            await Anim.Tween(0.2f, t => bubble.style.opacity = 1 - t);
            if (id != reactionId) return;
            Ui.Show(bubble, false);
            view.SetFace(Def.DefaultFace);
        }

        void PlaceBubble()
        {
            bubble.style.left = BubbleSpot.x;
            bubble.style.top = BubbleSpot.y;
            var face = (Def.Rig?.Face ?? new Vector2(512f, 330f)) * (Root.layout.height / 1536f);
            tailAim = Root.ChangeCoordinatesTo(bubble.parent, face) - BubbleSpot;
            bubble[0].MarkDirtyRepaint();
        }

        // The crooked box with a tail on its bottom edge, leaning toward the speaker.
        Vector2[] BubbleWithTail(Rect r)
        {
            var box = Shape.Crooked(10)(r);
            Vector2 bottomRight = box[2], bottomLeft = box[3];
            var x = Mathf.Clamp(tailAim.x, r.xMin + 60f, r.xMax - 60f);
            Vector2 Edge(float at) => Vector2.Lerp(bottomLeft, bottomRight, Mathf.InverseLerp(bottomLeft.x, bottomRight.x, at));
            var root = Edge(x);
            var toward = (tailAim - root).normalized;
            toward.y = Mathf.Max(toward.y, 0.45f); // always out of the bottom, even when she stands beside the bubble
            var tip = root + toward.normalized * TailLength;
            return new[] { box[0], box[1], bottomRight, Edge(x + TailWidth / 2f), tip, Edge(x - TailWidth / 2f), bottomLeft };
        }

        // A squash-and-stretch hop after a touch, plus idle breathing for a portrait that does not move by itself.
        void Animate()
        {
            squash = Mathf.Max(0f, squash - 0.08f);
            var hop = Mathf.Sin(squash * Mathf.PI);
            var breath = view.HasIdleMotion ? 0f : Mathf.Sin(Time.unscaledTime * 1.7f + phase) * 0.005f;
            view.Element.style.scale = new Scale(new Vector2(1f - hop * 0.03f, 1f + breath + hop * 0.04f));
            view.Element.style.translate = new Translate(0, -hop * 18f);
        }
    }
}
