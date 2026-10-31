using System;
using UnityEngine.InputSystem;
using static Skybound.FlightMath;

namespace Skybound
{
    /// <summary>
    /// Keyboard controls, plus a gamepad or joystick if one is plugged in. Keyboard
    /// axes ease toward the key target instead of snapping, so a tap gives a small,
    /// smooth deflection like a real stick.
    ///
    ///   Up / W     push (nose down)        Down / S    pull (nose up)
    ///   Left / A   roll left               Right / D   roll right
    ///   Q / E      rudder left / right     B           brakes (hold)
    ///   + / PgUp   throttle up             - / PgDn    throttle down
    ///   [ / ]      trim nose down / up
    ///
    /// Gamepad: left stick flies, right stick X is rudder, triggers are throttle,
    /// south button is brakes. Joystick: stick flies, trigger is brakes.
    /// </summary>
    public class KeyboardAxes
    {
        const double AXIS_RATE = 2.5; // full deflection in 0.4 s
        const double RETURN_RATE = 4; // back to centre in 0.25 s
        // Pitch is far more sensitive than roll on a light aircraft: a quick tap
        // should nudge the nose, not slam the elevator (full deflection in ~1.1 s).
        const double ELEVATOR_RATE = 0.9;
        const double ELEVATOR_RETURN = 2.5;
        // Some devices report as joysticks and rest off-centre; ignore small readings.
        const float JOYSTICK_DEADZONE = 0.15f;

        public double elevator;
        public double aileron;
        public double rudder;
        /// <summary>-1, 0 or +1 while a throttle key is held.</summary>
        public int throttleDir;
        /// <summary>-1, 0 or +1 while a trim key is held.</summary>
        public int trimDir;
        public double brake;
        /// <summary>Name of the gamepad or joystick currently adding input, or null.</summary>
        public string activeDevice;
        /// <summary>On-screen buttons held down: they act like holding the key.</summary>
        public bool holdFaster, holdSlower, holdBrake;

        double keyElevator, keyAileron, keyRudder;

        public static double Approach(double value, double target, double dt, double rate = AXIS_RATE, double returnRate = RETURN_RATE)
        {
            var step = (target == 0 ? returnRate : rate) * dt;
            return Math.Abs(target - value) <= step ? target : value + Sign(target - value) * step;
        }

        static int Dir(bool pos, bool neg) => (pos ? 1 : 0) - (neg ? 1 : 0);

        public void Update(double dt)
        {
            var kb = Keyboard.current;
            bool Held(Key a, Key b = Key.None, Key c = Key.None) =>
                kb != null && (kb[a].isPressed || (b != Key.None && kb[b].isPressed) || (c != Key.None && kb[c].isPressed));

            keyElevator = Approach(keyElevator, Dir(Held(Key.DownArrow, Key.S), Held(Key.UpArrow, Key.W)), dt, ELEVATOR_RATE, ELEVATOR_RETURN);
            keyAileron = Approach(keyAileron, Dir(Held(Key.RightArrow, Key.D), Held(Key.LeftArrow, Key.A)), dt);
            keyRudder = Approach(keyRudder, Dir(Held(Key.E), Held(Key.Q)), dt);
            throttleDir = Dir(holdFaster || Held(Key.Equals, Key.NumpadPlus, Key.PageUp), holdSlower || Held(Key.Minus, Key.NumpadMinus, Key.PageDown));
            trimDir = Dir(Held(Key.RightBracket), Held(Key.LeftBracket));
            brake = holdBrake || Held(Key.B) ? 1 : 0;

            elevator = keyElevator;
            aileron = keyAileron;
            rudder = keyRudder;
            activeDevice = null;
            ReadGamepad();
            ReadJoystick();
        }

        void ReadGamepad()
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            var stick = pad.leftStick.ReadValue();
            var yaw = pad.rightStick.x.ReadValue();
            double lt = pad.leftTrigger.ReadValue(), rt = pad.rightTrigger.ReadValue();
            var pressed = pad.buttonSouth.isPressed;
            aileron += stick.x;
            elevator -= stick.y; // stick back = nose up
            rudder += yaw;
            if (rt > 0.5) throttleDir = 1;
            else if (lt > 0.5) throttleDir = -1;
            if (pressed) brake = 1;
            if (stick.sqrMagnitude > 0 || yaw != 0 || lt > 0.5 || rt > 0.5 || pressed) activeDevice = pad.displayName;
        }

        void ReadJoystick()
        {
            var stickDevice = Joystick.current;
            if (stickDevice == null) return;
            var stick = stickDevice.stick.ReadValue();
            if (stick.magnitude < JOYSTICK_DEADZONE) stick = default;
            var pressed = stickDevice.trigger.isPressed;
            aileron += stick.x;
            elevator -= stick.y;
            if (pressed) brake = 1;
            if (stick.sqrMagnitude > 0 || pressed) activeDevice = stickDevice.displayName;
        }
    }
}
