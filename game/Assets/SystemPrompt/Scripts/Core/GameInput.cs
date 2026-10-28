using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SystemPrompt
{
    public enum Btn
    {
        Confirm, Cancel, Up, Down, Left, Right,
        Auto, Skip, Log, Hide, Phone, QuickSave, QuickLoad, Menu, Delete,
        Interact, // the latent space: a left click while the cursor is locked, besides Confirm
        Undo,     // the latent space: take back the last push, or turn a light plate back
    }

    // Polled once per frame by GameRoot. Works with either input backend, since a project created
    // from scratch may run on the legacy Input Manager, the Input System, or both.
    public static class GameInput
    {
        const float RepeatDelay = 0.35f;
        const float RepeatInterval = 0.07f;

        static readonly Btn[] Repeating = { Btn.Up, Btn.Down, Btn.Left, Btn.Right };
        static readonly float[] heldFor = new float[4];
        static readonly bool[] repeatFired = new bool[4];
        static float nextRepeat;

        // While a text field has focus, letters and space belong to the text field.
        public static bool TextEntryActive;

        public static float ScrollY { get; private set; }

        // Walking in the latent space: x right, y forward, from WASD / the arrows / the left stick.
        public static Vector2 Move { get; private set; }
        // Turning the camera: -1..1 from Q/E or the right stick, and the mouse while the cursor is locked
        // (pixels this frame).
        public static float Turn { get; private set; }
        public static Vector2 Look { get; private set; }
        public static bool SprintHeld { get; private set; }

        static readonly bool[] pressed = new bool[System.Enum.GetValues(typeof(Btn)).Length];

        public static void Update()
        {
            for (var i = 0; i < pressed.Length; i++) pressed[i] = RawPressed((Btn)i);
            ScrollY = RawScroll();
            ReadSticks();

            for (var i = 0; i < Repeating.Length; i++)
            {
                var b = Repeating[i];
                if (!RawHeld(b))
                {
                    heldFor[i] = 0;
                    repeatFired[i] = false;
                    continue;
                }
                heldFor[i] += Time.unscaledDeltaTime;
                if (heldFor[i] < RepeatDelay) continue;
                if (!repeatFired[i] || Time.unscaledTime >= nextRepeat)
                {
                    pressed[(int)b] = true;
                    repeatFired[i] = true;
                    nextRepeat = Time.unscaledTime + RepeatInterval;
                }
            }
        }

        public static bool Pressed(Btn b) => pressed[(int)b];

        public static bool Consume(Btn b)
        {
            if (!pressed[(int)b]) return false;
            pressed[(int)b] = false;
            return true;
        }

        public static bool SkipHeld => RawHeld(Btn.Skip) && !TextEntryActive;

#if ENABLE_INPUT_SYSTEM
        static bool Hit(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
        static bool Hold(Key k) => Keyboard.current != null && Keyboard.current[k].isPressed;

        static bool RawPressed(Btn b)
        {
            var pad = Gamepad.current;
            var text = TextEntryActive;
            switch (b)
            {
                case Btn.Confirm: return Hit(Key.Enter) || Hit(Key.NumpadEnter) || (!text && Hit(Key.Space)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
                case Btn.Cancel: return Hit(Key.Escape) || (!text && Hit(Key.Backspace)) || (pad != null && pad.buttonEast.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
                case Btn.Menu: return Hit(Key.Escape) || (pad != null && pad.startButton.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
                case Btn.Up: return Hit(Key.UpArrow) || (!text && Hit(Key.W)) || (pad != null && pad.dpad.up.wasPressedThisFrame);
                case Btn.Down: return Hit(Key.DownArrow) || (!text && Hit(Key.S)) || (pad != null && pad.dpad.down.wasPressedThisFrame);
                case Btn.Left: return Hit(Key.LeftArrow) || (!text && Hit(Key.A)) || (pad != null && pad.dpad.left.wasPressedThisFrame);
                case Btn.Right: return Hit(Key.RightArrow) || (!text && Hit(Key.D)) || (pad != null && pad.dpad.right.wasPressedThisFrame);
                case Btn.Auto: return !text && (Hit(Key.Q) || (pad != null && pad.buttonWest.wasPressedThisFrame));
                case Btn.Skip: return !text && Hit(Key.F);
                case Btn.Log: return !text && (Hit(Key.L) || (pad != null && pad.leftShoulder.wasPressedThisFrame));
                case Btn.Hide: return !text && Hit(Key.H);
                case Btn.Phone: return !text && (Hit(Key.Tab) || (pad != null && pad.buttonNorth.wasPressedThisFrame));
                case Btn.QuickSave: return Hit(Key.F5);
                case Btn.QuickLoad: return Hit(Key.F9);
                case Btn.Delete: return Hit(Key.Delete) || (pad != null && pad.selectButton.wasPressedThisFrame);
                case Btn.Undo: return (!text && Hit(Key.Backspace)) || (pad != null && pad.buttonEast.wasPressedThisFrame);
                case Btn.Interact: return Cursor.lockState == CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            }
            return false;
        }

        static void ReadSticks()
        {
            var pad = Gamepad.current;
            var text = TextEntryActive;
            var move = Vector2.zero;
            if (!text)
            {
                if (Hold(Key.W) || Hold(Key.UpArrow)) move.y += 1f;
                if (Hold(Key.S) || Hold(Key.DownArrow)) move.y -= 1f;
                if (Hold(Key.D) || Hold(Key.RightArrow)) move.x += 1f;
                if (Hold(Key.A) || Hold(Key.LeftArrow)) move.x -= 1f;
            }
            if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > move.sqrMagnitude) move = pad.leftStick.ReadValue();
            Move = Vector2.ClampMagnitude(move, 1f);

            var turn = 0f;
            if (!text && Hold(Key.Q)) turn -= 1f;
            if (!text && Hold(Key.E)) turn += 1f;
            if (pad != null && Mathf.Abs(pad.rightStick.ReadValue().x) > Mathf.Abs(turn)) turn = pad.rightStick.ReadValue().x;
            Turn = turn;
            Look = Cursor.lockState == CursorLockMode.Locked && Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            SprintHeld = !text && (Hold(Key.LeftShift) || Hold(Key.RightShift) || (pad != null && (pad.leftStickButton.isPressed || pad.rightTrigger.isPressed)));
        }

        static bool RawHeld(Btn b)
        {
            var pad = Gamepad.current;
            switch (b)
            {
                case Btn.Up: return Hold(Key.UpArrow) || (!TextEntryActive && Hold(Key.W)) || (pad != null && pad.dpad.up.isPressed);
                case Btn.Down: return Hold(Key.DownArrow) || (!TextEntryActive && Hold(Key.S)) || (pad != null && pad.dpad.down.isPressed);
                case Btn.Left: return Hold(Key.LeftArrow) || (!TextEntryActive && Hold(Key.A)) || (pad != null && pad.dpad.left.isPressed);
                case Btn.Right: return Hold(Key.RightArrow) || (!TextEntryActive && Hold(Key.D)) || (pad != null && pad.dpad.right.isPressed);
                case Btn.Skip: return Hold(Key.LeftCtrl) || Hold(Key.RightCtrl) || (pad != null && pad.rightShoulder.isPressed);
            }
            return false;
        }

        static float RawScroll()
        {
            if (Mouse.current == null) return 0;
            var y = Mouse.current.scroll.ReadValue().y;
            return Mathf.Abs(y) < 0.01f ? 0 : Mathf.Sign(y);
        }
#else
        static bool Hit(KeyCode k) => Input.GetKeyDown(k);
        static bool Hold(KeyCode k) => Input.GetKey(k);

        static bool RawPressed(Btn b)
        {
            var text = TextEntryActive;
            switch (b)
            {
                case Btn.Confirm: return Hit(KeyCode.Return) || Hit(KeyCode.KeypadEnter) || (!text && Hit(KeyCode.Space));
                case Btn.Cancel: return Hit(KeyCode.Escape) || (!text && Hit(KeyCode.Backspace)) || Input.GetMouseButtonDown(1);
                case Btn.Menu: return Hit(KeyCode.Escape) || Input.GetMouseButtonDown(1);
                case Btn.Up: return Hit(KeyCode.UpArrow) || (!text && Hit(KeyCode.W));
                case Btn.Down: return Hit(KeyCode.DownArrow) || (!text && Hit(KeyCode.S));
                case Btn.Left: return Hit(KeyCode.LeftArrow) || (!text && Hit(KeyCode.A));
                case Btn.Right: return Hit(KeyCode.RightArrow) || (!text && Hit(KeyCode.D));
                case Btn.Auto: return !text && Hit(KeyCode.Q);
                case Btn.Skip: return !text && Hit(KeyCode.F);
                case Btn.Log: return !text && Hit(KeyCode.L);
                case Btn.Hide: return !text && Hit(KeyCode.H);
                case Btn.Phone: return !text && Hit(KeyCode.Tab);
                case Btn.QuickSave: return Hit(KeyCode.F5);
                case Btn.QuickLoad: return Hit(KeyCode.F9);
                case Btn.Delete: return Hit(KeyCode.Delete);
                case Btn.Undo: return !text && Hit(KeyCode.Backspace);
                case Btn.Interact: return Cursor.lockState == CursorLockMode.Locked && Input.GetMouseButtonDown(0);
            }
            return false;
        }

        static void ReadSticks()
        {
            var text = TextEntryActive;
            var move = Vector2.zero;
            if (!text)
            {
                if (Hold(KeyCode.W) || Hold(KeyCode.UpArrow)) move.y += 1f;
                if (Hold(KeyCode.S) || Hold(KeyCode.DownArrow)) move.y -= 1f;
                if (Hold(KeyCode.D) || Hold(KeyCode.RightArrow)) move.x += 1f;
                if (Hold(KeyCode.A) || Hold(KeyCode.LeftArrow)) move.x -= 1f;
            }
            Move = Vector2.ClampMagnitude(move, 1f);
            Turn = (!text && Hold(KeyCode.E) ? 1f : 0f) - (!text && Hold(KeyCode.Q) ? 1f : 0f);
            Look = Cursor.lockState == CursorLockMode.Locked ? new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f : Vector2.zero;
            SprintHeld = !text && (Hold(KeyCode.LeftShift) || Hold(KeyCode.RightShift));
        }

        static bool RawHeld(Btn b)
        {
            switch (b)
            {
                case Btn.Up: return Hold(KeyCode.UpArrow) || (!TextEntryActive && Hold(KeyCode.W));
                case Btn.Down: return Hold(KeyCode.DownArrow) || (!TextEntryActive && Hold(KeyCode.S));
                case Btn.Left: return Hold(KeyCode.LeftArrow) || (!TextEntryActive && Hold(KeyCode.A));
                case Btn.Right: return Hold(KeyCode.RightArrow) || (!TextEntryActive && Hold(KeyCode.D));
                case Btn.Skip: return Hold(KeyCode.LeftControl) || Hold(KeyCode.RightControl);
            }
            return false;
        }

        static float RawScroll()
        {
            var y = Input.mouseScrollDelta.y;
            return Mathf.Abs(y) < 0.01f ? 0 : Mathf.Sign(y);
        }
#endif
    }
}
