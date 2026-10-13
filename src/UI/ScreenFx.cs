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

    /// <summary>
    /// Slowed time, 0..1 (Overclock): call it every frame it applies; it eases in, and eases out once the calls stop.
    /// Shown whatever the grime setting, since it says something about the game, not the monitor.
    /// </summary>
    public void Slow(float amount) { _slowWant = MathF.Max(_slowWant, M.Clamp(amount, 0, 1)); _slowAt = Time.GetTicksMsec(); }
    private float _slowWant, _slow;
    private ulong _slowAt;

    /// <summary>Edge spent: fate bends -- a golden ripple runs out across the world and fades in under a second.</summary>
    public void Fate(float strength = 1) { _fate = MathF.Max(_fate, M.Clamp(strength, 0, 1)); _fateT = 0; }
    private float _fate, _fateT;
    /// <summary>Random flicker on or off (off in the walkthrough).</summary>
    public static bool Flicker = true;

    private ColorRect _rect;
    private ShaderMaterial _mat, _lightMat;
    /// <summary>(dev) Force a variant: -1 as normal, 0 off, 1 the light overlay, 2 the full shader.</summary>
    public int Force = -1;

    /// <summary>
    /// The same scanlines, vignette, warm cast and flicker multiplied over the
    /// screen without reading it back (no full-screen copy): no colour fringing,
    /// tearing or grain.
    /// </summary>
    private const string LightCode = @"
shader_type canvas_item;
render_mode blend_mul;
uniform float amount = 0.6;
uniform float flicker = 1.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
    vec2 c = SCREEN_UV - 0.5;
    float d = dot(c, c);
    float m = 1.0 - amount * 0.07 * (sin(FRAGCOORD.y * 3.14159) * 0.5 + 0.5);
    m *= 1.0 - smoothstep(0.1, 0.55, d * 1.4) * 0.5 * amount;
    m *= 1.0 - flicker * amount * 0.04 * step(0.985, hash(vec2(floor(TIME * 24.0), 3.0)));
    COLOR = vec4(mix(vec3(1.0), vec3(1.0, 0.96, 0.86), amount * 0.6) * m, 1.0);
}";
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


    /// <summary>
    /// Slowed time, drawn on its own layer between the world and the interface, so only the world slows and blurs
    /// and the HUD stays sharp: the edges drag inward in a zoom blur, the colour bleeds out to a cold cyan, the
    /// picture splits a little at the rim, and the corners close in with a slow heartbeat.
    /// </summary>
    private const string SlowCode = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear;
uniform float slow = 0.0;
uniform float fate = 0.0;
uniform float fate_t = 0.0;

