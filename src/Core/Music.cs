using System;
using Godot;

namespace Sprawl;

public enum MusicTrack { Title, City, Run, Debrief }

/// <summary>
/// The soundtrack: a small darksynth sequencer writing into an
/// <see cref="AudioStreamGenerator"/>. No audio files. Each track is a
/// four-chord loop, a tempo, and which layers play: detuned-saw pad, a
/// filtered saw bass, a pulse arpeggio with echo, and synthesised drums.
/// <see cref="Intensity"/> (eased) brings the drums and arpeggio in on a run
/// when the shooting starts.
/// </summary>
public partial class Music : Node
{
    public const int Rate = 22050;
    public static Music Instance { get; private set; }
    public float Intensity { get; set; }

    private AudioStreamPlayer _player;
    private AudioStreamGeneratorPlayback _playback;
    private Sequencer _current, _previous;
    private float _fade = 1f;
    private MusicTrack? _track;
    private Vector2[] _buffer = Array.Empty<Vector2>();

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        _player?.Stop();
        _playback?.Dispose();
        _playback = null;
    }

    public override void _Ready()
    {
        AudioBuses.Ensure();
        _player = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 0.3f },
            Bus = AudioBuses.Music,
        };
        AddChild(_player);
        _player.Play();
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public void Play(MusicTrack track)
    {
        if (_track == track && _current != null) return;
        _previous = _current;
        _current = new Sequencer(Spec(track));
        _fade = 0f;
        _track = track;
    }

    public override void _Process(double delta)
    {
        if (_playback == null) return;
        float target = Mathf.Clamp(Intensity, 0f, 1f);
        if (_current != null) _current.Intensity = Mathf.MoveToward(_current.Intensity, target, (float)delta * 0.5f);
        int n = _playback.GetFramesAvailable();
        if (n <= 0) return;
        if (_buffer.Length != n) _buffer = new Vector2[n];
        float step = 1f / (Rate * 1.2f);
        for (int i = 0; i < n; i++)
        {
            _fade = MathF.Min(1f, _fade + step);
            float s = 0f;
            if (_current != null) s += _current.Next() * _fade;
            if (_previous != null) s += _previous.Next() * (1f - _fade);
            _buffer[i] = new Vector2(s, s);
        }
        if (_fade >= 1f) _previous = null;
        _playback.PushBuffer(_buffer);
    }

    internal static float[] RenderOffline(MusicTrack track, float seconds, float intensity)
    {
        var seq = new Sequencer(Spec(track)) { Intensity = intensity };
        var b = new float[(int)(seconds * Rate)];
        for (int i = 0; i < b.Length; i++) b[i] = seq.Next();
        return b;
    }

    internal sealed class TrackSpec
    {
        public float Bpm;
        public int[][] Chords;
        public bool FourFloor, Pad = true, AlwaysDrums, AlwaysArp, Lead;
        public float BassCut = 0.08f;
        public int Seed;
    }

    private static TrackSpec Spec(MusicTrack t) => t switch
    {
        // Notes as MIDI; chords are root + triad.
        MusicTrack.Title => new TrackSpec { Bpm = 84, Chords = new[] { new[] { 40, 52, 55, 59 }, new[] { 36, 52, 55, 60 }, new[] { 43, 50, 55, 59 }, new[] { 38, 50, 54, 57 } }, Lead = true, Seed = 3 },
        MusicTrack.City => new TrackSpec { Bpm = 104, Chords = new[] { new[] { 45, 57, 60, 64 }, new[] { 41, 57, 60, 65 }, new[] { 36, 55, 60, 64 }, new[] { 43, 55, 59, 62 } }, FourFloor = true, AlwaysDrums = true, AlwaysArp = true, BassCut = 0.12f, Seed = 7 },
        MusicTrack.Run => new TrackSpec { Bpm = 118, Chords = new[] { new[] { 38, 50, 53, 57 }, new[] { 34, 50, 53, 58 }, new[] { 43, 50, 55, 58 }, new[] { 45, 49, 52, 57 } }, BassCut = 0.1f, Seed = 11 },
        _ => new TrackSpec { Bpm = 76, Chords = new[] { new[] { 45, 57, 60, 64 }, new[] { 41, 57, 60, 65 }, new[] { 38, 57, 62, 65 }, new[] { 40, 56, 59, 64 } }, Seed = 5 },
    };

    private static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

    private sealed class Sequencer
    {
        private readonly TrackSpec _s;
        private readonly int _stepLen;
        private int _sample, _step;
        public float Intensity;
        private readonly Random _rng;
        private uint _noise = 22222;

        private double _bassPh, _arpPh, _leadPh;
        private readonly double[] _padPh = new double[6];
        private float _bassHz, _bassT = 9, _bassLow;
        private float _arpHz, _arpT = 9;
        private float _leadHz, _leadT = 9;
        private float _kickT = 9, _snareT = 9, _hatT = 9, _hatLow, _padLow;
        private readonly float[] _echo = new float[(int)(Rate * 0.75f)];
        private int _echoAt;
        private readonly int[] _motif;

        public Sequencer(TrackSpec s)
        {
            _s = s;
            _stepLen = (int)(Rate * 60f / s.Bpm / 4f);
            _rng = new Random(s.Seed);
            _motif = new int[16];
            for (int i = 0; i < 16; i++) _motif[i] = _rng.Next(6) < 2 ? -1 : _rng.Next(4);
        }

        private float Noise()
        {
            _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5;
            return (_noise / (float)uint.MaxValue) * 2f - 1f;
        }

        private void Trigger()
        {
            int bar = _step / 16 % _s.Chords.Length;
            int st = _step % 16;
            int[] chord = _s.Chords[bar];
            bool drums = _s.AlwaysDrums || Intensity > 0.15f;
            bool arp = _s.AlwaysArp || Intensity > 0.35f;
            // Bass: driving eighths, or a pulse on the beat when calm.
            if (st % 2 == 0 && (drums || st % 8 == 0))
            {
                _bassHz = Hz(chord[0] + (st % 8 == 6 && drums ? 12 : 0));
                _bassT = 0;
            }
            if (arp)
            {
                int note = chord[1 + st % 3] + 12 * (st / 8 % 2);
                _arpHz = Hz(note);
                _arpT = 0;
            }
            if (_s.Lead && st % 2 == 0 && _motif[(st + bar * 4) % 16] >= 0)
            {
                _leadHz = Hz(chord[1 + _motif[(st + bar * 4) % 16] % 3] + 12);
                _leadT = 0;
            }
            if (drums)
            {
                if (_s.FourFloor ? st % 4 == 0 : st is 0 or 6 or 8 or 10) _kickT = 0;
                if (st is 4 or 12) _snareT = 0;
                if (st % 2 == 1 || Intensity > 0.7f) _hatT = 0;
            }
        }

        public float Next()
        {
            if (_sample % _stepLen == 0) { Trigger(); _step++; }
            _sample++;
            float dt = 1f / Rate;
            int bar = (_step - 1) / 16 % _s.Chords.Length;
            int[] chord = _s.Chords[Math.Max(0, bar)];
            float outv = 0;

            if (_s.Pad)
            {
                float pad = 0;
                for (int k = 0; k < 3; k++)
                for (int d = 0; d < 2; d++)
                {
                    int i = k * 2 + d;
                    _padPh[i] += Hz(chord[1 + k]) * (d == 0 ? 0.997 : 1.004) / Rate;
                    pad += (float)(2 * (_padPh[i] - Math.Floor(_padPh[i])) - 1);
                }
                _padLow += (pad - _padLow) * 0.04f;
                outv += _padLow * 0.07f;
            }

            _bassT += dt;
            _bassPh += _bassHz / Rate;
            float saw = (float)(2 * (_bassPh - Math.Floor(_bassPh)) - 1);
            float cut = _s.BassCut + 0.25f * MathF.Exp(-_bassT * 14);
            _bassLow += (saw - _bassLow) * cut;
            outv += _bassLow * MathF.Exp(-_bassT * 5) * 0.32f;

            _arpT += dt;
            _arpPh += _arpHz / Rate;
            float pulse = (_arpPh - Math.Floor(_arpPh)) < 0.3 ? 1f : -1f;
            float arp = pulse * MathF.Exp(-_arpT * 14) * 0.06f * (_s.AlwaysArp ? 0.8f : Intensity);
            float echoed = _echo[_echoAt];
            _echo[_echoAt] = arp + echoed * 0.42f;
            _echoAt = (_echoAt + 1) % _echo.Length;
            outv += arp + echoed * 0.5f;

            if (_s.Lead)
            {
                _leadT += dt;
                _leadPh += _leadHz * (1 + 0.004f * MathF.Sin(_sample * 0.0012f)) / Rate;
                float tri = (float)(4 * Math.Abs(_leadPh - Math.Floor(_leadPh) - 0.5) - 1);
                outv += tri * MathF.Min(1, _leadT * 30) * MathF.Exp(-_leadT * 2.5f) * 0.1f;
            }

            float drumGain = _s.AlwaysDrums ? 0.85f : MathF.Min(1, Intensity * 1.3f);
            _kickT += dt;
            if (_kickT < 0.4f) outv += MathF.Sin(MathF.Tau * (45 + 90 * MathF.Exp(-_kickT * 30)) * _kickT) * MathF.Exp(-_kickT * 9) * 0.5f * drumGain;
            _snareT += dt;
            if (_snareT < 0.3f) outv += (Noise() * 0.7f + MathF.Sin(MathF.Tau * 190 * _snareT) * 0.3f) * MathF.Exp(-_snareT * 16) * 0.22f * drumGain;
            _hatT += dt;
            if (_hatT < 0.08f)
            {
                float n = Noise();
                _hatLow += (n - _hatLow) * 0.5f;
                outv += (n - _hatLow) * MathF.Exp(-_hatT * 70) * 0.09f * drumGain;
            }
            return MathF.Tanh(outv * 1.3f) * 0.8f;
        }
    }
}
