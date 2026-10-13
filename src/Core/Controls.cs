using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>Everything a player can do with a key or a button.</summary>
public enum Act
{
    Up, Down, Left, Right, Sneak, Interact, Reload, Dash, Medkit, AbilityQ, AbilityF, Grenade, Whistle, Map,
    Weapon1, Weapon2, Weapon3, Handbrake, Pause, Pda, Rig, Horn, Spell1, Spell2, Spell3,
}

/// <summary>
/// Input, one layer up from keys: actions bound to keys (rebindable in the
/// settings), a gamepad on top (twin-stick on foot, triggers in the car),
/// and -- under <c>--agent</c> -- only what the agent injected, so whoever
/// is sitting at the machine cannot steer the playtester's game.
/// </summary>
public static class Controls
{
    /// <summary>Device id stamped on every event the agent injects.</summary>
    public const int AgentDevice = 77;
    public static readonly HashSet<Key> AgentKeys = new();
    public static bool AgentLeft, AgentRight;
    public static Vector2 AgentMouse = new(800, 450);

    public static readonly Dictionary<Act, Key> Defaults = new()
    {
        [Act.Up] = Key.W, [Act.Down] = Key.S, [Act.Left] = Key.A, [Act.Right] = Key.D, [Act.Sneak] = Key.Shift,
        [Act.Interact] = Key.E, [Act.Reload] = Key.R, [Act.Dash] = Key.Space, [Act.Medkit] = Key.H,
        [Act.AbilityQ] = Key.Q, [Act.AbilityF] = Key.F, [Act.Grenade] = Key.G, [Act.Whistle] = Key.V, [Act.Map] = Key.M,
        [Act.Weapon1] = Key.Key1, [Act.Weapon2] = Key.Key2, [Act.Weapon3] = Key.Key3, [Act.Handbrake] = Key.Space, [Act.Pause] = Key.Escape, [Act.Pda] = Key.P, [Act.Rig] = Key.T,
        [Act.Horn] = Key.H, [Act.Spell1] = Key.Z, [Act.Spell2] = Key.X, [Act.Spell3] = Key.C,
    };

    public static readonly Dictionary<Act, string> Names = new()
    {
        [Act.Up] = "Move up / throttle", [Act.Down] = "Move down / brake", [Act.Left] = "Move left / steer left", [Act.Right] = "Move right / steer right",
        [Act.Sneak] = "Sneak (hold)", [Act.Interact] = "Use / enter", [Act.Reload] = "Reload", [Act.Dash] = "Dash", [Act.Medkit] = "Medkit",
        [Act.AbilityQ] = "Chrome ability 1", [Act.AbilityF] = "Chrome ability 2", [Act.Grenade] = "Next grenade", [Act.Whistle] = "Whistle (lure)", [Act.Map] = "Map",
        [Act.Weapon1] = "Primary", [Act.Weapon2] = "Secondary", [Act.Weapon3] = "Melee", [Act.Handbrake] = "Handbrake", [Act.Pause] = "Pause / back", [Act.Pda] = "PDA (city, safehouse)", [Act.Rig] = "Jack into your drone", [Act.Horn] = "Horn (in the car)",
        [Act.Spell1] = "Spell 1", [Act.Spell2] = "Spell 2", [Act.Spell3] = "Spell 3",
    };

    private static readonly Dictionary<Act, JoyButton> Pad = new()
    {
        [Act.Dash] = JoyButton.A, [Act.Interact] = JoyButton.X, [Act.Medkit] = JoyButton.B, [Act.AbilityQ] = JoyButton.Y,
        [Act.AbilityF] = JoyButton.RightShoulder, [Act.Grenade] = JoyButton.LeftShoulder, [Act.Weapon1] = JoyButton.DpadLeft,
        [Act.Weapon2] = JoyButton.DpadRight, [Act.Weapon3] = JoyButton.DpadUp, [Act.Reload] = JoyButton.DpadDown, [Act.Map] = JoyButton.Back,
        [Act.Pause] = JoyButton.Start, [Act.Whistle] = JoyButton.RightStick, [Act.Handbrake] = JoyButton.A, [Act.Sneak] = JoyButton.LeftStick, [Act.Horn] = JoyButton.B,
        // Buttons shared by context: the right stick whistles on a run and opens the PDA in the car.
        [Act.Pda] = JoyButton.RightStick, [Act.Rig] = JoyButton.Misc1,
    };

