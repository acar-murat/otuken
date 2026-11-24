using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Otuken
{
    public struct TouchPoint
    {
        public int Id;
        public Vector2 Pos; // screen pixels, origin bottom-left
    }

    /// <summary>
    /// Reads touch, keyboard and mouse through whichever input backend the project uses
    /// (Input System package or the legacy Input Manager), so the game works with either setting.
    /// </summary>
    public static class Controls
    {
        public static readonly List<TouchPoint> Touches = new List<TouchPoint>(10);

        public static void Poll()
        {
            Touches.Clear();
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts == null) return;
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                Touches.Add(new TouchPoint { Id = t.touchId.ReadValue(), Pos = t.position.ReadValue() });
            }
#else
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                Touches.Add(new TouchPoint { Id = t.fingerId, Pos = t.position });
            }
#endif
        }

        public static Vector2 KeyMove()
        {
            Vector2 v = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return v;
            if (k.wKey.isPressed || k.upArrowKey.isPressed) v.y += 1;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) v.y -= 1;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) v.x -= 1;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) v.x += 1;
#else
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v.y += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v.y -= 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) v.x -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) v.x += 1;
#endif
            return v.sqrMagnitude > 1 ? v.normalized : v;
        }

#if ENABLE_INPUT_SYSTEM
        public static bool SkillPressed => Keyboard.current != null && (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.leftShiftKey.wasPressedThisFrame);
        public static bool UltPressed => Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
        public static bool PausePressed => Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.pKey.wasPressedThisFrame);
        public static bool FireKeyHeld => Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        public static bool MousePresent => Mouse.current != null;
        public static bool MouseHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool MouseClicked => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static Vector2 MousePos => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        public static bool SkillPressed => Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.LeftShift);
        public static bool UltPressed => Input.GetKeyDown(KeyCode.Q);
        public static bool PausePressed => Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P);
        public static bool FireKeyHeld => Input.GetKey(KeyCode.Space);
        public static bool MousePresent => Input.mousePresent;
        public static bool MouseHeld => Input.GetMouseButton(0);
        public static bool MouseClicked => Input.GetMouseButtonDown(0);
        public static Vector2 MousePos => Input.mousePosition;
#endif
    }
}
