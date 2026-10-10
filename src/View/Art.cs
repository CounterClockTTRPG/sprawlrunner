using System;
using Godot;

namespace Sprawl;

/// <summary>
/// Everything drawn from primitives: people seen from above, drones,
/// turrets, cars, and the soft glow texture the lights are made of.
/// </summary>
public static class Art
{
    public static Vector2 G(V2 v) => new(v.X, v.Y);
    public static Vector2 G(V2 v, float scale) => new(v.X * scale, v.Y * scale);
    public static V2 S(Vector2 v) => new(v.X, v.Y);
    public static Color C(uint rgba) => UITheme.Rgba(rgba);

    private static Texture2D _glow, _soft;

    /// <summary>A radial falloff, white in the middle, for additive lights.</summary>
    public static Texture2D Glow => _glow ??= MakeGlow(128, 2.2f);
    /// <summary>A flatter disc, for smoke and shadows.</summary>
    public static Texture2D Soft => _soft ??= MakeGlow(64, 1.2f);

    private static Texture2D MakeGlow(int size, float power)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = MathF.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = MathF.Pow(MathF.Max(0, 1 - d), power);
            img.SetPixel(x, y, new Color(1, 1, 1, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static CanvasItemMaterial _add;
    public static CanvasItemMaterial Additive => _add ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

    public static void DrawGlow(CanvasItem ci, Vector2 at, float radius, Color c)
    {
        ci.DrawTextureRect(Glow, new Rect2(at - new Vector2(radius, radius), new Vector2(radius * 2, radius * 2)), false, c);
    }

    /// <summary>Cheap stable hash noise in 0..1 for tile decoration.</summary>
    public static float Hash(int x, int y, int salt = 0)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xffffff) / (float)0xffffff;
    }

    // ---- people -------------------------------------------------------------------

    /// <summary>The one thing that marks a runner's trade at a glance.</summary>
    public enum Mark { None, Chrome, Hood, Suit, Pack, Aura }
    /// <summary>What a faction's people wear on their heads and shoulders: Viper bandanas, Lotus sashes, corporate helmets.</summary>
    public enum Gear { None, Bandana, Sash, Helmet }

    public struct Look
    {
        public Color Body, Trim, Skin, Hair;
        public bool Heavy, Coat;
        public Mark Mark;
        /// <summary>Cybereyes: a glint where the eyes are.</summary>
        public bool Eyes;
        public Gear Gear;
    }

    /// <summary>
    /// The player, by archetype: a street samurai in dark armour with a chrome
    /// arm and a red visor, a decker hooded with a deck on the wrist, a face in
    /// a sharp pale suit, an engineer in hi-vis with a hard hat and a pack, a
    /// mage in a violet coat with the gift glowing round them.
    /// </summary>
    public static Look PlayerLook(string archetype, bool eyes = false)
    {
        Color skin = new(0.86f, 0.7f, 0.58f);
        Look l = archetype switch
        {
            "decker" => new Look { Body = new Color(0.1f, 0.14f, 0.16f), Trim = UITheme.Cyan, Skin = skin, Hair = new Color(0.12f, 0.2f, 0.22f), Mark = Mark.Hood },
            "face" => new Look { Body = new Color(0.78f, 0.76f, 0.72f), Trim = new Color(1f, 0.8f, 0.3f), Skin = skin, Hair = new Color(0.2f, 0.12f, 0.08f), Coat = true, Mark = Mark.Suit },
            "engineer" => new Look { Body = new Color(0.32f, 0.28f, 0.2f), Trim = new Color(1f, 0.55f, 0.1f), Skin = skin, Hair = new Color(1f, 0.7f, 0.1f), Heavy = true, Mark = Mark.Pack },
            "mage" => new Look { Body = new Color(0.22f, 0.1f, 0.32f), Trim = UITheme.Violet, Skin = skin, Hair = new Color(0.85f, 0.85f, 0.9f), Coat = true, Mark = Mark.Aura },
            _ => new Look { Body = new Color(0.12f, 0.13f, 0.17f), Trim = new Color(1f, 0.25f, 0.3f), Skin = skin, Hair = new Color(0.9f, 0.2f, 0.5f), Coat = true, Mark = Mark.Chrome },
        };
        l.Eyes = eyes;
        return l;
    }

