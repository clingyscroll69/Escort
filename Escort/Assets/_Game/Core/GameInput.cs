using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HS.Core
{
    /// <summary>
    /// Code-defined Input System actions (keyboard+mouse and gamepad for every verb, GDD §2).
    /// Keyboard: WASD move, mouse aim, LMB knife, Space dodge, Q/MMB ping, E interact, C/Ctrl crouch, Shift walk,
    /// 1-6 skills, Esc pause, Tab hero insight. Gamepad: LS move, RS aim, X knife, B dodge, Y ping, A interact,
    /// L3 crouch, RT/RB/LT/LB and D-pad left/right skills 1-6, Start pause, Select insight.
    /// </summary>
    public sealed class GameInput : IDisposable
    {
        public readonly InputActionMap Gameplay = new InputActionMap("Gameplay");
        public readonly InputActionMap UI = new InputActionMap("UI");

        public readonly InputAction Move, AimPointer, AimStick, Attack, Dodge, Ping, Interact, Crouch, Walk, Pause, Insight;
        public readonly InputAction[] Skills = new InputAction[6];
        public readonly InputAction Navigate, Confirm, Cancel, Any;
        /// <summary>Skill demos (picker, Field Guide): skip to the end card / play again.</summary>
        public readonly InputAction DemoSkip, DemoReplay;

        public bool UsingGamepad { get; private set; }

        static GameInput _instance;
        public static GameInput Instance => _instance ??= new GameInput();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance?.Dispose();
            _instance = null;
        }

        GameInput()
        {
            Move = Gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick");

            AimPointer = Gameplay.AddAction("AimPointer", InputActionType.PassThrough, "<Pointer>/position", expectedControlLayout: "Vector2");
            AimStick = Gameplay.AddAction("AimStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");

            Attack = Gameplay.AddAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            Attack.AddBinding("<Gamepad>/buttonWest");
            Dodge = Gameplay.AddAction("Dodge", InputActionType.Button, "<Keyboard>/space");
            Dodge.AddBinding("<Gamepad>/buttonEast");
            Ping = Gameplay.AddAction("Ping", InputActionType.Button, "<Keyboard>/q");
            Ping.AddBinding("<Mouse>/middleButton");
            Ping.AddBinding("<Gamepad>/buttonNorth");
            Interact = Gameplay.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            Interact.AddBinding("<Gamepad>/buttonSouth");
            Crouch = Gameplay.AddAction("Crouch", InputActionType.Button, "<Keyboard>/c");
            Crouch.AddBinding("<Keyboard>/leftCtrl");
            Crouch.AddBinding("<Gamepad>/leftStickPress");
            Walk = Gameplay.AddAction("Walk", InputActionType.Button, "<Keyboard>/leftShift");
            Pause = Gameplay.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            Pause.AddBinding("<Gamepad>/start");
            Insight = Gameplay.AddAction("Insight", InputActionType.Button, "<Keyboard>/tab");
            Insight.AddBinding("<Gamepad>/select");

            string[] keys = { "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5", "<Keyboard>/6" };
            string[] pads = { "<Gamepad>/rightTrigger", "<Gamepad>/rightShoulder", "<Gamepad>/leftTrigger", "<Gamepad>/leftShoulder", "<Gamepad>/dpad/left", "<Gamepad>/dpad/right" };
            for (int i = 0; i < Skills.Length; i++)
            {
                Skills[i] = Gameplay.AddAction("Skill" + (i + 1), InputActionType.Button, keys[i]);
                Skills[i].AddBinding(pads[i]);
            }
            Skills[0].AddBinding("<Mouse>/rightButton");

            Navigate = UI.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
            Navigate.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Navigate.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Navigate.AddBinding("<Gamepad>/dpad");
            Navigate.AddBinding("<Gamepad>/leftStick");
            Confirm = UI.AddAction("Confirm", InputActionType.Button, "<Keyboard>/enter");
            Confirm.AddBinding("<Keyboard>/space");
            Confirm.AddBinding("<Keyboard>/e");
            Confirm.AddBinding("<Gamepad>/buttonSouth");
            Cancel = UI.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
            Cancel.AddBinding("<Keyboard>/backspace");
            Cancel.AddBinding("<Gamepad>/buttonEast");
            DemoSkip = UI.AddAction("DemoSkip", InputActionType.Button, "<Keyboard>/f");
            DemoSkip.AddBinding("<Gamepad>/buttonWest");
            DemoReplay = UI.AddAction("DemoReplay", InputActionType.Button, "<Keyboard>/r");
            DemoReplay.AddBinding("<Gamepad>/buttonNorth");
            Any = UI.AddAction("Any", InputActionType.Button, "<Keyboard>/anyKey");
            Any.AddBinding("<Mouse>/leftButton");
            Any.AddBinding("<Gamepad>/buttonSouth");
            Any.AddBinding("<Gamepad>/start");

            Gameplay.Enable();
            UI.Enable();
            InputSystem.onActionChange += OnActionChange;
        }

        void OnActionChange(object obj, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed || obj is not InputAction a || a.activeControl == null) return;
            var device = a.activeControl.device;
            if (device is Gamepad) UsingGamepad = true;
            else if (device is Keyboard || device is Mouse) UsingGamepad = false;
        }

        public void Dispose()
        {
            InputSystem.onActionChange -= OnActionChange;
            Gameplay.Dispose();
            UI.Dispose();
        }
    }
}