    /// <summary>The key bound to an action (the agent always plays on the defaults).</summary>
    public static Key KeyOf(Act a) => !AgentRunner.Active && Profile.Current.Bindings.TryGetValue(a, out Key k) ? k : Defaults[a];

    /// <summary>The key's name as printed on the player's keyboard (bindings are positions: Q on AZERTY is the QWERTY A).</summary>
    public static string KeyName(Act a)
    {
        // The HUD asks every frame: remember each action's name until its key changes (a rebinding).
        Key k = KeyOf(a);
        if (_keyNames.TryGetValue(a, out var known) && known.key == k) return known.name;
        Key label = AgentRunner.Active || !LayoutKeyNames ? Key.None : DisplayServer.KeyboardGetKeycodeFromPhysical(k);
        string name = OS.GetKeycodeString(label != Key.None ? label : k);
        _keyNames[a] = (k, name);
        return name;
    }

    private static readonly Dictionary<Act, (Key key, string name)> _keyNames = new();
    /// <summary>Name keys as this keyboard prints them (off in the walkthrough, whose shots show the default QWERTY names).</summary>
    public static bool LayoutKeyNames = true;

    /// <summary>Is the key held -- by position only, so a layout's letters never fire a second action (AZERTY's Q is Left, not Ability 1).</summary>
    public static bool Down(Key k) => AgentRunner.Active ? AgentKeys.Contains(k) : Input.IsPhysicalKeyPressed(k);

    /// <summary>Is the action held: its key, the arrow keys for movement, or the gamepad.</summary>
    public static bool Down(Act a)
    {
        if (Down(KeyOf(a))) return true;
        if (a == Act.Up && Down(Key.Up) || a == Act.Down && Down(Key.Down) || a == Act.Left && Down(Key.Left) || a == Act.Right && Down(Key.Right)) return true;
        return PadOn && Pad.TryGetValue(a, out JoyButton b) && Input.IsJoyButtonPressed(0, b);
    }

    /// <summary>Did this event just press the action?</summary>
    public static bool Is(InputEvent e, Act a)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k) return (k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode) == KeyOf(a);
        if (e is InputEventJoypadButton { Pressed: true } jb && !AgentRunner.Active) return Pad.TryGetValue(a, out JoyButton b) && jb.ButtonIndex == b;
        return false;
    }

    /// <summary>
    /// Did this event back out of a menu: Escape, the pause binding, or B on a pad.
    /// Where the player types (<paramref name="typing"/>), a pause key rebound to a letter doesn't count.
    /// </summary>
    public static bool Back(InputEvent e, bool typing = false) =>
        e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
        || !typing && e is InputEventKey && Is(e, Act.Pause)
        || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B } && !AgentRunner.Active;

    public static bool PadOn => !AgentRunner.Active && Input.GetConnectedJoypads().Count > 0;

    /// <summary>A stick with a dead zone: left (move/steer) or right (aim).</summary>
    public static V2 Stick(bool left)
    {
        if (!PadOn) return V2.Zero;
        var v = new V2(Input.GetJoyAxis(0, left ? JoyAxis.LeftX : JoyAxis.RightX), Input.GetJoyAxis(0, left ? JoyAxis.LeftY : JoyAxis.RightY));
        return v.Length() < 0.25f ? V2.Zero : v;
    }

    public static float Trigger(bool right) => PadOn ? Input.GetJoyAxis(0, right ? JoyAxis.TriggerRight : JoyAxis.TriggerLeft) : 0;

    public static bool Mouse(MouseButton b) => AgentRunner.Active
        ? b == MouseButton.Left ? AgentLeft : b == MouseButton.Right && AgentRight
        : Input.IsMouseButtonPressed(b);

    public static Vector2 MousePos(Viewport v) => AgentRunner.Active ? AgentMouse : v.GetMousePosition();
}
