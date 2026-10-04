using Godot;

namespace Sprawl;

/// <summary>
/// The mixer: effects and music on their own buses under Master, so the
/// settings sliders can turn each down separately.
/// </summary>
///
/// <remarks>
/// Built from code rather than a bus layout resource, like everything else in
/// the project. Call <see cref="Ensure"/> before creating any player that names
/// one of these buses -- a player on a bus that does not exist plays on Master.
/// </remarks>
public static class AudioBuses
{
    public const string Sfx = "Sfx";
    public const string Music = "Music";

    public static void Ensure()
    {
        Add(Sfx);
        Add(Music);
    }

    private static void Add(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        int index = AudioServer.BusCount;
        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
    }

    /// <summary>Set a bus's volume from a 0..1 slider value; 0 mutes it. Works for "Master" too.</summary>
    public static void SetVolume(string bus, float linear01)
    {
        int index = AudioServer.GetBusIndex(bus);
        if (index < 0) return;
        float v = Mathf.Clamp(linear01, 0f, 1f);
        AudioServer.SetBusMute(index, v <= 0.001f);
        // Squared, so the slider's middle sounds like the middle rather than
        // like nearly full: loudness is closer to logarithmic than linear.
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(v * v, 0.00001f)));
    }
}
