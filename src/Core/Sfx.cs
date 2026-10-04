using System;
using Godot;

namespace Sprawl;

public enum Sound
{
    UiMove, UiSelect, UiBack, Error, Coin, Key,
    Pistol, Silenced, Smg, Shotgun, Rifle, Sniper, Lmg, Rocket, Fireball, EnemyShot,
    Swing, Slash, Hit, Crit, Death, Glass, Debris, Explosion, Bounce, Throw, Reload, Empty, Door,
    Pickup, Alarm, Alert, Dash, Heal, Hack, Ability, Flash, Emp, Ignite, Beep, Charge, Radio, Objective,
    Crash, Bump, Honk, Knock, Scrape, Victory, Fail, Siren, Step, Jingle, Scream, Ring,
}

/// <summary>
/// Every sound effect, synthesised at startup from short recipes of tones,
/// noise and struck partials. No audio files. Null-safe through
/// <see cref="Instance"/>, so the headless self-check needs no case of its own.
/// Also owns the engine: one looping tone whose pitch follows the car.
/// </summary>
public partial class Sfx : Node
{
    public const int Rate = 22050;
    private const int PoolSize = 24;
    private const float Tau = MathF.Tau;

    public static Sfx Instance { get; private set; }

    private AudioStreamWav[] _streams;
    private AudioStreamPlayer[] _flat;
    private AudioStreamPlayer _engine, _rain, _voice;
    private int _next;
    private ulong[] _last;
    private readonly Random _rng = new();

    public override void _EnterTree()
    {
        Instance = this;
        AudioBuses.Ensure();
        Sound[] all = Enum.GetValues<Sound>();
        _streams = new AudioStreamWav[all.Length];
        _last = new ulong[all.Length];
        foreach (Sound s in all) _streams[(int)s] = ToWav(Synthesize(s));
        _flat = new AudioStreamPlayer[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            _flat[i] = new AudioStreamPlayer { Bus = AudioBuses.Sfx };
            AddChild(_flat[i]);
        }
        _engine = new AudioStreamPlayer { Bus = AudioBuses.Sfx, Stream = Loop(EngineLoop()), VolumeDb = -80 };
        AddChild(_engine);
        _rain = new AudioStreamPlayer { Bus = AudioBuses.Sfx, Stream = Loop(RainLoop()), VolumeDb = -80 };
        AddChild(_rain);
        _voice = new AudioStreamPlayer { Bus = AudioBuses.Sfx };
        AddChild(_voice);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        foreach (AudioStreamPlayer p in _flat) { p.Stop(); p.Stream = null; }
        _engine.Stop();
        _rain.Stop();
        _voice.Stop();
    }

