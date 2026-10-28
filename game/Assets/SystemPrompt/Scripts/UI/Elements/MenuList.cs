using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Keyboard/gamepad/mouse navigation over a list of elements. The selected element gets the
    // "is-selected" class; USS handles the look.
    public sealed class MenuList
    {
        readonly List<VisualElement> items = new List<VisualElement>();
        readonly List<Action> actions = new List<Action>();
        readonly List<bool> enabled = new List<bool>();

        public int Index { get; private set; } = -1;
        public bool Horizontal;
        public bool Wrap = true;
        public int Columns = 1; // > 1 turns it into a grid: Left/Right move within a row
        public Action<VisualElement, bool> Styler; // for looks USS can't express, e.g. recoloring a Shape
        public event Action<int> SelectionChanged;

        public int Count => items.Count;
        public VisualElement this[int i] => items[i];
        public VisualElement Selected => Index >= 0 && Index < items.Count ? items[Index] : null;

        public void Add(VisualElement item, Action onActivate, bool isEnabled = true)
        {
            var i = items.Count;
            items.Add(item);
            actions.Add(onActivate);
            enabled.Add(isEnabled);
            item.AddToClassList("sp-item");
            item.EnableInClassList("is-disabled", !isEnabled);
            item.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (i < enabled.Count && enabled[i] && Index != i) Select(i);
            });
            item.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                if (i >= enabled.Count) return;
                if (!enabled[i])
                {
                    GameRoot.Instance.Audio.Sfx("crack", 0.5f);
                    return;
                }
                Select(i, false);
                Activate();
            });
            Styler?.Invoke(item, false);
            if (Index < 0 && isEnabled) Select(i, false);
        }

        public void Clear()
        {
            items.Clear();
            actions.Clear();
            enabled.Clear();
            Index = -1;
        }

        public void Deselect()
        {
            if (Index >= 0 && Index < items.Count)
            {
                items[Index].RemoveFromClassList("is-selected");
                Styler?.Invoke(items[Index], false);
            }
            Index = -1;
        }

        public void SetEnabled(int i, bool on)
        {
            enabled[i] = on;
            items[i].EnableInClassList("is-disabled", !on);
        }

        public void Select(int i, bool sound = true)
        {
            if (i < 0 || i >= items.Count) return;
            if (Index >= 0 && Index < items.Count)
            {
                items[Index].RemoveFromClassList("is-selected");
                Styler?.Invoke(items[Index], false);
            }
            Index = i;
            items[i].AddToClassList("is-selected");
            Styler?.Invoke(items[i], true);
            if (sound) GameRoot.Instance.Audio.Sfx("cursor");
            SelectionChanged?.Invoke(i);
        }

        public void Move(int delta)
        {
            if (items.Count == 0) return;
            var i = Index;
            for (var tries = 0; tries < items.Count; tries++)
            {
                i += delta;
                if (i < 0 || i >= items.Count)
                {
                    if (!Wrap) return;
                    i = (i + items.Count) % items.Count;
                }
                if (enabled[i])
                {
                    Select(i);
                    return;
                }
            }
        }

        public void Activate()
        {
            if (Index < 0 || Index >= actions.Count || !enabled[Index]) return;
            GameRoot.Instance.Audio.Sfx("confirm");
            actions[Index]?.Invoke();
        }

        // Returns true when the input was used.
        public bool HandleInput()
        {
            var prev = Horizontal ? Btn.Left : Btn.Up;
            var next = Horizontal ? Btn.Right : Btn.Down;
            if (Columns > 1)
            {
                if (GameInput.Consume(Btn.Up)) { Move(-Columns); return true; }
                if (GameInput.Consume(Btn.Down)) { Move(Columns); return true; }
                if (GameInput.Consume(Btn.Left)) { Move(-1); return true; }
                if (GameInput.Consume(Btn.Right)) { Move(1); return true; }
            }
            else
            {
                if (GameInput.Consume(prev)) { Move(-1); return true; }
                if (GameInput.Consume(next)) { Move(1); return true; }
            }
            if (GameInput.Consume(Btn.Confirm)) { Activate(); return true; }
            return false;
        }
    }

    public interface IInputLayer
    {
        void HandleInput();
    }

    // Full-screen layer above the current screen that takes all input until closed.
    // Opening fades in the backdrop and throws the content in Persona-style (from the left, tilted,
    // overshooting), with the Cascade elements sliding in one after another. Closing hands input back
    // at once and plays the exit before the layer is removed.
    // The pages behind the pause menu (存档, 设置, 回看, 手机) each draw their own screen and play their
    // own entrance and exit through PlayMotion.
    public abstract class Modal : IInputLayer
    {
        const float Stagger = 0.05f;
        const float CascadeStagger = 0.035f;

        protected readonly GameRoot Game;
        public readonly VisualElement Root;
        public bool IsOpen { get; private set; }

        readonly VisualElement backdrop;
        readonly bool decorated;
        int animation;

        // Elements that slide in one by one after the content, e.g. a menu's items.
        protected virtual IEnumerable<VisualElement> Cascade => null;
        // Content that animates its own entrance (the phone) skips the shared one.
        protected virtual bool OwnEntrance => false;
        // Content that animates its own exit (the pause menu) gets this many seconds before the layer goes.
        protected virtual float OwnExit => 0f;
        // Full-screen menus and pages hide the story's own UI underneath; small dialogs leave it.
        public virtual bool CoversScreen => true;

        int motion;

        // decorated: moving stripes and drifting data bits behind the content; small dialogs only dim.
        protected Modal(GameRoot game, string classes, bool decorated = true)
        {
            Game = game;
            Root = Ui.El("sp-modal " + classes);
            Ui.Fill(Root);
            Root.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            backdrop = Ui.El("sp-modal__backdrop", Root);
            Ui.Fill(backdrop);
            backdrop.pickingMode = PickingMode.Ignore;
            this.decorated = decorated;
            if (decorated) Decorate(backdrop);
        }

        static void Decorate(VisualElement backdrop)
        {
            var stripes = Ui.El("sp-modal__stripes", backdrop);
            Ui.El("sp-modal__stripe is-wide", stripes);
            var band = Ui.El("sp-modal__stripe is-band", stripes);
            var dots = new Halftone(Palette.Green.WithAlpha(0.3f), 18f, 5f);
            Ui.Fill(dots);
            band.Add(dots);
            Ui.El("sp-modal__stripe is-thin", stripes);
            ParticleField.Drift(stripes, 60f, 1f);
            var particles = ParticleField.Create(Ambient.Data);
            Ui.Fill(particles);
            backdrop.Add(particles);
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            Game.OpenModal(this);
            OnOpen();
            AnimateIn(++animation).Forget();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Game.CloseModal(this);
            OnClose();
            AnimateOut(++animation).Forget();
        }

        public abstract void HandleInput();
        protected virtual void OnOpen() { }
        protected virtual void OnClose() { }
        // The modal above this one closed and input is back here.
        protected virtual void OnRegain() { }

        internal void Regain()
        {
            if (IsOpen) OnRegain();
        }

        // Runs step(seconds elapsed) each frame for a page's own entrance or exit. The next PlayMotion cuts
        // this one short (its step is not called again); returns whether it ran to the end.
        protected async Awaitable<bool> PlayMotion(float seconds, Action<float> step)
        {
            var id = ++motion;
            await Anim.Tween(seconds, t =>
            {
                if (id == motion) step(t * seconds);
            }, Ease.Linear, () => id != motion);
            return id == motion;
        }

        List<VisualElement> Content()
        {
            var list = new List<VisualElement>();
            foreach (var child in Root.Children())
                if (child != backdrop) list.Add(child);
            return list;
        }

        async Awaitable AnimateIn(int id)
        {
            var content = OwnEntrance ? new List<VisualElement>() : Content();
            if (decorated && !OwnEntrance) Tear(id).Forget();
            var items = new List<VisualElement>(Cascade ?? Array.Empty<VisualElement>());
            var total = Mathf.Max(0.16f, (content.Count - 1) * Stagger + 0.26f, 0.08f + (items.Count - 1) * CascadeStagger + 0.2f);
            await Anim.Tween(total, t =>
            {
                var time = t * total;
                backdrop.style.opacity = Mathf.Clamp01(time / 0.16f);
                for (var i = 0; i < content.Count; i++) Pose(content[i], Ease.OutBack(Mathf.Clamp01((time - i * Stagger) / 0.26f)));
                for (var i = 0; i < items.Count; i++)
                {
                    var k = Mathf.Clamp01((time - 0.08f - i * CascadeStagger) / 0.2f);
                    items[i].style.left = (1 - Ease.OutCubic(k)) * -140f;
                    items[i].style.opacity = k;
                }
            }, Ease.Linear, () => id != animation);
            if (id != animation) return;
            backdrop.style.opacity = StyleKeyword.Null;
            foreach (var c in content) ClearPose(c);
            foreach (var item in items)
            {
                item.style.left = StyleKeyword.Null;
                item.style.opacity = StyleKeyword.Null;
            }
        }

        // The screen behind tears as the dialog is thrown in: a grab of it (taken before the dialog shows)
        // under the stripes, dimmed like the backdrop, its colours split and its rows thrown sideways, then
        // settling back into the live screen.
        async Awaitable Tear(int id)
        {
            var fx = await FxSurface.Grab(backdrop, Game.Frame);
            if (fx == null) return;
            fx.Element.SendToBack();
            var calm = Game.Settings.reduceFlash;
            await Anim.Tween(0.32f, t =>
            {
                fx.Set("_Tint", new Color(0.28f, 0.28f, 0.28f)).Set("_Add", Palette.Ink * 0.72f)
                    .Set("_Split", 26f * (1 - t)).Glitch(calm ? 0f : 80f * (1 - t)).Set("_Zoom", 1f + 0.04f * (1 - t)).Set("_Scan", 0.5f * (1 - t));
            }, Ease.OutCubic, () => id != animation);
            fx.Element.RemoveFromHierarchy();
        }

        async Awaitable AnimateOut(int id)
        {
            var content = OwnExit > 0f ? new List<VisualElement>() : Content();
            await Anim.Tween(Mathf.Max(0.16f, OwnExit), t =>
            {
                backdrop.style.opacity = 1 - t;
                foreach (var c in content)
                {
                    c.style.translate = new Translate(t * 220f, t * -24f);
                    c.style.opacity = 1 - t;
                }
            }, Ease.InCubic, () => id != animation);
            if (id != animation) return; // opened again meanwhile
            Root.RemoveFromHierarchy();
            backdrop.style.opacity = StyleKeyword.Null;
            foreach (var c in content) ClearPose(c);
        }

        // k: 0 = thrown in from the lower left, 1 = in place (OutBack overshoots a little past 1).
        static void Pose(VisualElement e, float k)
        {
            var away = 1 - k;
            e.style.translate = new Translate(away * -160f, away * 40f);
            Ui.Turn(e, away * -6f);
            Ui.Zoom(e, Mathf.Lerp(0.94f, 1f, k));
            e.style.opacity = Mathf.Clamp01(k * 1.6f);
        }

        static void ClearPose(VisualElement e)
        {
            e.style.translate = StyleKeyword.Null;
            e.style.rotate = StyleKeyword.Null;
            e.style.scale = StyleKeyword.Null;
            e.style.opacity = StyleKeyword.Null;
        }
    }
}