    public static Look LookFor(Actor a, Faction f)
    {
        if (a.IsPlayer) return PlayerLook("samurai");
        // A ghoul: grey-skinned, hairless, in what's left of someone else's clothes; no faction's colours.
        if (a.Wild && a.Kind == EnemyKind.Bruiser)
            return new Look { Body = new Color(0.24f, 0.22f, 0.2f), Trim = new Color(0.42f, 0.36f, 0.3f), Skin = new Color(0.62f, 0.66f, 0.6f), Hair = new Color(0.55f, 0.58f, 0.54f), Heavy = true };
        if (a.IsHostage) return new Look { Body = new Color(0.85f, 0.85f, 0.8f), Trim = new Color(0.6f, 0.6f, 0.65f), Skin = new Color(0.75f, 0.58f, 0.45f), Hair = new Color(0.25f, 0.2f, 0.15f) };
        Color skin = new(0.8f, 0.62f, 0.5f);
        var l = f switch
        {
            Faction.Vipers => new Look { Body = new Color(0.25f, 0.32f, 0.18f), Trim = new Color(0.55f, 1f, 0.2f), Skin = skin, Hair = new Color(0.1f, 0.9f, 0.4f) },
            Faction.Lotus => new Look { Body = new Color(0.18f, 0.08f, 0.1f), Trim = new Color(1f, 0.2f, 0.25f), Skin = skin, Hair = new Color(0.08f, 0.06f, 0.06f) },
            _ => new Look { Body = new Color(0.22f, 0.27f, 0.36f), Trim = new Color(1f, 0.82f, 0.25f), Skin = skin, Hair = new Color(0.12f, 0.12f, 0.14f) },
        };
        // The faction's badge on its rank and file.
        l.Gear = f switch
        {
            Faction.Vipers when a.Kind is EnemyKind.Ganger or EnemyKind.Bruiser or EnemyKind.Firebug => Gear.Bandana,
            Faction.Lotus when a.Kind is EnemyKind.Guard or EnemyKind.Blade or EnemyKind.Bruiser => Gear.Sash,
            Faction.Halcyon when a.Kind is EnemyKind.Guard or EnemyKind.Sniper or EnemyKind.Rigger => Gear.Helmet,
            _ => Gear.None,
        };
        if (a.Kind is EnemyKind.Heavy or EnemyKind.Shield or EnemyKind.Bulwark) l.Gear = Gear.Helmet;
        switch (a.Kind)
        {
            case EnemyKind.Heavy: l.Heavy = true; l.Body = l.Body.Darkened(0.2f); break;
            case EnemyKind.Mage: l.Body = new Color(0.3f, 0.12f, 0.45f); l.Trim = new Color(0.9f, 0.5f, 1f); l.Coat = true; break;
            case EnemyKind.Sniper: l.Body = new Color(0.2f, 0.22f, 0.2f); break;
            case EnemyKind.Vip: l.Body = new Color(0.9f, 0.9f, 0.92f); l.Trim = new Color(0.95f, 0.75f, 0.2f); l.Coat = true; break;
            case EnemyKind.Blade: l.Coat = true; break;
            case EnemyKind.Shield: l.Heavy = true; break;
            case EnemyKind.Bruiser: l.Heavy = true; l.Body = l.Body.Lerp(new Color(0.3f, 0.2f, 0.15f), 0.5f); break;
            case EnemyKind.Firebug: l.Trim = new Color(1f, 0.55f, 0.1f); l.Hair = new Color(1f, 0.4f, 0.05f); break;
            case EnemyKind.Rigger: l.Body = new Color(0.25f, 0.25f, 0.3f); l.Trim = new Color(0.4f, 0.9f, 1f); break;
            case EnemyKind.Boss: l.Heavy = true; l.Body = l.Body.Darkened(0.3f); l.Trim = new Color(1f, 0.8f, 0.2f); l.Coat = true; break;
            case EnemyKind.Ally: l.Body = new Color(0.15f, 0.2f, 0.3f); l.Trim = UITheme.Good; break;
        }
        return l;
    }