void fragment() {
    vec2 uv = SCREEN_UV;
    vec2 c = uv - 0.5;
    float d = dot(c, c);
    float t = TIME;
    // Fate bending (Edge): a ring of ripple running outward from the middle, and gold where it passes.
    float dist = length(c * vec2(1.6, 1.0));
    float front = fate_t * 1.4;
    float wave = fate * exp(-pow((dist - front) * 9.0, 2.0));
    uv += normalize(c + 1e-5) * wave * 0.012 * sin(dist * 90.0 - fate_t * 30.0);
    vec3 col = texture(screen, uv).rgb;
    vec3 acc = col;
    for (int i = 1; i <= 6; i++) {
        float k = float(i) * 0.012 * slow * smoothstep(0.02, 0.3, d);
        acc += texture(screen, uv - c * k).rgb;
    }
    vec3 blurred = acc / 7.0;
    float lum = dot(blurred, vec3(0.3, 0.59, 0.11));
    vec3 cold = mix(blurred, vec3(lum) * vec3(0.7, 1.05, 1.25), 0.55);
    float split = 0.006 * slow * (0.3 + d * 3.0);
    cold.r = mix(cold.r, texture(screen, uv + c * split).r * 0.8, 0.5 * slow);
    cold.b = mix(cold.b, texture(screen, uv - c * split).b * 1.1, 0.5 * slow);
    col = mix(col, cold, slow);
    float beat = 0.5 + 0.5 * sin(t * 4.2);
    float rim = smoothstep(0.08, 0.5, d * 1.5);
    col = mix(col, col * vec3(0.35, 0.6, 0.75), rim * slow * (0.55 + 0.15 * beat));
    col += vec3(0.0, 0.08, 0.12) * slow * rim * beat;
    // The gold: on the ripple's front, and a warm wash with a golden rim that fades with it.
    float glum = dot(col, vec3(0.3, 0.59, 0.11));
    col = mix(col, vec3(glum) * vec3(1.35, 1.05, 0.55), fate * 0.35);
    col += vec3(1.0, 0.75, 0.3) * wave * 0.35;
    col += vec3(0.5, 0.33, 0.08) * fate * smoothstep(0.1, 0.5, d * 1.5) * 0.5;
    COLOR = vec4(col, 1.0);
}";
    private CanvasLayer _slowLayer;
    private ColorRect _slowRect;
    private ShaderMaterial _slowMat;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _Ready()
    {
        Layer = 100;
        _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _lightMat = new ShaderMaterial { Shader = new Shader { Code = LightCode } };
        _rect = new ColorRect { Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore };
        UITheme.Fill(_rect);
        AddChild(_rect);
        // The world sits on layer 0 and the interface on 10 (Root): this goes between.
        _slowMat = new ShaderMaterial { Shader = new Shader { Code = SlowCode } };
        _slowRect = new ColorRect { Material = _slowMat, MouseFilter = Control.MouseFilterEnum.Ignore };
        UITheme.Fill(_slowRect);
        _slowLayer = new CanvasLayer { Layer = 5, Visible = false };
        _slowLayer.AddChild(_slowRect);
        AddChild(_slowLayer);
        Apply();
    }

    /// <summary>Read the setting.</summary>
    private static readonly StringName PAmount = "amount", PJolt = "jolt", PInterference = "interference", PFlicker = "flicker", PSlow = "slow", PFate = "fate", PFateT = "fate_t";

    public void Apply()
    {
        if (_mat == null) return;
        float a = Profile.Current.Grime;
        _rect.Visible = a > 0.01f && Force != 0;
        _mat.SetShaderParameter(PAmount, a);
        _lightMat.SetShaderParameter(PAmount, a);
    }

    /// <summary>A hit: a red, smeared jolt that fades in a fraction of a second.</summary>
    public void Jolt(float strength) => _jolt = MathF.Max(_jolt, M.Clamp(strength, 0, 1));

    public override void _Process(double delta)
    {
        if (_mat == null) return;
        float dt = (float)delta;
        if (Time.GetTicksMsec() - _slowAt > 100) _slowWant = 0;
        // In quickly, out a little slower: time snapping back.
        _slow += (_slowWant - _slow) * MathF.Min(1, dt * (_slowWant > _slow ? 7 : 4));
        if (_slow < 0.002f) _slow = 0;
        _fateT += dt;
        _fate = MathF.Max(0, _fate - dt * 1.3f);
        _slowLayer.Visible = _slow > 0 || _fate > 0;
        if (_slowLayer.Visible)
        {
            _slowMat.SetShaderParameter(PSlow, _slow);
            _slowMat.SetShaderParameter(PFate, _fate);
            _slowMat.SetShaderParameter(PFateT, _fateT);
        }
        _rect.Visible = Profile.Current.Grime > 0.01f && Force != 0;
        if (!_rect.Visible) return;
        _jolt = MathF.Max(0, _jolt - dt * 3.5f);
        if (Time.GetTicksMsec() - _heldAt > 100) _held = 0;
        _interference += (_held - _interference) * MathF.Min(1, dt * 6);
        float scale = M.Clamp(Profile.Current.Grime / 0.6f, 0, 1.5f);
        // The light overlay when asked for, but a hit or a glitch still smears the picture.
        bool full = Force == 2 || Force == -1 && (!Profile.Current.LightScreenFx || _jolt > 0.01f || _interference > 0.01f);
        _rect.Material = full ? _mat : _lightMat;
        ShaderMaterial m = full ? _mat : _lightMat;
        if (full)
        {
            m.SetShaderParameter(PJolt, _jolt * scale);
            m.SetShaderParameter(PInterference, _interference * scale);
        }
        m.SetShaderParameter(PFlicker, Flicker ? 1f : 0f);
    }
}
