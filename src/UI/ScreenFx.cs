using System;
using Godot;

namespace Sprawl;

/// <summary>
/// The dirty monitor everything is seen through: film grain, faint
/// scanlines, a vignette, a little colour fringing towards the edges, a warm
/// cast, and now and then a flicker. On a run it answers what happens:
/// <see cref="Jolt"/> when you are hit, and <see cref="Interfere"/> while
/// the alarm is up or you are jacked into a drone.
/// </summary>
///
/// <remarks>
/// One full-screen shader on the topmost canvas layer, reading the screen
/// behind it, so it covers the world and every screen alike. The "Screen
/// grime" setting scales all of it, and 0 hides the layer outright, which
/// also costs nothing. The walkthrough keeps it on, minus the random flicker,
/// so screenshots show the real look and stay comparable.
/// </remarks>
public partial class ScreenFx : CanvasLayer
{
    public static ScreenFx Instance { get; private set; }

    /// <summary>
    /// Tearing and noise, 0..1: whoever owns the moment calls this every frame
    /// it applies; it lapses a tenth of a second after the last call.
    /// </summary>
    public void Interfere(float amount) { _held = MathF.Max(_held, amount); _heldAt = Time.GetTicksMsec(); }
    private float _held;
    private ulong _heldAt;
    /// <summary>Random flicker on or off (off in the walkthrough).</summary>
    public static bool Flicker = true;

    private ColorRect _rect;
    private ShaderMaterial _mat;
    private float _jolt, _interference;

    private const string Code = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear;
uniform float amount = 0.6;
uniform float flicker = 1.0;
uniform float jolt = 0.0;
uniform float interference = 0.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
    vec2 uv = SCREEN_UV;
    vec2 c = uv - 0.5;
    float d = dot(c, c);
    float t = TIME;
    // Tearing: a few horizontal bands slip sideways.
    float rows = floor(uv.y * 70.0);
    float tear = step(0.992 - interference * 0.09, hash(vec2(rows, floor(t * 14.0))));
    uv.x += tear * (hash(vec2(t, rows)) - 0.5) * 0.03 * (interference + jolt);
    uv += c * jolt * 0.012;
    // Colour fringing, stronger towards the edges and on a jolt.
    float ab = (0.0008 + d * 0.006) * amount + jolt * 0.006 + interference * 0.002;
    vec3 col;
    col.r = texture(screen, uv + c * ab).r;
    col.g = texture(screen, uv).g;
    col.b = texture(screen, uv - c * ab).b;
    // Scanlines, grain, a warm cast, a vignette.
    col *= 1.0 - amount * 0.07 * (sin(FRAGCOORD.y * 3.14159) * 0.5 + 0.5);
    col += (hash(FRAGCOORD.xy + fract(t * 7.13) * vec2(97.0, 57.0)) - 0.5) * (0.05 * amount + 0.08 * interference);
    col = mix(col, col * vec3(1.04, 1.0, 0.9), amount * 0.6);
    col *= 1.0 - smoothstep(0.1, 0.55, d * 1.4) * 0.5 * amount;
    // A flicker now and then, like a tired backlight.
    col *= 1.0 - flicker * amount * 0.04 * step(0.985, hash(vec2(floor(t * 24.0), 3.0)));
    col = mix(col, col * vec3(1.25, 0.75, 0.75), jolt * 0.35);
    COLOR = vec4(col, 1.0);
}";

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _Ready()
    {
        Layer = 100;
        _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _rect = new ColorRect { Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore };
        UITheme.Fill(_rect);
        AddChild(_rect);
        Apply();
    }

    /// <summary>Read the setting.</summary>
    public void Apply()
    {
        if (_mat == null) return;
        float a = Profile.Current.Grime;
        _rect.Visible = a > 0.01f;
        _mat.SetShaderParameter("amount", a);
    }

    /// <summary>A hit: a red, smeared jolt that fades in a fraction of a second.</summary>
    public void Jolt(float strength) => _jolt = MathF.Max(_jolt, M.Clamp(strength, 0, 1));

    public override void _Process(double delta)
    {
        if (_mat == null || !_rect.Visible) return;
        float dt = (float)delta;
        _jolt = MathF.Max(0, _jolt - dt * 3.5f);
        if (Time.GetTicksMsec() - _heldAt > 100) _held = 0;
        _interference += (_held - _interference) * MathF.Min(1, dt * 6);
        float scale = M.Clamp(Profile.Current.Grime / 0.6f, 0, 1.5f);
        _mat.SetShaderParameter("jolt", _jolt * scale);
        _mat.SetShaderParameter("interference", _interference * scale);
        _mat.SetShaderParameter("flicker", Flicker ? 1f : 0f);
    }
}