    /// <summary>A person from above, facing <paramref name="facing"/>; radius in pixels.</summary>
    /// <param name="breathe">A clock for the idle breath (0: still, as the dead are).</param>
    /// <param name="recoil">1 the instant a shot leaves the gun, falling to 0: the weapon kicks back and flashes.</param>
    public static void Person(CanvasItem ci, Vector2 p, float facing, float r, Look l, WeaponClass? weapon, float flash = 0, float walk = 0, float alpha = 1, float breathe = 0, float recoil = 0)
    {
        Vector2 f = new(MathF.Cos(facing), MathF.Sin(facing));
        Vector2 side = new(-f.Y, f.X);
        Color body = l.Body.Lerp(Colors.White, flash) with { A = alpha };
        Color trim = l.Trim with { A = alpha };
        // A soft shadow, offset from the light.
        ci.DrawTextureRect(Glow, new Rect2(p + new Vector2(2, 3) - Vector2.One * r * 1.7f, Vector2.One * r * 3.4f), false, new Color(0, 0, 0, 0.6f * alpha));
        // Feet, swinging.
        float sw = MathF.Sin(walk) * r * 0.35f;
        ci.DrawCircle(p + side * r * 0.4f + f * sw, r * 0.22f, new Color(0.05f, 0.05f, 0.06f, alpha));
        ci.DrawCircle(p - side * r * 0.4f - f * sw, r * 0.22f, new Color(0.05f, 0.05f, 0.06f, alpha));
        // Weapon.
        if (weapon is WeaponClass w && w != WeaponClass.Melee)
        {
            float len = w switch { WeaponClass.Pistol => 0.75f, WeaponClass.Smg => 0.95f, WeaponClass.Sniper => 1.5f, WeaponClass.Lmg => 1.3f, WeaponClass.Launcher => 1.35f, _ => 1.15f } * r;
            float thick = w is WeaponClass.Lmg or WeaponClass.Launcher ? r * 0.32f : r * 0.2f;
            Vector2 gs = p + f * r * (0.35f - 0.22f * recoil) + side * r * 0.25f;
            ci.DrawLine(gs, gs + f * len * 1.3f, new Color(0.08f, 0.08f, 0.1f, alpha), thick);
            ci.DrawLine(gs + f * len * 0.6f, gs + f * len * 1.3f, new Color(0.3f, 0.32f, 0.36f, alpha), thick * 0.5f);
            if (recoil > 0.5f)
            {
                Vector2 mz = gs + f * len * 1.35f;
                ci.DrawColoredPolygon(new[] { mz + f * r * 0.7f, mz + side * r * 0.22f, mz - side * r * 0.22f }, new Color(1f, 0.85f, 0.4f, (recoil - 0.5f) * 2 * alpha));
            }
        }
        // Shoulders: an ellipse across the facing.
        var pts = new Vector2[12];
        float bw = r * (l.Heavy ? 1.15f : 0.95f), bd = r * (l.Heavy ? 0.75f : 0.6f);
        // Standing still, shoulders rise and fall.
        if (breathe != 0 && walk == 0) { float br = 1 + 0.035f * MathF.Sin(breathe * 2.4f); bw *= br; bd *= br; }
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathF.Tau;
            pts[i] = p + side * MathF.Cos(a) * bw + f * MathF.Sin(a) * bd;
        }
        ci.DrawColoredPolygon(pts, body);
        var loop = new Vector2[13];
        Array.Copy(pts, loop, 12);
        loop[12] = pts[0];
        ci.DrawPolyline(loop, trim with { A = 0.8f * alpha }, 1.3f);
        // A Lotus sash, shoulder to hip.
        if (l.Gear == Gear.Sash) ci.DrawLine(p + side * bw * 0.85f + f * bd * 0.2f, p - side * bw * 0.6f - f * bd * 0.7f, new Color(0.85f, 0.1f, 0.15f, alpha), r * 0.18f);
        if (l.Coat) ci.DrawColoredPolygon(new[] { p - f * bd * 0.2f + side * bw * 0.8f, p - f * bd * 1.5f + side * bw * 0.6f, p - f * bd * 1.5f - side * bw * 0.6f, p - f * bd * 0.2f - side * bw * 0.8f }, body.Darkened(0.25f));
        // Arms forward to the weapon.
        ci.DrawCircle(p + f * r * 0.55f + side * r * 0.5f, r * 0.2f, l.Skin with { A = alpha });
        ci.DrawCircle(p + f * r * 0.65f - side * r * 0.1f, r * 0.2f, l.Skin with { A = alpha });
        // What they carry on their back, under the head.
        if (l.Mark == Mark.Pack)
        {
            Vector2 bp = p - f * bd * 0.95f;
            ci.DrawRect(new Rect2(bp - new Vector2(r * 0.45f, r * 0.45f), new Vector2(r * 0.9f, r * 0.9f)), new Color(0.25f, 0.22f, 0.15f, alpha));
            ci.DrawLine(bp - side * r * 0.4f, bp + side * r * 0.4f, trim, 2);
        }
        if (l.Mark == Mark.Chrome)
        {
            // A chrome arm: the off-hand side, bright and segmented.
            Vector2 sh = p - side * bw * 0.85f;
            ci.DrawLine(sh, sh + f * r * 0.7f, new Color(0.75f, 0.8f, 0.85f, alpha), r * 0.28f);
            ci.DrawLine(sh + f * r * 0.3f - side * r * 0.12f, sh + f * r * 0.3f + side * r * 0.12f, new Color(0.3f, 0.32f, 0.36f, alpha), 1.5f);
        }
        if (l.Mark == Mark.Hood)
        {
            // A wrist deck, lit.
            Vector2 wr = p + f * r * 0.55f + side * r * 0.5f;
            ci.DrawRect(new Rect2(wr - new Vector2(r * 0.18f, r * 0.18f), new Vector2(r * 0.36f, r * 0.36f)), UITheme.Cyan with { A = 0.9f * alpha });
        }
        if (l.Mark == Mark.Suit)
        {
            // Shirt and tie down the front of a pale jacket.
            ci.DrawLine(p + f * bd * 0.1f, p + f * bd * 0.95f, new Color(0.95f, 0.95f, 0.95f, alpha), r * 0.22f);
            ci.DrawLine(p + f * bd * 0.2f, p + f * bd * 0.9f, trim, r * 0.08f);
        }
        // Head.
        ci.DrawCircle(p, r * 0.45f, l.Skin.Lerp(Colors.White, flash) with { A = alpha });
        if (l.Mark == Mark.Hood) ci.DrawCircle(p - f * r * 0.04f, r * 0.5f, l.Body.Lightened(0.08f) with { A = alpha });
        else if (l.Mark == Mark.Pack)
        {
            ci.DrawCircle(p - f * r * 0.06f, r * 0.46f, new Color(1f, 0.68f, 0.1f, alpha));
            ci.DrawLine(p - side * r * 0.46f, p + side * r * 0.46f, new Color(0.7f, 0.45f, 0.05f, alpha), 1.5f);
        }
        else if (l.Gear == Gear.Helmet)
        {
            // A helmet and its visor slit.
            ci.DrawCircle(p - f * r * 0.04f, r * 0.48f, new Color(0.18f, 0.2f, 0.24f, alpha));
            ci.DrawLine(p + f * r * 0.3f + side * r * 0.25f, p + f * r * 0.3f - side * r * 0.25f, trim, 2f);
        }
        else ci.DrawCircle(p - f * r * 0.1f, r * 0.4f, l.Hair with { A = alpha });
        // A Viper bandana: a band round the head, its tails behind.
        if (l.Gear == Gear.Bandana)
        {
            ci.DrawArc(p, r * 0.42f, MathF.Atan2(side.Y, side.X) - 0.3f, MathF.Atan2(side.Y, side.X) + MathF.PI + 0.3f, 10, new Color(0.35f, 0.9f, 0.25f, alpha), r * 0.12f);
            ci.DrawLine(p - f * r * 0.42f, p - f * r * 0.75f + side * r * 0.12f, new Color(0.35f, 0.9f, 0.25f, alpha), r * 0.1f);
        }
        if (l.Mark == Mark.Chrome) ci.DrawLine(p + f * r * 0.3f + side * r * 0.28f, p + f * r * 0.3f - side * r * 0.28f, new Color(1f, 0.2f, 0.25f, alpha), 2.5f);
        if (l.Eyes && l.Mark != Mark.Chrome)
        {
            ci.DrawCircle(p + f * r * 0.32f + side * r * 0.14f, r * 0.08f, new Color(0.4f, 1f, 1f, alpha));
            ci.DrawCircle(p + f * r * 0.32f - side * r * 0.14f, r * 0.08f, new Color(0.4f, 1f, 1f, alpha));
        }
        if (l.Mark == Mark.Aura)
            ci.DrawArc(p, r * 1.35f, 0, MathF.Tau, 20, UITheme.Violet with { A = 0.35f * alpha }, 2);
    }

    /// <summary>An office's night cleaner: teal coveralls with a hi-vis stripe, their own face and hair.</summary>
    public static Look CleanerLook(int seed)
    {
        float h = Hash(seed, 5);
        return new Look { Body = new Color(0.2f, 0.34f, 0.36f), Trim = new Color(0.38f, 0.56f, 0.56f), Skin = new Color(0.45f + h * 0.45f, 0.33f + h * 0.32f, 0.25f + h * 0.27f), Hair = Color.FromHsv(0.05f + h * 0.06f, 0.5f, 0.12f + Hash(seed, 9) * 0.35f) };
    }

    /// <summary>
    /// What the cleaner adds over <see cref="Person"/>: the hi-vis stripe across
    /// the back, a cap and its peak, and, calling, a lit phone at the ear.
    /// </summary>
    public static void CleanerKit(CanvasItem ci, Vector2 p, float facing, float r, bool calling, float t)
    {
        Vector2 f = new(MathF.Cos(facing), MathF.Sin(facing));
        Vector2 side = new(-f.Y, f.X);
        Color vis = new(0.85f, 0.95f, 0.3f);
        ci.DrawLine(p - f * r * 0.36f + side * r * 0.86f, p - f * r * 0.36f - side * r * 0.86f, vis, r * 0.24f);
        ci.DrawCircle(p - f * r * 0.06f, r * 0.41f, new Color(0.16f, 0.27f, 0.29f));
        ci.DrawColoredPolygon(new[] { p + f * r * 0.2f + side * r * 0.32f, p + f * r * 0.62f + side * r * 0.2f, p + f * r * 0.62f - side * r * 0.2f, p + f * r * 0.2f - side * r * 0.32f }, new Color(0.12f, 0.21f, 0.23f));
        if (!calling) return;
        Vector2 ear = p + side * r * 0.5f + f * r * 0.1f;
        ci.DrawRect(new Rect2(ear - new Vector2(r * 0.15f, r * 0.24f), new Vector2(r * 0.3f, r * 0.48f)), new Color(0.06f, 0.07f, 0.08f));
        ci.DrawRect(new Rect2(ear - new Vector2(r * 0.1f, r * 0.17f), new Vector2(r * 0.2f, r * 0.3f)), new Color(0.5f, 1f, 0.8f));
        DrawGlow(ci, ear, r * (1.4f + 0.3f * MathF.Sin(t * 10)), new Color(0.4f, 1f, 0.75f, 0.45f));
    }

    /// <summary>
    /// A janitor's cart seen from above, its handle toward <paramref name="facing"/>
    /// reversed (the pusher's hands): castors, a steel frame, a yellow mop
    /// bucket with grey water and the mop in it, a black bin bag, a spray
    /// bottle. <paramref name="r"/> is the pusher's figure radius.
    /// </summary>
    public static void CleanerCart(CanvasItem ci, Vector2 c, float facing, float r, float alpha = 1)
    {
        Vector2 f = new(MathF.Cos(facing), MathF.Sin(facing));
        Vector2 s = new(-f.Y, f.X);
        float hl = r * 0.62f, hw = r * 0.78f;
        Vector2 Q(float along, float across) => c + f * along + s * across;
        ci.DrawTextureRect(Glow, new Rect2(c + new Vector2(2, 3) - Vector2.One * r * 1.6f, Vector2.One * r * 3.2f), false, new Color(0, 0, 0, 0.55f * alpha));
        // Castors at the corners, just outside the frame.
        foreach (float a in new[] { -1f, 1f })
        foreach (float b in new[] { -1f, 1f })
            ci.DrawCircle(Q(a * hl * 0.8f, b * hw * 1.02f), r * 0.15f, new Color(0.04f, 0.04f, 0.05f, alpha));
        // The frame: dark steel, a lighter rim, a divider between bucket and bag.
        Vector2[] frame = { Q(hl, hw), Q(hl, -hw), Q(-hl, -hw), Q(-hl, hw) };
        ci.DrawColoredPolygon(frame, new Color(0.19f, 0.22f, 0.27f, alpha));
        ci.DrawPolyline(new[] { frame[0], frame[1], frame[2], frame[3], frame[0] }, new Color(0.5f, 0.55f, 0.62f, alpha), 1.3f);
        ci.DrawLine(Q(hl * 0.95f, 0), Q(-hl * 0.95f, 0), new Color(0.32f, 0.36f, 0.42f, alpha), 1f);
        // The handle the cleaner pushes, on struts off the back edge.
        ci.DrawLine(Q(-hl, hw * 0.7f), Q(-hl - r * 0.22f, hw * 0.7f), new Color(0.55f, 0.6f, 0.66f, alpha), 1.5f);
        ci.DrawLine(Q(-hl, -hw * 0.7f), Q(-hl - r * 0.22f, -hw * 0.7f), new Color(0.55f, 0.6f, 0.66f, alpha), 1.5f);
        ci.DrawLine(Q(-hl - r * 0.22f, hw * 0.82f), Q(-hl - r * 0.22f, -hw * 0.82f), new Color(0.78f, 0.82f, 0.88f, alpha), 2.2f);
        // The bin bag, tied off, catching the light on one side.
        Vector2 bag = Q(-hl * 0.05f, -hw * 0.48f);
        ci.DrawCircle(bag, r * 0.4f, new Color(0.06f, 0.06f, 0.08f, alpha));
        ci.DrawArc(bag, r * 0.3f, -2.4f, -0.9f, 8, new Color(0.38f, 0.42f, 0.5f, 0.8f * alpha), 1.3f);
        ci.DrawCircle(bag + f * r * 0.05f, r * 0.09f, new Color(0.2f, 0.21f, 0.24f, alpha));
        // The spray bottle in the corner.
        Vector2 spray = Q(hl * 0.62f, -hw * 0.7f);
        ci.DrawCircle(spray, r * 0.13f, new Color(0.25f, 0.6f, 0.95f, alpha));
        ci.DrawCircle(spray, r * 0.05f, new Color(0.95f, 0.95f, 0.95f, alpha));
        // The mop bucket: yellow, grey water, the mop head in it and the handle leaning back over the cart.
        Vector2 bucket = Q(hl * 0.18f, hw * 0.46f);
        ci.DrawCircle(bucket, r * 0.4f, new Color(0.95f, 0.75f, 0.12f, alpha));
        ci.DrawArc(bucket, r * 0.4f, 0, MathF.Tau, 16, new Color(0.55f, 0.4f, 0.05f, alpha), 1.2f);
        ci.DrawCircle(bucket, r * 0.28f, new Color(0.3f, 0.42f, 0.42f, 0.95f * alpha));
        for (int k = 0; k < 7; k++)
        {
            float a = k / 7f * MathF.Tau + 0.3f;
            Vector2 d = new(MathF.Cos(a), MathF.Sin(a));
            ci.DrawLine(bucket + d * r * 0.06f, bucket + d * r * 0.24f, new Color(0.82f, 0.8f, 0.72f, alpha), 1.2f);
        }
        ci.DrawLine(bucket, Q(hl + r * 0.2f, hw * 1.05f), new Color(0.7f, 0.56f, 0.34f, alpha), 1.8f);
        ci.DrawCircle(Q(hl + r * 0.2f, hw * 1.05f), r * 0.09f, new Color(0.2f, 0.45f, 0.75f, alpha));
    }

    /// <summary>
    /// Someone down: lying flat along <paramref name="dir"/>, limbs splayed,
    /// colour drained, in a pool of blood (none when only knocked out). Hounds
    /// fall as hounds. Nobody mistakes it for someone standing.
    /// </summary>
    public static void Corpse(CanvasItem ci, Vector2 p, float dir, float r, Look l, EnemyKind kind, bool ko, int seed)
    {
        Vector2 f = new(MathF.Cos(dir), MathF.Sin(dir)), s = new(-f.Y, f.X);
        Color Drain(Color c) => c.Lerp(new Color(0.25f, 0.25f, 0.27f), 0.45f).Darkened(0.25f);
        bool pale = kind == EnemyKind.Barghest;
        if (pale) kind = EnemyKind.Hound;
        if (kind is EnemyKind.HiveWorker or EnemyKind.HiveQueen or EnemyKind.EggSac)
        {
            // Ichor, not blood; a cracked shell and curled legs (or a burst, empty sac).
            ci.DrawCircle(p, r * 1.5f, new Color(0.45f, 0.55f, 0.1f, 0.6f));
            if (kind == EnemyKind.EggSac) { ci.DrawArc(p, r * 0.9f, 0.4f, 5.4f, 12, new Color(0.4f, 0.45f, 0.25f), 2); return; }
            Color shell = new(0.2f, 0.2f, 0.1f);
            for (int k = 0; k < 6; k++) ci.DrawArc(p + s * (k % 2 == 0 ? 1 : -1) * r * 0.4f + f * (k / 2 - 1) * r * 0.4f, r * 0.4f, 0, 2.5f, 6, shell, 1.5f);
            ci.DrawCircle(p, r * 0.6f, shell);
            ci.DrawCircle(p - f * r * 0.7f, r * 0.55f, shell.Darkened(0.2f));
            return;
        }
        if (!ko)
        {
            float pool = r * (1.4f + Hash(seed, 1) * 0.5f);
            var blob = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * MathF.Tau;
                float rr = pool * (0.75f + 0.35f * Hash(seed, i + 2));
                blob[i] = p + f * MathF.Cos(a) * rr * 1.3f + s * MathF.Sin(a) * rr;
            }
            ci.DrawColoredPolygon(blob, new Color(0.33f, 0.02f, 0.04f, 0.85f));
        }
        if (kind == EnemyKind.Hound)
        {
            // On its side: body, four stiff legs out one way, head flopped.
            Color fur = pale ? new Color(0.36f, 0.37f, 0.4f) : new Color(0.12f, 0.07f, 0.05f);
            for (int k = 0; k < 4; k++)
            {
                Vector2 hip = p + f * ((k < 2 ? 0.55f : -0.55f) * r) + s * r * 0.3f;
                ci.DrawLine(hip, hip + s * r * 0.9f + f * (k % 2 == 0 ? 0.15f : -0.15f) * r, fur, r * 0.22f);
            }
            var body = new Vector2[12];
            for (int i = 0; i < 12; i++) { float a = i / 12f * MathF.Tau; body[i] = p + f * MathF.Cos(a) * r * 1.25f + s * MathF.Sin(a) * r * 0.55f; }
            ci.DrawColoredPolygon(body, fur);
            ci.DrawCircle(p + f * r * 1.45f + s * r * 0.25f, r * 0.42f, fur);
            ci.DrawLine(p - f * r * 1.2f, p - f * r * 1.9f + s * r * 0.4f, fur, r * 0.15f);
            return;
        }
        float big = kind is EnemyKind.Heavy or EnemyKind.Boss or EnemyKind.Shield ? 1.25f : 1f;
        Color body2 = Drain(l.Body), skin = Drain(l.Skin), hair = Drain(l.Hair);
        // Legs and arms, splayed.
        ci.DrawLine(p - f * r * 0.5f + s * r * 0.25f, p - f * r * 1.55f * big + s * r * 0.55f, body2.Darkened(0.3f), r * 0.32f);
        ci.DrawLine(p - f * r * 0.5f - s * r * 0.25f, p - f * r * 1.5f * big - s * r * 0.2f, body2.Darkened(0.3f), r * 0.32f);
        ci.DrawLine(p + f * r * 0.5f + s * r * 0.55f * big, p + f * r * 0.15f + s * r * 1.35f * big, skin, r * 0.22f);
        ci.DrawLine(p + f * r * 0.5f - s * r * 0.55f * big, p + f * r * 1.15f - s * r * 1.0f * big, skin, r * 0.22f);
        // Torso, flat on the ground.
        var torso = new Vector2[12];
        for (int i = 0; i < 12; i++) { float a = i / 12f * MathF.Tau; torso[i] = p + f * MathF.Cos(a) * r * 0.85f * big + s * MathF.Sin(a) * r * 0.6f * big; }
        ci.DrawColoredPolygon(torso, body2);
        if (l.Coat) ci.DrawColoredPolygon(new[] { p - f * r * 0.4f + s * r * 0.6f, p - f * r * 1.3f + s * r * 0.9f, p - f * r * 1.3f - s * r * 0.9f, p - f * r * 0.4f - s * r * 0.6f }, body2.Darkened(0.2f));
        // Head, turned aside.
        Vector2 head = p + f * r * 1.15f * big + s * r * 0.15f;
        ci.DrawCircle(head, r * 0.42f, skin);
        ci.DrawCircle(head + f * r * 0.12f, r * 0.36f, hair);
    }

    public static void Drone(CanvasItem ci, Vector2 p, float facing, float r, Color trim, float t, float flash = 0)
    {
        Vector2 f = new(MathF.Cos(facing), MathF.Sin(facing));
        Vector2 side = new(-f.Y, f.X);
        ci.DrawCircle(p + new Vector2(6, 10), r * 0.9f, new Color(0, 0, 0, 0.25f));
        for (int k = 0; k < 4; k++)
        {
            Vector2 arm = (k % 2 == 0 ? f : -f) * r * 0.7f + (k < 2 ? side : -side) * r * 0.7f;
            ci.DrawLine(p, p + arm, new Color(0.2f, 0.22f, 0.25f), 2.5f);
            ci.DrawCircle(p + arm, r * 0.38f, new Color(0.6f, 0.65f, 0.7f, 0.25f + 0.15f * MathF.Sin(t * 60 + k)));
            ci.DrawArc(p + arm, r * 0.38f, 0, MathF.Tau, 12, new Color(0.5f, 0.55f, 0.6f, 0.6f), 1);
        }
        ci.DrawCircle(p, r * 0.45f, new Color(0.25f, 0.28f, 0.33f).Lerp(Colors.White, flash));
        ci.DrawCircle(p + f * r * 0.3f, r * 0.15f, trim);
    }

    public static void Turret(CanvasItem ci, Vector2 p, float facing, float r, Color trim, float flash = 0)
    {
        Vector2 f = new(MathF.Cos(facing), MathF.Sin(facing));
        ci.DrawRect(new Rect2(p - new Vector2(r, r), new Vector2(r * 2, r * 2)), new Color(0.18f, 0.2f, 0.22f).Lerp(Colors.White, flash));
        ci.DrawRect(new Rect2(p - new Vector2(r, r), new Vector2(r * 2, r * 2)), trim with { A = 0.5f }, false, 1.5f);
        ci.DrawCircle(p, r * 0.7f, new Color(0.3f, 0.32f, 0.35f).Lerp(Colors.White, flash));
        ci.DrawLine(p, p + f * r * 1.6f, new Color(0.1f, 0.1f, 0.12f), r * 0.35f);
        ci.DrawCircle(p, r * 0.25f, trim);
    }

    public static void Camera(CanvasItem ci, Vector2 p, float facing, float r, Color led)
    {
        Vector2 f = new(MathF.Cos(facing), MathF.Sin(facing));
        Vector2 side = new(-f.Y, f.X);
        ci.DrawColoredPolygon(new[] { p - f * r + side * r * 0.6f, p + f * r * 1.2f + side * r * 0.45f, p + f * r * 1.2f - side * r * 0.45f, p - f * r - side * r * 0.6f }, new Color(0.8f, 0.82f, 0.85f));
        ci.DrawCircle(p + f * r * 1.1f, r * 0.3f, new Color(0.05f, 0.05f, 0.08f));
        ci.DrawCircle(p - f * r * 0.4f, r * 0.2f, led);
    }

    /// <summary>Traffic: a car of the given kind, with what makes it that kind.</summary>
    public static void Vehicle(CanvasItem ci, CarKind kind, Vector2 p, float heading, float len, float wid, Color body, bool brake, float t)
    {
        if (kind == CarKind.Bike) { Car(ci, p, heading, len, wid, body, false, brake, true); return; }
        Vector2 f = new(MathF.Cos(heading), MathF.Sin(heading)), s = new(-f.Y, f.X);
        Vector2 Q(float a, float b) => p + f * a * len / 2 + s * b * wid / 2;
        switch (kind)
        {
            case CarKind.Bus:
            case CarKind.Garbage:
            case CarKind.IceCream:
            {
                ci.DrawColoredPolygon(new[] { Q(1.05f, 1.1f) + new Vector2(4, 6), Q(1.05f, -1.1f) + new Vector2(4, 6), Q(-1.05f, -1.1f) + new Vector2(4, 6), Q(-1.05f, 1.1f) + new Vector2(4, 6) }, new Color(0, 0, 0, 0.35f));
                ci.DrawColoredPolygon(new[] { Q(1, 0.95f), Q(1, -0.95f), Q(-1, -0.95f), Q(-1, 0.95f) }, body);
                ci.DrawColoredPolygon(new[] { Q(1, 0.9f), Q(1, -0.9f), Q(0.8f, -0.9f), Q(0.8f, 0.9f) }, new Color(0.12f, 0.2f, 0.28f));
                if (kind == CarKind.Bus)
                    for (int k = 0; k < 6; k++) ci.DrawLine(Q(0.65f - k * 0.27f, 0.75f), Q(0.65f - k * 0.27f, -0.75f), new Color(0.75f, 0.85f, 1f, 0.5f), 2);
                if (kind == CarKind.Garbage)
                {
                    ci.DrawColoredPolygon(new[] { Q(0.7f, 0.85f), Q(0.7f, -0.85f), Q(-0.95f, -0.85f), Q(-0.95f, 0.85f) }, body.Darkened(0.25f));
                    for (int k = 0; k < 4; k++) ci.DrawLine(Q(0.5f - k * 0.38f, 0.85f), Q(0.5f - k * 0.38f, -0.85f), body.Darkened(0.45f), 2);
                }
                if (kind == CarKind.IceCream)
                {
                    ci.DrawLine(Q(0.7f, 0.95f), Q(-1, 0.95f), new Color(1f, 0.45f, 0.7f), 4);
                    ci.DrawLine(Q(0.7f, -0.95f), Q(-1, -0.95f), new Color(1f, 0.45f, 0.7f), 4);
                    Vector2 cone = Q(-0.2f, 0);
                    ci.DrawColoredPolygon(new[] { cone + f * 2 + s * 6, cone + f * 2 - s * 6, cone - f * 12 }, new Color(0.9f, 0.7f, 0.4f));
                    ci.DrawCircle(cone + f * 5, 6.5f, new Color(1f, 0.6f, 0.8f));
                    DrawGlow(ci, cone, len * 0.5f, new Color(1f, 0.5f, 0.8f, 0.25f + 0.1f * MathF.Sin(t * 4)));
                }
                if (brake) DrawGlow(ci, Q(-1.1f, 0), len * 0.2f, new Color(1, 0.1f, 0.05f, 0.4f));
                return;
            }
            case CarKind.Limo:
                Car(ci, p, heading, len, wid, body, false, brake);
                for (int k = 0; k < 3; k++) ci.DrawLine(Q(0.3f - k * 0.3f, 0.75f), Q(0.3f - k * 0.3f, -0.75f), new Color(0.2f, 0.25f, 0.35f), 3);
                return;
            case CarKind.Taxi:
                Car(ci, p, heading, len, wid, body, false, brake);
                ci.DrawRect(new Rect2(p - new Vector2(5, 3), new Vector2(10, 6)), new Color(1f, 0.95f, 0.6f));
                ci.DrawCircle(p, 4, new Color(1f, 1f, 0.8f, 0.5f));
                return;
            case CarKind.Ambulance:
            {
                // A TraumaLink box: white, a green stripe and cross, the light bar going.
                ci.DrawColoredPolygon(new[] { Q(1.05f, 1.1f) + new Vector2(4, 6), Q(1.05f, -1.1f) + new Vector2(4, 6), Q(-1.05f, -1.1f) + new Vector2(4, 6), Q(-1.05f, 1.1f) + new Vector2(4, 6) }, new Color(0, 0, 0, 0.35f));
                ci.DrawColoredPolygon(new[] { Q(1, 0.95f), Q(1, -0.95f), Q(-1, -0.95f), Q(-1, 0.95f) }, body);
                ci.DrawColoredPolygon(new[] { Q(1, 0.9f), Q(1, -0.9f), Q(0.78f, -0.9f), Q(0.78f, 0.9f) }, new Color(0.12f, 0.2f, 0.28f));
                Color green = new(0.15f, 0.75f, 0.4f);
                ci.DrawLine(Q(0.7f, 0.95f), Q(-1, 0.95f), green, 3);
                ci.DrawLine(Q(0.7f, -0.95f), Q(-1, -0.95f), green, 3);
                Vector2 cross = Q(-0.25f, 0);
                ci.DrawLine(cross - f * 6, cross + f * 6, green, 4);
                ci.DrawLine(cross - s * 6, cross + s * 6, green, 4);
                bool flash = MathF.Sin(t * 14) > 0;
                ci.DrawCircle(Q(0.62f, 0.5f), 2.6f, flash ? new Color(1, 0.2f, 0.15f) : new Color(0.4f, 0.05f, 0.05f));
                ci.DrawCircle(Q(0.62f, -0.5f), 2.6f, !flash ? new Color(0.3f, 0.6f, 1f) : new Color(0.05f, 0.1f, 0.3f));
                DrawGlow(ci, Q(0.62f, flash ? 0.5f : -0.5f), len * 0.45f, flash ? new Color(1, 0.2f, 0.15f, 0.3f) : new Color(0.3f, 0.6f, 1f, 0.3f));
                if (brake) DrawGlow(ci, Q(-1.1f, 0), len * 0.2f, new Color(1, 0.1f, 0.05f, 0.4f));
                return;
            }
            case CarKind.Patrol:
                Car(ci, p, heading, len, wid, body, false, brake);
                ci.DrawLine(Q(0.05f, 0.7f), Q(0.05f, -0.7f), new Color(0.05f, 0.05f, 0.1f), 5);
                ci.DrawCircle(Q(0.05f, 0.45f), 2.5f, new Color(0.5f, 0, 0));
                ci.DrawCircle(Q(0.05f, -0.45f), 2.5f, new Color(0, 0.15f, 0.5f));
                return;
            default:
                Car(ci, p, heading, len, wid, body, false, brake);
                return;
        }
    }

    // ---- cars ---------------------------------------------------------------------

    /// <summary>A car from above. Size in pixels (length, width).</summary>
    public static void Car(CanvasItem ci, Vector2 p, float heading, float len, float wid, Color body, bool lights, bool brake, bool bike = false, float lean = 0, float wheelie = 0)
    {
        Vector2 f = new(MathF.Cos(heading), MathF.Sin(heading));
        Vector2 s = new(-f.Y, f.X);
        Vector2 Q(float a, float b) => p + f * a * len / 2 + s * b * wid / 2;
        ci.DrawColoredPolygon(new[] { Q(1.05f, 1.1f) + new Vector2(4, 6), Q(1.05f, -1.1f) + new Vector2(4, 6), Q(-1.05f, -1.1f) + new Vector2(4, 6), Q(-1.05f, 1.1f) + new Vector2(4, 6) }, new Color(0, 0, 0, 0.35f));
        if (bike)
        {
            // Tyres, frame and tank; the rider leans into the corner; a wheelie lifts the front.
            Vector2 lift = new Vector2(0, -wheelie * 6);
            ci.DrawLine(Q(-1, 0), Q(-0.55f, 0), new Color(0.05f, 0.05f, 0.06f), wid * 0.55f);
            ci.DrawLine(Q(0.55f, 0) + lift, Q(1, 0) + lift, new Color(0.05f, 0.05f, 0.06f), wid * 0.55f);
            ci.DrawLine(Q(-0.7f, 0), Q(0.8f, 0) + lift * 0.6f, new Color(0.25f, 0.25f, 0.28f), wid * 0.35f);
            ci.DrawColoredPolygon(new[] { Q(0.7f, 0.55f) + lift * 0.5f, Q(0.7f, -0.55f) + lift * 0.5f, Q(0.1f, -0.65f), Q(0.1f, 0.65f) }, body);
            ci.DrawColoredPolygon(new[] { Q(0.05f, 0.45f), Q(0.05f, -0.45f), Q(-0.65f, -0.4f), Q(-0.65f, 0.4f) }, body.Darkened(0.35f));
            ci.DrawLine(Q(0.6f, 1.25f) + lift, Q(0.6f, -1.25f) + lift, new Color(0.7f, 0.7f, 0.75f), 2);
            Vector2 rider = Q(-0.15f, lean * 0.9f);
            ci.DrawCircle(rider, wid * 0.45f, new Color(0.12f, 0.12f, 0.15f));
            ci.DrawCircle(Q(0.15f, lean * 1.1f), wid * 0.32f, body.Lightened(0.4f));
            ci.DrawCircle(Q(0.18f, lean * 1.1f) + f * 1.5f, wid * 0.12f, new Color(0.3f, 0.8f, 1f, 0.9f));
            if (lights) DrawGlow(ci, Q(1.7f, 0) + lift, len * 0.7f, new Color(1, 0.95f, 0.8f, 0.25f));
            if (brake) DrawGlow(ci, Q(-1.15f, 0), len * 0.25f, new Color(1, 0.1f, 0.05f, 0.45f));
            return;
        }
        var hull = new[] { Q(0.92f, 0.85f), Q(1f, 0.6f), Q(1f, -0.6f), Q(0.92f, -0.85f), Q(-0.95f, -0.9f), Q(-1f, -0.7f), Q(-1f, 0.7f), Q(-0.95f, 0.9f) };
        ci.DrawColoredPolygon(hull, body);
        // Roof and glass.
        ci.DrawColoredPolygon(new[] { Q(0.45f, 0.7f), Q(0.45f, -0.7f), Q(-0.55f, -0.72f), Q(-0.55f, 0.72f) }, body.Darkened(0.15f));
        ci.DrawColoredPolygon(new[] { Q(0.62f, 0.72f), Q(0.62f, -0.72f), Q(0.42f, -0.65f), Q(0.42f, 0.65f) }, new Color(0.12f, 0.2f, 0.28f));
        ci.DrawColoredPolygon(new[] { Q(-0.55f, 0.7f), Q(-0.55f, -0.7f), Q(-0.72f, -0.68f), Q(-0.72f, 0.68f) }, new Color(0.1f, 0.16f, 0.22f));
        ci.DrawLine(Q(0.9f, 0.85f), Q(-0.9f, 0.88f), body.Lightened(0.25f), 1);
        // Lamps.
        ci.DrawRect(new Rect2(Q(0.97f, 0.62f) - new Vector2(2, 2), new Vector2(4, 4)), new Color(1, 0.97f, 0.85f));
        ci.DrawRect(new Rect2(Q(0.97f, -0.62f) - new Vector2(2, 2), new Vector2(4, 4)), new Color(1, 0.97f, 0.85f));
        Color tail = brake ? new Color(1, 0.15f, 0.1f) : new Color(0.6f, 0.05f, 0.05f);
        ci.DrawRect(new Rect2(Q(-0.98f, 0.62f) - new Vector2(2, 2), new Vector2(4, 4)), tail);
        ci.DrawRect(new Rect2(Q(-0.98f, -0.62f) - new Vector2(2, 2), new Vector2(4, 4)), tail);
        if (lights)
        {
            DrawGlow(ci, Q(1.9f, 0.4f), len * 0.55f, new Color(1, 0.95f, 0.8f, 0.2f));
            DrawGlow(ci, Q(1.9f, -0.4f), len * 0.55f, new Color(1, 0.95f, 0.8f, 0.2f));
        }
        if (brake) DrawGlow(ci, Q(-1.2f, 0), len * 0.35f, new Color(1, 0.1f, 0.05f, 0.4f));
    }
}
