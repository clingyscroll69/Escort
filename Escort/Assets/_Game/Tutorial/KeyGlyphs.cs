using System.Collections.Generic;
using System.Text.RegularExpressions;
using HS.Core;
using UnityEngine.InputSystem;

namespace HS.Tutorial
{
    public enum GlyphDevice { Keyboard, Gamepad }

    /// <summary>
    /// Key and button names for tutorial and HUD text, read from <see cref="GameInput"/>'s real bindings so a rebinding
    /// can never make a tip lie. Copy says "{ping}"; the player reads Q, or Y on a gamepad (whichever they used last).
    /// </summary>
    public static class KeyGlyphs
    {
        static readonly Regex Token = new Regex(@"\{([a-z0-9]+)\}");

        public static GlyphDevice Current => GameInput.Instance.UsingGamepad ? GlyphDevice.Gamepad : GlyphDevice.Keyboard;

        public static string SlotToken(int slot) => "skill" + (slot + 1);

        /// <summary>The label for a token on a device, or null when the token isn't a key.</summary>
        public static string Label(string token, GlyphDevice d)
        {
            bool pad = d == GlyphDevice.Gamepad;
            var input = GameInput.Instance;
            switch (token)
            {
                case "move": return pad ? "LS" : "WASD";
                case "aim": return pad ? "RS" : "Mouse";
                case "skills": return pad ? "RT RB LT LB D-pad ←→" : "1–6";
                case "walk": return pad ? "LS (lightly)" : First(input.Walk, false);
                case "attack": return First(input.Attack, pad);
                case "dodge": return First(input.Dodge, pad);
                case "ping": return First(input.Ping, pad);
                case "interact": return First(input.Interact, pad);
                case "crouch": return First(input.Crouch, pad);
                case "insight": return First(input.Insight, pad);
                case "pause": return First(input.Pause, pad);
                case "confirm": return First(input.Confirm, pad);
                case "cancel": return First(input.Cancel, pad);
                case "demoskip": return First(input.DemoSkip, pad);
                case "demoreplay": return First(input.DemoReplay, pad);
                case "capstone": return First(input.Capstone, pad);
            }
            if (token != null && token.StartsWith("skill") && int.TryParse(token.Substring(5), out int n) && n >= 1 && n <= input.Skills.Length)
                return First(input.Skills[n - 1], pad);
            return null;
        }

        /// <summary>Replace every known {token} with an inline keycap; unknown tokens are left as they are.</summary>
        public static string Format(string text, GlyphDevice d)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Token.Replace(text, m =>
            {
                var label = Label(m.Groups[1].Value, d);
                return label != null ? Chip(label) : m.Value;
            });
        }

        public static string Format(string text) => Format(text, Current);

        /// <summary>Inline keycap: gold label on a faint gold plate (TMP rich text), never split across lines.</summary>
        public static string Chip(string label) =>
            "<nobr><mark=#F2C14E30> <color=#F2C14E><b>" + label + "</b></color> </mark></nobr>";

        static string First(InputAction action, bool pad)
        {
            if (action == null) return null;
            foreach (var b in action.bindings)
            {
                if (b.isComposite || b.isPartOfComposite || string.IsNullOrEmpty(b.path)) continue;
                bool isPad = b.path.StartsWith("<Gamepad>");
                bool isKb = b.path.StartsWith("<Keyboard>") || b.path.StartsWith("<Mouse>") || b.path.StartsWith("<Pointer>");
                if ((pad && isPad) || (!pad && isKb)) return Name(b.path);
            }
            return null;
        }

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "space", "Space" }, { "leftShift", "Shift" }, { "rightShift", "Shift" }, { "leftCtrl", "Ctrl" }, { "rightCtrl", "Ctrl" },
            { "tab", "Tab" }, { "escape", "Esc" }, { "enter", "Enter" }, { "backspace", "Backspace" },
            { "upArrow", "↑" }, { "downArrow", "↓" }, { "leftArrow", "←" }, { "rightArrow", "→" },
            { "leftButton", "LMB" }, { "rightButton", "RMB" }, { "middleButton", "MMB" },
            { "buttonSouth", "A" }, { "buttonEast", "B" }, { "buttonWest", "X" }, { "buttonNorth", "Y" },
            { "leftShoulder", "LB" }, { "rightShoulder", "RB" }, { "leftTrigger", "LT" }, { "rightTrigger", "RT" },
            { "leftStickPress", "L3" }, { "rightStickPress", "R3" }, { "start", "Start" }, { "select", "Select" },
            { "leftStick", "LS" }, { "rightStick", "RS" }, { "dpad", "D-pad" },
        };

        static readonly Dictionary<string, string> DpadArrows = new Dictionary<string, string>
        {
            { "left", "←" }, { "right", "→" }, { "up", "↑" }, { "down", "↓" },
        };

        static string Name(string path)
        {
            int slash = path.LastIndexOf('/');
            if (path.Contains("/dpad/") && DpadArrows.TryGetValue(path.Substring(slash + 1), out var arrow)) return "D-pad " + arrow;
            var control = slash >= 0 ? path.Substring(slash + 1) : path;
            if (Names.TryGetValue(control, out var n)) return n;
            return control.Length == 1 ? control.ToUpperInvariant() : char.ToUpperInvariant(control[0]) + control.Substring(1);
        }
    }
}
