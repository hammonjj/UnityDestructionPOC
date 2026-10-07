using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>
    /// Who is playing, handed from the title menu to the game scene. Each player owns the devices they joined with: a
    /// keyboard + mouse, or one gamepad. When nothing was set (the scene opened straight from the editor, tests) the
    /// game starts with one player on every device, as before.
    /// </summary>
    public static class GameSession
    {
        public const int MaxPlayers = 2;

        public sealed class Slot
        {
            public string label;
            public InputDevice[] devices;
            public bool IsKeyboard => devices.Length > 0 && devices[0] is Keyboard;
        }

        public static readonly List<Slot> Slots = new List<Slot>();

        public static void Clear() => Slots.Clear();

        public static bool Owns(InputDevice device)
        {
            foreach (var s in Slots)
                foreach (var d in s.devices)
                    if (d == device) return true;
            return false;
        }

        /// <summary>Add a keyboard + mouse player. False when full or the keyboard already joined.</summary>
        public static bool JoinKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null || Slots.Count >= MaxPlayers || Owns(kb)) return false;
            var mouse = Mouse.current;
            Slots.Add(new Slot
            {
                label = "Keyboard + mouse",
                devices = mouse != null ? new InputDevice[] { kb, mouse } : new InputDevice[] { kb },
            });
            return true;
        }

        /// <summary>Add a gamepad player. False when full or that gamepad already joined.</summary>
        public static bool JoinGamepad(Gamepad pad)
        {
            if (pad == null || Slots.Count >= MaxPlayers || Owns(pad)) return false;
            Slots.Add(new Slot { label = pad.displayName, devices = new InputDevice[] { pad } });
            return true;
        }

        /// <summary>Drop slots whose devices were unplugged.</summary>
        public static void Prune() => Slots.RemoveAll(s => System.Array.Exists(s.devices, d => d == null || !d.added));

        /// <summary>The devices of player <paramref name="index"/>, or null (= every device) when no session was set up.</summary>
        public static InputDevice[] DevicesFor(int index) => index < Slots.Count ? Slots[index].devices : null;

        /// <summary>Players to spawn: the joined ones, or one on every device when none joined.</summary>
        public static int PlayerCount => Slots.Count > 0 ? Slots.Count : 1;
    }
}