    public void Play(Sound s, float pitch = 1f, float volume = 1f)
    {
        if (_streams == null || volume <= 0.01f) return;
        int i = (int)s;
        ulong now = Time.GetTicksMsec();
        bool rapid = s is Sound.Smg or Sound.Lmg or Sound.Rifle or Sound.EnemyShot or Sound.Hit or Sound.Debris or Sound.Key;
        if (_last[i] != 0 && now - _last[i] < (ulong)(rapid ? 35 : 25)) return;
        _last[i] = now;
        pitch *= 1f + ((float)_rng.NextDouble() * 2f - 1f) * 0.06f;
        AudioStreamPlayer p = _flat[_next];
        _next = (_next + 1) % PoolSize;
        p.Stream = _streams[i];
        p.PitchScale = Mathf.Max(0.05f, pitch);
        p.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(volume, 0.0001f, 2f));
        p.Play();
    }

    /// <summary>
    /// Speak a line (see <see cref="Assets.Voice"/>), cutting off the last one;
    /// null just stops it. One voice at a time, on the effects bus.
    /// </summary>
    public bool Speaking => _voice != null && _voice.Playing;

    public void Voice(AudioStream line)
    {
        if (_voice == null) return;
        _voice.Stop();
        if (line == null) return;
        _voice.Stream = line;
        _voice.Play();
    }

    /// <summary>Engine note: 0 = off; speed01 0..1 raises the pitch.</summary>
    public void Engine(float volume, float speed01)
    {
        if (_engine == null) return;
        if (volume <= 0.01f) { if (_engine.Playing) _engine.Stop(); return; }
        if (!_engine.Playing) _engine.Play();
        _engine.VolumeDb = Mathf.LinearToDb(volume);
        _engine.PitchScale = 0.7f + speed01 * 1.3f;
    }

    public void Rain(float volume)
    {
        if (_rain == null) return;
        if (volume <= 0.01f) { if (_rain.Playing) _rain.Stop(); return; }
        if (!_rain.Playing) _rain.Play();
        _rain.VolumeDb = Mathf.LinearToDb(volume);
    }

    private static AudioStreamWav ToWav(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcm = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32000f);
            bytes[i * 2] = (byte)(pcm & 0xFF);
            bytes[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }
        return new AudioStreamWav { Data = bytes, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false };
    }

    private static AudioStreamWav Loop(float[] samples)
    {
        AudioStreamWav w = ToWav(samples);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = samples.Length;
        return w;
    }

    private static float[] EngineLoop()
    {
        // Whole cycles of 60 Hz in a quarter second so the loop is seamless.
        float[] b = New(0.25f);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float v = 0;
            v += MathF.Sin(Tau * 60 * t) * 0.6f;
            v += (2 * ((60 * 2 * t) % 1) - 1) * 0.25f;
            v += MathF.Sin(Tau * 180 * t) * 0.15f;
            v *= 0.75f + 0.25f * MathF.Sin(Tau * 24 * t);
            b[i] = v;
        }
        Lowpass(b, 0.25f);
        return Normalise(b, 0.5f);
    }

    private static float[] RainLoop()
    {
        float[] b = New(2f);
        Noise(b, 0, 2f, 0.35f, 0.35f, false, 0.5f, seed: 77);
        Noise(b, 0, 2f, 0.6f, 0.6f, true, 0.25f, seed: 78);
        Crackle(b, 0, 2f, 120, 0.15f, 79);
        // Cross-fade the ends so the loop does not click.
        int f = Rate / 10;
        for (int i = 0; i < f; i++)
        {
            float u = i / (float)f;
            b[i] = b[i] * u + b[b.Length - f + i] * (1 - u);
        }
        Array.Resize(ref b, b.Length - f);
        return Normalise(b, 0.5f);
    }

    private static float[] Normalise(float[] b, float peak)
    {
        float max = 0;
        foreach (float v in b) max = MathF.Max(max, MathF.Abs(v));
        if (max > 1e-6f) for (int i = 0; i < b.Length; i++) b[i] *= peak / max;
        return b;
    }

    private static float[] Gun(int seed, float crack, float body, float len, float peak, float tail = 0.3f)
    {
        float[] b = New(len);
        Noise(b, 0f, len, 0.9f, 0.2f, true, crack, decay: 30f / len, seed: seed);
        Noise(b, 0f, len, 0.25f, 0.05f, false, body, decay: 12f / len, seed: seed + 1);
        Tone(b, 0f, len * 0.6f, Exp(160f, 50f), Wave.Sine, body * 0.8f, decay: 18f / len);
        Echo(b, 0.06f, tail);
        Saturate(b, 1.6f);
        return Finish(b, peak);
    }

    internal static float[] Synthesize(Sound s)
    {
        int seed = 4242 + (int)s * 7919;
        float[] b;
        switch (s)
        {
            case Sound.UiMove:
                b = New(0.04f);
                Tone(b, 0f, 0.04f, Flat(1800f), Wave.Square, 0.3f, decay: 80f, duty: 0.3f);
                return Finish(b, 0.18f);
            case Sound.UiSelect:
                b = New(0.12f);
                Tone(b, 0f, 0.05f, Flat(1200f), Wave.Square, 0.5f, decay: 40f, duty: 0.25f);
                Tone(b, 0.05f, 0.07f, Flat(1800f), Wave.Square, 0.5f, decay: 35f, duty: 0.25f);
                return Finish(b, 0.25f);
            case Sound.UiBack:
                b = New(0.12f);
                Tone(b, 0f, 0.05f, Flat(1400f), Wave.Square, 0.5f, decay: 40f, duty: 0.25f);
                Tone(b, 0.05f, 0.07f, Flat(900f), Wave.Square, 0.5f, decay: 35f, duty: 0.25f);
                return Finish(b, 0.22f);
            case Sound.Error:
                b = New(0.22f);
                Tone(b, 0f, 0.2f, Flat(140f), Wave.Saw, 0.6f, decay: 8f);
                Tone(b, 0f, 0.2f, Flat(147f), Wave.Saw, 0.5f, decay: 8f);
                return Finish(b, 0.3f);
            case Sound.Coin:
                b = New(0.3f);
                Tone(b, 0f, 0.08f, Flat(1568f), Wave.Square, 0.4f, decay: 20f, duty: 0.25f);
                Tone(b, 0.07f, 0.22f, Flat(2093f), Wave.Square, 0.4f, decay: 12f, duty: 0.25f);
                return Finish(b, 0.28f);
            case Sound.Key:
                b = New(0.03f);
                Noise(b, 0f, 0.03f, 0.9f, 0.9f, true, 0.6f, decay: 160f, seed: seed);
                Tone(b, 0f, 0.02f, Flat(2400f), Wave.Square, 0.2f, decay: 200f);
                return Finish(b, 0.15f);

            case Sound.Pistol: return Gun(seed, 0.9f, 0.7f, 0.22f, 0.6f);
            case Sound.Silenced:
                b = New(0.12f);
                Noise(b, 0f, 0.1f, 0.3f, 0.1f, false, 0.8f, decay: 50f, seed: seed);
                Tone(b, 0f, 0.05f, Exp(400f, 120f), Wave.Sine, 0.4f, decay: 60f);
                return Finish(b, 0.35f);
            case Sound.Smg: return Gun(seed, 0.8f, 0.5f, 0.14f, 0.5f, 0.15f);
            case Sound.Shotgun: return Gun(seed, 0.9f, 1.2f, 0.45f, 0.75f, 0.4f);
            case Sound.Rifle: return Gun(seed, 1f, 0.8f, 0.2f, 0.6f, 0.25f);
            case Sound.Sniper:
                b = Gun(seed, 1.2f, 1f, 0.7f, 0.8f, 0.5f);
                return b;
            case Sound.Lmg: return Gun(seed, 0.9f, 0.9f, 0.18f, 0.6f, 0.2f);
            case Sound.EnemyShot: return Gun(seed, 0.7f, 0.5f, 0.16f, 0.4f, 0.2f);
            case Sound.Rocket:
                b = New(0.6f);
                Noise(b, 0f, 0.6f, 0.1f, 0.5f, false, 1f, attack: 0.02f, decay: 4f, seed: seed);
                Tone(b, 0f, 0.4f, Exp(90f, 200f), Wave.Saw, 0.3f, decay: 6f);
                return Finish(b, 0.6f);
            case Sound.Fireball:
                b = New(0.5f);
                Noise(b, 0f, 0.5f, 0.05f, 0.3f, false, 1f, attack: 0.05f, decay: 5f, seed: seed);
                Tone(b, 0f, 0.5f, Exp(220f, 660f), Wave.Tri, 0.25f, decay: 5f, vibHz: 18, vibDepth: 0.1f);
                return Finish(b, 0.5f);

            case Sound.Swing:
                b = New(0.16f);
                Noise(b, 0f, 0.16f, 0.1f, 0.6f, false, 0.8f, attack: 0.04f, decay: 14f, seed: seed);
                return Finish(b, 0.3f);
            case Sound.Slash:
                b = New(0.2f);
                Noise(b, 0f, 0.15f, 0.3f, 0.8f, true, 0.7f, decay: 20f, seed: seed);
                Tone(b, 0f, 0.1f, Exp(300f, 90f), Wave.Sine, 0.6f, decay: 30f);
                return Finish(b, 0.45f);
            case Sound.Hit:
                b = New(0.12f);
                Noise(b, 0f, 0.1f, 0.35f, 0.08f, false, 0.9f, decay: 40f, seed: seed);
                Tone(b, 0f, 0.08f, Exp(220f, 80f), Wave.Sine, 0.6f, decay: 40f);
                return Finish(b, 0.4f);
            case Sound.Crit:
                b = New(0.2f);
                Partials(b, 0f, 0.2f, 1600f, new[] { 1f, 2.7f, 4.1f }, 0.5f, 20f);
                Noise(b, 0f, 0.08f, 0.4f, 0.1f, false, 0.8f, decay: 40f, seed: seed);
                return Finish(b, 0.45f);
            case Sound.Death:
                b = New(0.5f);
                Noise(b, 0f, 0.3f, 0.2f, 0.04f, false, 0.8f, decay: 10f, seed: seed);
                Tone(b, 0f, 0.4f, Exp(180f, 60f), Wave.Saw, 0.3f, decay: 8f);
                Lowpass(b, 0.3f);
                return Finish(b, 0.45f);
            case Sound.Glass:
                b = New(0.6f);
                Partials(b, 0f, 0.6f, 2600f, new[] { 1f, 1.43f, 2.17f, 2.9f, 3.7f }, 0.5f, 7f);
                Crackle(b, 0f, 0.4f, 90, 0.6f, seed);
                Noise(b, 0f, 0.15f, 0.8f, 0.6f, true, 0.6f, decay: 25f, seed: seed);
                return Finish(b, 0.45f);
            case Sound.Debris:
                b = New(0.35f);
                Noise(b, 0f, 0.3f, 0.2f, 0.05f, false, 0.9f, decay: 12f, seed: seed);
                Crackle(b, 0f, 0.3f, 60, 0.5f, seed + 1);
                return Finish(b, 0.4f);
            case Sound.Explosion:
                b = New(1.4f);
                Noise(b, 0f, 1.4f, 0.5f, 0.015f, false, 1.2f, decay: 3.2f, seed: seed);
                Noise(b, 0f, 0.15f, 0.9f, 0.3f, true, 0.8f, decay: 20f, seed: seed + 1);
                Tone(b, 0f, 0.8f, Exp(90f, 28f), Wave.Sine, 1f, decay: 4f);
                Crackle(b, 0.05f, 0.9f, 70, 0.3f, seed + 2);
                Saturate(b, 2f);
                return Finish(b, 0.9f);
            case Sound.Bounce:
                b = New(0.08f);
                Partials(b, 0f, 0.08f, 900f, new[] { 1f, 2.3f }, 0.6f, 50f);
                return Finish(b, 0.25f);
            case Sound.Throw:
                b = New(0.2f);
                Noise(b, 0f, 0.2f, 0.05f, 0.3f, false, 0.6f, attack: 0.05f, decay: 12f, seed: seed);
                return Finish(b, 0.25f);
            case Sound.Reload:
                b = New(0.4f);
                Partials(b, 0f, 0.06f, 1300f, new[] { 1f, 2.2f }, 0.5f, 60f);
                Partials(b, 0.22f, 0.08f, 900f, new[] { 1f, 2.5f, 3.3f }, 0.6f, 50f);
                Noise(b, 0.22f, 0.05f, 0.6f, 0.6f, true, 0.4f, decay: 80f, seed: seed);
                return Finish(b, 0.35f);
            case Sound.Empty:
                b = New(0.06f);
                Partials(b, 0f, 0.06f, 2000f, new[] { 1f, 1.7f }, 0.5f, 80f);
                return Finish(b, 0.25f);
            case Sound.Door:
                b = New(0.3f);
                Noise(b, 0f, 0.3f, 0.05f, 0.15f, false, 0.6f, attack: 0.03f, decay: 8f, seed: seed);
                Tone(b, 0f, 0.25f, Exp(120f, 160f), Wave.Tri, 0.2f, decay: 8f);
                return Finish(b, 0.25f);
            case Sound.Pickup:
                b = New(0.18f);
                Arpeggio(b, new[] { Hz(76), Hz(83), Hz(88) }, 0.045f, 0.08f, Wave.Square, 0.4f, 25f, 0.25f);
                return Finish(b, 0.3f);
            case Sound.Alarm:
            {
                b = New(1.6f);
                for (int k = 0; k < 4; k++)
                    Tone(b, k * 0.4f, 0.38f, k % 2 == 0 ? Exp(700f, 1100f) : Exp(1100f, 700f), Wave.Square, 0.4f, duty: 0.4f);
                Lowpass(b, 0.5f);
                return Finish(b, 0.4f);
            }
            case Sound.Alert:
                b = New(0.2f);
                Tone(b, 0f, 0.08f, Flat(880f), Wave.Square, 0.4f, decay: 10f, duty: 0.3f);
                Tone(b, 0.08f, 0.12f, Flat(1320f), Wave.Square, 0.4f, decay: 10f, duty: 0.3f);
                return Finish(b, 0.3f);
            case Sound.Dash:
                b = New(0.22f);
                Noise(b, 0f, 0.22f, 0.1f, 0.7f, false, 0.8f, attack: 0.02f, decay: 10f, seed: seed);
                Tone(b, 0f, 0.2f, Exp(200f, 900f), Wave.Sine, 0.2f, decay: 10f);
                return Finish(b, 0.3f);
            case Sound.Heal:
                b = New(0.5f);
                Arpeggio(b, new[] { Hz(64), Hz(68), Hz(71), Hz(76) }, 0.08f, 0.2f, Wave.Tri, 0.5f, 6f);
                return Finish(b, 0.35f);
            case Sound.Hack:
            {
                b = New(0.5f);
                var r = new Random(seed);
                for (int k = 0; k < 10; k++) Tone(b, k * 0.045f, 0.04f, Flat(600 + r.Next(1800)), Wave.Square, 0.4f, decay: 40f, duty: 0.3f);
                return Finish(b, 0.3f);
            }
            case Sound.Ability:
                b = New(0.6f);
                Tone(b, 0f, 0.6f, Exp(200f, 1200f), Wave.Saw, 0.3f, attack: 0.05f, decay: 4f);
                Tone(b, 0f, 0.6f, Exp(203f, 1210f), Wave.Saw, 0.3f, attack: 0.05f, decay: 4f);
                Lowpass(b, 0.4f);
                return Finish(b, 0.4f);
            case Sound.Flash:
                b = New(0.9f);
                Noise(b, 0f, 0.1f, 0.9f, 0.5f, true, 1f, decay: 20f, seed: seed);
                Tone(b, 0f, 0.9f, Flat(4000f), Wave.Sine, 0.3f, attack: 0.05f, decay: 3f);
                return Finish(b, 0.7f);
            case Sound.Emp:
                b = New(0.8f);
                Tone(b, 0f, 0.8f, Exp(1600f, 60f), Wave.Square, 0.4f, decay: 3f, duty: 0.3f);
                Crackle(b, 0f, 0.6f, 120, 0.4f, seed);
                return Finish(b, 0.5f);
            case Sound.Ignite:
                b = New(0.8f);
                Noise(b, 0f, 0.8f, 0.05f, 0.4f, false, 0.9f, attack: 0.08f, decay: 3f, seed: seed);
                Crackle(b, 0.1f, 0.7f, 50, 0.4f, seed + 1);
                return Finish(b, 0.45f);
            case Sound.Beep:
                b = New(0.1f);
                Tone(b, 0f, 0.1f, Flat(2200f), Wave.Sine, 0.5f, decay: 20f);
                return Finish(b, 0.2f);
            case Sound.Charge:
                b = New(0.7f);
                Tone(b, 0f, 0.7f, Exp(300f, 1400f), Wave.Sine, 0.4f, attack: 0.2f);
                return Finish(b, 0.25f);
            case Sound.Radio:
            {
                b = New(0.7f);
                Noise(b, 0f, 0.7f, 0.5f, 0.5f, true, 0.4f, seed: seed, amHz: 9);
                var r = new Random(seed);
                for (int k = 0; k < 8; k++) Tone(b, 0.05f + k * 0.075f, 0.06f, Flat(300 + r.Next(300)), Wave.Saw, 0.3f, decay: 10f);
                Lowpass(b, 0.5f);
                return Finish(b, 0.3f);
            }
            case Sound.Objective:
                b = New(0.7f);
                Arpeggio(b, new[] { Hz(72), Hz(76), Hz(79), Hz(84) }, 0.1f, 0.35f, Wave.Square, 0.4f, 5f, 0.3f);
                return Finish(b, 0.35f);
            case Sound.Crash:
                b = New(0.6f);
                Noise(b, 0f, 0.5f, 0.4f, 0.05f, false, 1f, decay: 7f, seed: seed);
                Partials(b, 0f, 0.5f, 420f, new[] { 1f, 2.3f, 3.7f, 5.1f }, 0.5f, 9f);
                Crackle(b, 0f, 0.4f, 50, 0.4f, seed + 1);
                return Finish(b, 0.6f);
            case Sound.Bump:
                b = New(0.3f);
                Noise(b, 0f, 0.25f, 0.2f, 0.05f, false, 0.9f, decay: 14f, seed: seed);
                Partials(b, 0f, 0.25f, 300f, new[] { 1f, 2.1f }, 0.4f, 16f);
                return Finish(b, 0.45f);
            case Sound.Honk:
                b = New(0.4f);
                Tone(b, 0f, 0.38f, Flat(370f), Wave.Square, 0.4f, duty: 0.4f);
                Tone(b, 0f, 0.38f, Flat(466f), Wave.Square, 0.3f, duty: 0.4f);
                Lowpass(b, 0.3f);
                return Finish(b, 0.3f);
            case Sound.Knock:
                b = New(0.2f);
                Noise(b, 0f, 0.15f, 0.2f, 0.05f, false, 0.8f, decay: 25f, seed: seed);
                Tone(b, 0.02f, 0.15f, Exp(600f, 900f), Wave.Tri, 0.2f, decay: 12f);
                return Finish(b, 0.35f);
            case Sound.Scrape:
                b = New(0.25f);
                Noise(b, 0f, 0.25f, 0.6f, 0.6f, true, 0.6f, attack: 0.02f, decay: 10f, seed: seed, amHz: 40);
                return Finish(b, 0.2f);
            case Sound.Jingle:
            {
                // "Greensleeves", more or less, on a tinny music box.
                b = New(2.6f);
                int[] tune = { 69, 72, 74, 76, 77, 76, 74, 71, 67, 69, 71, 72, 69, 69, 68, 69, 71, 68, 64 };
                for (int k = 0; k < tune.Length; k++) Tone(b, k * 0.13f, 0.16f, Flat(Hz(tune[k] + 12)), Wave.Square, 0.35f, decay: 14f, duty: 0.2f);
                Echo(b, 0.09f, 0.25f);
                return Finish(b, 0.3f);
            }
            case Sound.Step:
                b = New(0.07f);
                Noise(b, 0f, 0.07f, 0.15f, 0.05f, false, 0.8f, decay: 60f, seed: seed);
                Tone(b, 0f, 0.05f, Exp(110f, 70f), Wave.Sine, 0.4f, decay: 60f);
                return Finish(b, 0.18f);
            case Sound.Ring:
            {
                // A commlink's trill: two quick bursts of a warbling two-tone chirp.
                b = New(0.75f);
                for (int burst = 0; burst < 2; burst++)
                    for (int k = 0; k < 6; k++)
                        Tone(b, burst * 0.38f + k * 0.045f, 0.04f, Flat(k % 2 == 0 ? 1180f : 1480f), Wave.Square, 0.3f, duty: 0.35f);
                Lowpass(b, 0.55f);
                return Finish(b, 0.3f);
            }
            case Sound.Scream:
            {
                // A voice, not a siren: a quick rise, a wavering hold, a fall, with breath under it.
                b = New(0.9f);
                Func<float, float> pitch = u => (u < 0.15f ? 520f + 900f * u / 0.15f : u < 0.6f ? 1420f : 1420f - 700f * (u - 0.6f) / 0.4f) * (1 + 0.04f * MathF.Sin(u * 90f));
                Tone(b, 0f, 0.9f, pitch, Wave.Saw, 0.35f, decay: 1.2f);
                Tone(b, 0f, 0.9f, u => pitch(u) * 2.01f, Wave.Sine, 0.12f, decay: 1.2f);
                Noise(b, 0f, 0.9f, 0.12f, 0.6f, false, 0.6f, attack: 0.03f, decay: 2f, seed: seed);
                Lowpass(b, 0.45f);
                return Finish(b, 0.32f);
            }
            case Sound.Siren:
                b = New(1.2f);
                Tone(b, 0f, 0.6f, Exp(650f, 950f), Wave.Square, 0.35f, duty: 0.45f);
                Tone(b, 0.6f, 0.6f, Exp(950f, 650f), Wave.Square, 0.35f, duty: 0.45f);
                Lowpass(b, 0.35f);
                return Finish(b, 0.3f);
            case Sound.Victory:
                b = New(1.2f);
                Arpeggio(b, new[] { Hz(69), Hz(72), Hz(76), Hz(81), Hz(84) }, 0.12f, 0.5f, Wave.Saw, 0.3f, 3f);
                Lowpass(b, 0.4f);
                Echo(b, 0.18f, 0.3f);
                return Finish(b, 0.4f);
            case Sound.Fail:
                b = New(1.2f);
                Arpeggio(b, new[] { Hz(64), Hz(63), Hz(60), Hz(57) }, 0.22f, 0.5f, Wave.Saw, 0.3f, 2.5f);
                Lowpass(b, 0.35f);
                return Finish(b, 0.4f);
        }
        return New(0.05f);
    }

    private enum Wave { Sine, Tri, Square, Saw }

    private static float[] New(float seconds) => new float[Mathf.Max(2, (int)(seconds * Rate))];

    private static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

    private static Func<float, float> Flat(float hz) => _ => hz;

    /// <summary>An exponential glide from <paramref name="from"/> to <paramref name="to"/> over the note.</summary>
    private static Func<float, float> Exp(float from, float to) => u => from * MathF.Pow(to / from, u);

    /// <summary>
    /// An oscillator added into <paramref name="b"/>. <paramref name="hz"/> is
    /// a function of how far through the note we are (0..1); phase is
    /// integrated sample by sample, so a glide lands where it says it will.
    /// </summary>
    private static void Tone(float[] b, float start, float dur, Func<float, float> hz, Wave wave, float amp,
        float attack = 0.002f, float decay = 0f, float duty = 0.5f, float vibHz = 0f, float vibDepth = 0f, float amHz = 0f)
    {
        int s0 = (int)(start * Rate), n = Mathf.Max(1, (int)(dur * Rate));
        float release = Rate * 0.004f;
        double phase = 0;
        for (int i = 0; i < n && s0 + i < b.Length; i++)
        {
            float t = i / (float)Rate, u = i / (float)n;
            float f = hz(u);
            if (vibHz > 0f) f *= 1f + vibDepth * MathF.Sin(Tau * vibHz * t);
            phase += f / Rate;
            float p = (float)(phase - Math.Floor(phase));
            float v = wave switch
            {
                Wave.Sine => MathF.Sin(Tau * p),
                Wave.Tri => 4f * MathF.Abs(p - 0.5f) - 1f,
                Wave.Square => p < duty ? 1f : -1f,
                _ => 2f * p - 1f,
            };
            float env = MathF.Min(1f, t / MathF.Max(attack, 1e-4f)) * MathF.Exp(-decay * t) * MathF.Min(1f, (n - i) / release);
            if (amHz > 0f) env *= 0.5f + 0.5f * MathF.Sin(Tau * amHz * t);
            b[s0 + i] += v * env * amp;
        }
    }

    /// <summary>
    /// Filtered white noise. The one-pole low-pass cutoff glides from
    /// <paramref name="cut0"/> to <paramref name="cut1"/> (0..1 per sample);
    /// <paramref name="high"/> keeps the residue above it instead. The low band
    /// is scaled back up by its expected loss, so a dark rumble and a bright
    /// hiss at the same <paramref name="amp"/> are about as loud as each other.
    /// </summary>
    private static void Noise(float[] b, float start, float dur, float cut0, float cut1, bool high, float amp,
        float attack = 0.002f, float decay = 0f, int seed = 1, float amHz = 0f)
    {
        var rng = new Random(seed);
        int s0 = (int)(start * Rate), n = Mathf.Max(1, (int)(dur * Rate));
        float release = Rate * 0.004f;
        float low = 0f;
        for (int i = 0; i < n && s0 + i < b.Length; i++)
        {
            float t = i / (float)Rate, u = i / (float)n;
            float white = (float)rng.NextDouble() * 2f - 1f;
            float cut = Mathf.Clamp(Mathf.Lerp(cut0, cut1, u), 0.005f, 1f);
            low += (white - low) * cut;
            float v = high ? white - low : low / MathF.Sqrt(cut / (2f - cut));
            float env = MathF.Min(1f, t / MathF.Max(attack, 1e-4f)) * MathF.Exp(-decay * t) * MathF.Min(1f, (n - i) / release);
            if (amHz > 0f) env *= 0.5f + 0.5f * MathF.Sin(Tau * amHz * t);
            b[s0 + i] += v * env * amp * 0.6f;
        }
    }

    /// <summary>Struck metal: inharmonic partials, the higher ones dying first.</summary>
    private static void Partials(float[] b, float start, float dur, float baseHz, float[] ratios, float amp, float decay)
    {
        int s0 = (int)(start * Rate), n = Mathf.Max(1, (int)(dur * Rate));
        float release = Rate * 0.004f;
        for (int i = 0; i < n && s0 + i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float sum = 0f;
            for (int k = 0; k < ratios.Length; k++)
                sum += MathF.Sin(Tau * baseHz * ratios[k] * t) * MathF.Exp(-decay * (1f + k * 0.5f) * t) / (1f + k * 0.6f);
            float env = MathF.Min(1f, t / 0.001f) * MathF.Min(1f, (n - i) / release);
            b[s0 + i] += sum * env * amp;
        }
    }

    /// <summary>Scattered pops: grit, debris, fire.</summary>
    private static void Crackle(float[] b, float start, float dur, float perSecond, float amp, int seed)
    {
        var rng = new Random(seed);
        int count = Mathf.Max(1, (int)(perSecond * dur));
        for (int k = 0; k < count; k++)
        {
            int at = (int)((start + (float)rng.NextDouble() * dur) * Rate);
            int len = (int)((0.002f + (float)rng.NextDouble() * 0.004f) * Rate);
            float a = amp * (0.3f + 0.7f * (float)rng.NextDouble());
            for (int i = 0; i < len && at + i < b.Length; i++)
                b[at + i] += ((float)rng.NextDouble() * 2f - 1f) * a * (1f - i / (float)len);
        }
    }

    private static void Arpeggio(float[] b, float[] hz, float step, float len, Wave wave, float amp, float decay, float duty = 0.5f)
    {
        for (int k = 0; k < hz.Length; k++)
            Tone(b, k * step, len, Flat(hz[k]), wave, amp, decay: decay, duty: duty);
    }

    private static void Lowpass(float[] b, float cut)
    {
        float low = 0f;
        for (int i = 0; i < b.Length; i++)
        {
            low += (b[i] - low) * cut;
            b[i] = low;
        }
    }

    private static void Saturate(float[] b, float drive)
    {
        float norm = MathF.Tanh(drive);
        for (int i = 0; i < b.Length; i++) b[i] = MathF.Tanh(b[i] * drive) / norm;
    }

    private static void Echo(float[] b, float delay, float gain)
    {
        int d = (int)(delay * Rate);
        for (int i = d; i < b.Length; i++) b[i] += b[i - d] * gain;
    }

    /// <summary>De-click the ends and scale to <paramref name="peak"/>.</summary>
    private static float[] Finish(float[] b, float peak)
    {
        int fadeIn = Mathf.Min(b.Length / 2, (int)(0.0015f * Rate));
        int fadeOut = Mathf.Min(b.Length / 2, (int)(0.006f * Rate));
        for (int i = 0; i < fadeIn; i++) b[i] *= i / (float)fadeIn;
        for (int i = 0; i < fadeOut; i++) b[b.Length - 1 - i] *= i / (float)fadeOut;

        float max = 0f;
        for (int i = 0; i < b.Length; i++)
        {
            if (float.IsNaN(b[i]) || float.IsInfinity(b[i])) b[i] = 0f;
            max = MathF.Max(max, MathF.Abs(b[i]));
        }
        if (max > 1e-6f)
        {
            float scale = peak / max;
            for (int i = 0; i < b.Length; i++) b[i] *= scale;
        }
        return b;
    }
}
