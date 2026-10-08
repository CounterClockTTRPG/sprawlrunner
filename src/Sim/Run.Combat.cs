using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public sealed partial class Run
{
    private readonly Queue<(V2 pos, float radius, float damage, GrenadeKind kind, int owner, float tileMul)> _blasts = new();

    // ---- shots ----------------------------------------------------------------------

    private void SpawnShot(Actor from, WeaponDef w, float angle, float damage, bool crit, float aimDist = 99)
    {
        V2 dir = M.Dir(angle);
        var s = new Projectile
        {
            Pos = from.Pos, Origin = from.Pos, Vel = dir * w.Speed, Damage = damage, Range = w.Range, Team = from.Team, Owner = from.Id,
            Pierce = w.Pierce, Crit = crit, Blast = w.Blast, AimDist = aimDist, NonLethal = w.NonLethal || from.IsPlayer && AmmoGel(w), Stun = w.Stun,
            Homing = from.IsPlayer && (w.Id == "hornet" && C.Has("smartlink") || w.Homing), Incendiary = w.Incendiary,
            ArmorMul = (from.IsPlayer && C.Ammo(w) == "apds" || from == Ally && C.CrewHas(Job.Hired, "apds") ? 0.5f : 1) * w.ArmorMul,
            Kind = w.Blast > 0 ? (w.Id == "e_fireball" ? ShotKind.Fireball : ShotKind.Rocket) : w.Pellets > 1 ? ShotKind.Pellet : ShotKind.Bullet,
            WallMul = (w.Class == WeaponClass.Sniper ? 1.2f : w.Pellets > 1 ? 0.9f : 0.5f) * (from.IsPlayer && C.Ammo(w) == "explosive" ? 2.5f : 1f),
        };
        Shots.Add(s);
    }

    private void StepShots(float dt)
    {
        for (int i = Shots.Count - 1; i >= 0; i--)
        {
            Projectile s = Shots[i];
            if (s.Homing) Home(s, dt);
            V2 a = s.Pos, b = s.Pos + s.Vel * dt;
            float segLen = V2.Distance(a, b);
            bool dead = false;
            // A shot aimed at a lamp puts it out (only the runner's: a firefight doesn't black out the site by itself).
            if (s.Team == 0 && s.Blast <= 0 && ShootLamp(s, a, b)) { Shots.RemoveAt(i); continue; }
            // Repeat while a pierce carries the shot through something in this step.
            for (int guard = 0; guard < 4 && !dead; guard++)
            {
                Actor hitActor = null;
                float bestT = 2;
                foreach (Actor t in Actors)
                {
                    if (t.Dead || t.Team == s.Team || t.Id == s.Owner || t.IsGhost) continue;
                    if (t.IsPlayer && DashT > 0) continue;
                    float d = M.SegDist(a, b, t.Pos, out float tt);
                    if (d < t.Radius + (s.Kind == ShotKind.Rocket ? 0.15f : 0.05f) && tt < bestT) { bestT = tt; hitActor = t; }
                }
                bool tileHit = FirstBlock(s, a, b, out Pt tile, out V2 at);
                float tileT = tileHit ? (segLen < 1e-6f ? 0 : V2.Distance(a, at) / segLen) : 2;
                if (hitActor != null && bestT <= tileT)
                {
                    V2 where = V2.Lerp(a, b, bestT);
                    if (s.Blast > 0) { Blast(s, where); dead = true; break; }
                    // A riot shield takes what comes from the front.
                    if (hitActor.Is(EnemyKind.Shield) && hitActor.Stun <= 0 && MathF.Abs(M.AngleDiff(hitActor.Facing, M.Angle(s.Origin - hitActor.Pos))) < 1.05f)
                    {
                        Events.Add(new RunEvent(Ev.Impact, where, M.Norm(s.Vel), 0, (int)Tile.Reinforced));
                        Hurt(hitActor, s.Damage * 0.08f, ActorById(s.Owner), false);
                        dead = true;
                        break;
                    }
                    Hurt(hitActor, s.Damage, ActorById(s.Owner), false, s.Crit, nonLethal: s.NonLethal, armorMul: s.ArmorMul);
                    if (s.Stun > 0 && !hitActor.Dead && hitActor.Def != null && !hitActor.Is(EnemyKind.Camera)) hitActor.Stun = MathF.Max(hitActor.Stun, s.Stun);
                    if (s.Pierce && !hitActor.Dead) { dead = true; break; }
                    if (s.Pierce) { s.Damage *= 0.7f; a = where + M.Norm(s.Vel) * (hitActor.Radius * 2); continue; }
                    dead = true;
                    break;
                }
                if (tileHit)
                {
                    if (s.Blast > 0) { Blast(s, at); dead = true; break; }
                    Tile before = L[tile];
                    Events.Add(new RunEvent(Ev.Impact, at, M.Norm(s.Vel), 0, (int)before));
                    bool broke = DamageTile(tile, s.Damage * s.WallMul, at);
                    if (broke && (s.Pierce || before == Tile.Glass))
                    {
                        s.Damage *= 0.7f;
                        a = at;
                        continue;
                    }
                    dead = true;
                    break;
                }
                break;
            }
            s.Range -= segLen;
            s.Pos = b;
            if (dead || s.Range <= 0)
            {
                if (!dead && s.Blast > 0) Blast(s, b);
                // A flamethrower's fuel keeps burning where it lands.
                if (s.Incendiary) Ignite(s.Pos, 0.7f, 2.5f);
                Shots.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// The first tile that stops this shot. Low cover next to whoever fired is
    /// shot over when the aim is beyond it -- so cover protects the one crouched
    /// behind it, not the one leaning over it -- but hit when it is the target.
    /// </summary>
    private bool FirstBlock(Projectile s, V2 a, V2 b, out Pt tile, out V2 at)
    {
        Pt start = Pt.Of(a);
        Pt found = default;
        bool any = false;
        bool fromStart = Pt.Of(s.Origin) == start;
        var w = new Level.Walker(a, b);
        while (w.Next(out int x, out int y))
        {
            if (x == start.X && y == start.Y && fromStart) continue;
            Tile t = L[x, y];
            TileDef d = Level.Def(t);
            if (!d.BlocksShot) continue;
            float near = V2.Distance(s.Origin, new Pt(x, y).Centre);
            bool target = t is Tile.Rack or Tile.Vat or Tile.Console or Tile.Locker or Tile.Barrel;
            if (d.Low && !target && near < 1.25f && near + 0.45f < s.AimDist) continue;
            found = new Pt(x, y);
            any = true;
            break;
        }
        tile = found;
        at = any ? Level.Entry(a, b, found) : b;
        return any;
    }

    /// <summary>Does this shot, aimed at a lamp, pass close enough to it in this step? Then the lamp is out.</summary>
    private bool ShootLamp(Projectile s, V2 a, V2 b)
    {
        V2 aim = s.Origin + M.Norm(s.Vel) * s.AimDist;
        int k = L.LampNear(aim, Tables.LampAim);
        if (k < 0) return false;
        V2 lamp = L.Lights[k].pos;
        if (M.SegDist(a, b, lamp, out _) > Tables.LampHit || !L.Los(s.Origin, lamp)) return false;
        LampOut(k);
        return true;
    }

    /// <summary>A lamp goes out with a pop of glass: a little noise, and the room it lit is darker.</summary>
    public void LampOut(int k)
    {
        if (!L.LampsOut.Add(k)) return;
        V2 at = L.Lights[k].pos;
        Events.Add(new RunEvent(Ev.LampOut, at, default, 0, k));
        Noise(at, 5, true, quiet: true);
    }

    private void Blast(Projectile s, V2 at)
    {
        bool fire = s.Kind == ShotKind.Fireball;
        Explode(at, s.Blast, s.Damage, GrenadeKind.Frag, s.Owner, fire ? 0.6f : 3f);
        if (fire) Ignite(at, 1.2f, 3f);
    }

    private bool AmmoGel(WeaponDef w) => C.Ammo(w) == "gel";

    /// <summary>Smart rounds bend towards the hostile nearest their line.</summary>
    private void Home(Projectile s, float dt)
    {
        V2 dir = M.Norm(s.Vel);
        Actor best = null;
        float bestAng = 0.35f;
        foreach (Actor a in Actors)
        {
            if (!a.Hostile || V2.Distance(a.Pos, s.Pos) > 10) continue;
            float ang = MathF.Abs(M.AngleDiff(M.Angle(dir), M.Angle(a.Pos - s.Pos)));
            if (ang < bestAng) { bestAng = ang; best = a; }
        }
        if (best == null) return;
        float speed = s.Vel.Length();
        float turned = M.TurnTo(M.Angle(dir), M.Angle(best.Pos - s.Pos), 4f * dt);
        s.Vel = M.Dir(turned) * speed;
    }

    public Actor ActorById(int id) => FindActor(id);

    // ---- damage ---------------------------------------------------------------------

    public void Hurt(Actor a, float raw, Actor by, bool explosive, bool crit = false, bool ignoreArmor = false, bool nonLethal = false, float armorMul = 1)
    {
        if (a.Dead || raw <= 0) return;
        if (a.IsPlayer && DashT > 0 && !explosive) return;
        if (a.IsPlayer && _graceT > 0) return;
        float armor = ignoreArmor ? 0 : a.IsPlayer ? C.ArmorValue : a.Armor;
        if (explosive) armor *= 0.5f;
        armor *= armorMul;
        // Spirits shrug off mundane bullets; magic and fire and blast still bite.
        if (a.Is(EnemyKind.Spirit) && !explosive && !ignoreArmor) raw *= 0.25f;
        if (by != null && by.IsPlayer && Adrenaline) raw *= 1.25f;
        // Armour stops what it can; what always gets through shrinks with heavier armour, to a last trickle.
        float dmg = MathF.Max(raw - armor, raw * MathF.Max(Tables.ArmorFloorMin, Tables.ArmorFloor * (1 - armor / Tables.ArmorFloorFade)));
        a.Hp -= dmg;
        a.HitFlash = 0.12f;
        if (a.Is(EnemyKind.Cloaker)) a.RevealT = Tables.CloakReveal;
        if (a.IsPlayer && dmg >= 3) Interrupted();
        if (by != null && by.IsPlayer && !a.IsPlayer) Events.Add(new RunEvent(Ev.HitMark, a.Pos, default, a.Hp - dmg <= 0 ? 1 : 0));
        Events.Add(new RunEvent(Ev.Hit, a.Pos, default, MathF.Min(dmg, a.Hp + dmg), a.Id, crit ? "crit" : null));
        if (a.IsMedic && a.Team == 2 && by != null && by.Team == 0) ShotAtTrauma();
        if (a.Surrendered && by != null && by.IsPlayer && a.Hp <= 0) KilledHelpless++;
        // Someone hit beside them -- or one of them hit: everyone near runs.
        Panic(a.Pos, a.IsNpc ? Tables.PanicSeen + 2 : Tables.PanicSeen);
        MaybeSurrender(a, by);
        if (by != null && by.Team != a.Team && a.Team == 1 && a.Def != null && !a.Is(EnemyKind.Camera))
        {
            if (!a.Alert) BecomeAlert(a, by.Pos);
            a.LastKnown = by.Pos;
            a.LostT = 0;
        }
        if (a.Hp <= 0 && CheatDeath(a)) return;
        if (a.Hp <= 0) Kill(a, by, nonLethal && !a.IsPlayer && a.Def != null && a.Kind is not (EnemyKind.Drone or EnemyKind.Turret or EnemyKind.Camera));
    }

    private void Kill(Actor a, Actor by, bool ko = false)
    {
        a.Dead = true;
        a.KO = ko;
        a.Hp = 0;
        Events.Add(new RunEvent(Ev.Death, a.Pos, default, a.Def != null ? (int)a.Def.Kind : -1, a.Id, ko ? "ko" : null));
        if (a.IsPlayer) { Phase = RunPhase.Dead; return; }
        if (a.IsAlly && a == Ally && _secondWind && !ko)
        {
            _secondWind = false;
            a.Dead = false;
            a.Hp = a.MaxHp / 2;
            Say($"{a.Name} gets back up. \"Not today.\"");
            return;
        }
        if (a.IsAlly) { Say(a == Ally ? $"{a.Name} is down! Carry them out (hold E) or lose them." : $"{a.Name} is down!"); return; }
        if (a.Is(EnemyKind.Spirit)) { a.Escaped = true; Events.Add(new RunEvent(Ev.Emp, a.Pos, default, 0.8f)); return; }
        if (a.Def == null) return;
        if (a.Team == 1 && !a.IsMedic) TraumaCall(a);
        if (by != null && by.Team == 0 && !ko) Kills++;
        if (ko) KnockOuts++;
        if (a == Vip) Say(ko ? $"{Job.Target} is out cold. That will do." : $"{Job.Target} is down.");
        RivalDown(a, by, ko);
        if (by != null && by.Rival && a == Vip && !RivalKilledTarget) { RivalKilledTarget = true; Say($"{by.Name} got {Job.Target} first. The Johnson will only pay half for it."); }
        if (a.Badge) { a.Badge = false; Pickups.Add(new Pickup { Kind = PickupKind.Badge, Pos = a.Pos + new V2(0.25f, -0.2f), Amount = 1, Name = "a keycard" }); }
        if (a.Def.Cash > 0 && R.Chance(0.45f))
            Pickups.Add(new Pickup { Kind = PickupKind.Cash, Pos = a.Pos, Amount = (int)(a.Def.Cash * R.Range(0.5f, 1.5f) * (1 + 0.3f * Job.Threat)) });
        else if (R.Chance(0.06f))
            Pickups.Add(new Pickup { Kind = PickupKind.Grenade, Pos = a.Pos, Amount = 1, Id = "frag" });
        if (a.Is(EnemyKind.Drone) || a.Is(EnemyKind.Turret))
            _blasts.Enqueue((a.Pos, 1.2f, 20, GrenadeKind.Frag, 0, 0.5f));
        if (a == Boss)
        {
            if (by != null && by.Team == 0) BossKills++;
            Pickups.Add(new Pickup { Kind = PickupKind.Loot, Pos = a.Pos + new V2(0.3f, 0.2f), Amount = 2500 * Job.Threat, Name = $"{a.Name}'s chrome" });
            Say($"{a.Name} is down.");
        }
    }

    /// <summary>Damage a tile; returns true if it broke. Barrels go up; burning stuff sets fires.</summary>
    public bool DamageTile(Pt p, float amount, V2 at)
    {
        Tile? was = L.Damage(p.X, p.Y, amount);
        if (was == null) return false;
        Events.Add(new RunEvent(Ev.Broken, p.Centre, default, 0, (int)was.Value));
        if (was is Tile.Drywall or Tile.Brick or Tile.Concrete or Tile.Glass) WallsBroken++;
        if (was == Tile.Barrel) _blasts.Enqueue((p.Centre, 2.6f, 70, GrenadeKind.Frag, 0, 2f));
        if (was is Tile.Rack or Tile.Vat && Job.Type == JobType.Destroy)
            Events.Add(new RunEvent(Ev.Objective, p.Centre, Text: $"{Job.Item}: {TargetsLeft} left"));
        if (was == Tile.Vat && Job.Style == SiteStyle.Lab) Gas(p.Centre, GasKind.Toxic, Tables.ToxicRadius, Tables.ToxicLife);
        else if (was == Tile.Vat) Ignite(p.Centre, 1.2f, 4);
        if (was == Tile.Pipe) Gas(p.Centre, GasKind.Coolant, Tables.CoolantRadius, Tables.CoolantLife);
        return true;
    }

    public int KnockOuts;

    // ---- grenades -------------------------------------------------------------------

    private void StepGrenades(float dt)
    {
        for (int i = Grenades.Count - 1; i >= 0; i--)
        {
            Grenade g = Grenades[i];
            g.T += dt;
            g.Fuse -= dt;
            bool air = g.T < g.Flight;
            if (!air) g.Vel *= MathF.Exp(-6 * dt);
            // Move axis by axis so a wall reflects the right component.
            for (int axis = 0; axis < 2; axis++)
            {
                V2 step = axis == 0 ? new V2(g.Vel.X * dt, 0) : new V2(0, g.Vel.Y * dt);
                V2 np = g.Pos + step;
                Pt t = Pt.Of(np);
                TileDef d = Level.Def(L[t]);
                bool blocks = d.BlocksMove && !(air && d.Low) && L[t] != Tile.Door;
                if (L[t] == Tile.Glass && air && g.Vel.Length() > 4) { DamageTile(t, 10, np); blocks = false; }
                if (L[t] == Tile.Door) { OpenDoorAt(t); blocks = false; }
                if (blocks)
                {
                    if (axis == 0) g.Vel.X = -g.Vel.X * 0.45f; else g.Vel.Y = -g.Vel.Y * 0.45f;
                    if (g.Vel.Length() > 1) Events.Add(new RunEvent(Ev.Bounce, g.Pos));
                }
                else g.Pos = np;
            }
            if (g.Fuse <= 0)
            {
                Grenades.RemoveAt(i);
                Detonate(g);
            }
        }
    }

    private void OpenDoorAt(Pt t)
    {
        float hp = L.Hp[L.Idx(t.X, t.Y)];
        L.Set(t, Tile.DoorOpen);
        L.Hp[L.Idx(t.X, t.Y)] = hp;
    }

    private void Detonate(Grenade g)
    {
        GrenadeDef d = g.Def;
        float mul = g.Owner == Player.Id ? C.GrenadeDamageMul : 0.75f;
        switch (d.Kind)
        {
            case GrenadeKind.Frag: Explode(g.Pos, d.Radius, d.Damage * mul, d.Kind, g.Owner, 2.2f); break;
            case GrenadeKind.Breach: Explode(g.Pos, d.Radius, d.Damage * mul, d.Kind, g.Owner, 14f); break;
            case GrenadeKind.Fire:
                Events.Add(new RunEvent(Ev.Explosion, g.Pos, default, d.Radius, (int)d.Kind));
                Ignite(g.Pos, d.Radius, 6);
                Noise(g.Pos, 10, true);
                break;
            case GrenadeKind.Flash:
                Events.Add(new RunEvent(Ev.Flash, g.Pos, default, d.Radius));
                foreach (Actor a in Actors)
                {
                    if (a.Dead || V2.Distance(a.Pos, g.Pos) > d.Radius + 1 || !L.Los(g.Pos, a.Pos)) continue;
                    if (a.IsPlayer) { FlashT = 1.0f; continue; }
                    if (a.Is(EnemyKind.Camera) || a.Is(EnemyKind.Turret) || a.Is(EnemyKind.Drone)) continue;
                    a.Stun = 3f;
                    if (a.Team == 1 && !a.Alert) BecomeAlert(a, g.Pos, shout: false);
                }
                Noise(g.Pos, 16, true);
                break;
            case GrenadeKind.Emp:
                Events.Add(new RunEvent(Ev.Emp, g.Pos, default, d.Radius));
                foreach (Actor a in ActorsNow)
                {
                    if (a.Dead || V2.Distance(a.Pos, g.Pos) > d.Radius) continue;
                    if (a.Is(EnemyKind.Drone)) Hurt(a, 999, Player, true);
                    else if (a.Is(EnemyKind.Turret) || a.Is(EnemyKind.Camera)) a.Stun = 8;
                }
                break;
        }
    }

    /// <summary>
    /// A blast: tiles in the radius take damage falling off with distance (and
    /// much less behind something solid); actors in sight of the centre take
    /// damage and a shove. Chain reactions queue rather than recurse.
    /// </summary>
    public void Explode(V2 pos, float radius, float damage, GrenadeKind kind, int owner, float tileMul)
    {
        _blasts.Enqueue((pos, radius, damage, kind, owner, tileMul));
        FlushExplosions();
    }

    private void FlushExplosions()
    {
        int guard = 0;
        while (_blasts.Count > 0 && guard++ < 64)
        {
            var (pos, radius, damage, kind, owner, tileMul) = _blasts.Dequeue();
            Events.Add(new RunEvent(Ev.Explosion, pos, default, radius, (int)kind));
            // A blast takes out the lamps near it (a flashbang or an EMP too: the bulbs go).
            for (int k = 0; k < L.Lights.Count; k++)
                if (!L.LampsOut.Contains(k) && V2.Distance(L.Lights[k].pos, pos) < radius * Tables.LampBlast) LampOut(k);
            Actor by = ActorById(owner);
            int r = (int)MathF.Ceiling(radius);
            Pt c = Pt.Of(pos);
            for (int y = c.Y - r; y <= c.Y + r; y++)
            for (int x = c.X - r; x <= c.X + r; x++)
            {
                var p = new Pt(x, y);
                float d = V2.Distance(p.Centre, pos);
                if (d > radius + 0.5f || !L.In(p)) continue;
                if (Level.Def(L[p]).Hp <= 0) continue;
                float f = 1 - MathF.Min(1, d / (radius + 0.5f)) * 0.7f;
                if (Shielded(pos, p)) f *= 0.25f;
                DamageTile(p, damage * tileMul * f, p.Centre);
            }
            foreach (Actor a in ActorsNow)
            {
                if (a.Dead) continue;
                float d = V2.Distance(a.Pos, pos);
                if (d > radius + a.Radius || !L.Los(pos, a.Pos) && d > 0.8f) continue;
                float f = 1 - MathF.Min(1, d / (radius + a.Radius)) * 0.75f;
                if (a.IsPlayer && DashT > 0) f *= 0.5f;
                Hurt(a, damage * f, by, true);
                if (!a.Static && d > 0.01f) Move(a, M.Norm(a.Pos - pos) * f * 0.6f);
            }
            Noise(pos, 26, owner == Player.Id);
            if (kind == GrenadeKind.Frag && radius >= 2.4f) Ignite(pos, 0.8f, 1.5f);
        }
    }

    /// <summary>Something solid between a blast and this tile?</summary>
    private bool Shielded(V2 from, Pt to)
    {
        Pt start = Pt.Of(from);
        var w = new Level.Walker(from, to.Centre);
        while (w.Next(out int x, out int y))
        {
            if (x == to.X && y == to.Y) return false;
            if (x == start.X && y == start.Y) continue;
            TileDef d = Level.Def(L[x, y]);
            if (d.BlocksMove && !d.Low) return true;
        }
        return false;
    }

    // ---- fire -----------------------------------------------------------------------

    public void Ignite(V2 at, float radius, float seconds)
    {
        int r = (int)MathF.Ceiling(radius);
        Pt c = Pt.Of(at);
        bool any = false;
        for (int y = c.Y - r; y <= c.Y + r; y++)
        for (int x = c.X - r; x <= c.X + r; x++)
        {
            var p = new Pt(x, y);
            if (!L.In(p) || V2.Distance(p.Centre, at) > radius + 0.3f) continue;
            TileDef d = Level.Def(L[p]);
            if (d.BlocksMove && !d.Flammable) continue;
            if (Shielded(at, p)) continue;
            int i = L.Idx(x, y);
            Fire[i] = MathF.Max(Fire.GetValueOrDefault(i), seconds * R.Range(0.8f, 1.2f));
            any = true;
        }
        if (any) Events.Add(new RunEvent(Ev.Ignite, at, default, radius));
    }

    private float _fireSpread;
    private readonly List<int> _fireTiles = new();

    /// <summary>The burning tiles as a list (reused), so the fire can change while it is walked.</summary>
    private List<int> FireTiles()
    {
        _fireTiles.Clear();
        _fireTiles.AddRange(Fire.Keys);
        return _fireTiles;
    }

    private void StepFire(float dt)
    {
        if (Fire.Count == 0) return;
        _fireSpread += dt;
        bool spread = _fireSpread > 0.5f;
        if (spread) _fireSpread = 0;
        float drown = FireMul();
        foreach (int i in FireTiles())
        {
            float left = Fire[i] - dt * drown;
            var p = new Pt(i % L.W, i / L.W);
            if (Level.Def(L[p]).Flammable)
            {
                DamageTile(p, 22 * dt, p.Centre);
                left = MathF.Max(left, 0.5f);
                if (!Level.Def(L[p]).Flammable) left = 1.5f;
            }
            if (left <= 0) { Fire.Remove(i); continue; }
            Fire[i] = left;
            if (spread)
                foreach (Pt d in Pt.Dirs4)
                {
                    Pt n = p + d;
                    if (!L.In(n) || !Level.Def(L[n]).Flammable) continue;
                    int ni = L.Idx(n.X, n.Y);
                    if (!Fire.ContainsKey(ni) && R.Chance(0.25f)) Fire[ni] = 3;
                    if (L[n] == Tile.Barrel) DamageTile(n, 10, n.Centre);
                }
        }
        foreach (Actor a in Actors)
        {
            if (a.Dead || a.Flying || a.Static) continue;
            if (Fire.ContainsKey(L.Idx(M.Clamp((int)a.Pos.X, 0, L.W - 1), M.Clamp((int)a.Pos.Y, 0, L.H - 1)))) a.Burn = 2f;
        }
        // Barrels in fire cook off.
        foreach (int i in FireTiles())
        {
            var p = new Pt(i % L.W, i / L.W);
            for (int k = 0; k < 4; k++)
            {
                Pt n = p + Pt.Dirs4[k];
                if (L[n] == Tile.Barrel) DamageTile(n, 12 * dt, n.Centre);
            }
        }
    }

    private void Burning(Actor a, float dt)
    {
        if (a.Burn <= 0) return;
        a.Burn -= dt;
        a.Hp -= 11 * dt;
        if (a.Hp <= 0 && !a.Dead) Kill(a, a.IsPlayer ? null : Player);
    }

    // ---- noise ----------------------------------------------------------------------

    /// <summary>
    /// Something loud at pos. Unaware hostiles within the radius come to look
    /// (or, when it was the player's gunfire, know where it came from).
    /// Quiet noises (footsteps) only make them turn and wonder.
    /// </summary>
    public void Noise(V2 pos, float radius, bool byPlayer, bool quiet = false)
    {
        if (byPlayer && !quiet) radius *= MusicMul(pos);
        if (radius <= 0.5f) return;
        // Bystanders hear it too: a gunshot or a blast sends them running (a silenced shot only right beside them).
        // Quiet sounds -- footsteps, a whistle, a blade -- don't.
        if (!quiet && (byPlayer || radius >= Tables.PanicLoud)) Panic(pos, MathF.Max(Tables.PanicQuiet, radius * 0.8f));
        foreach (Actor a in Actors)
        {
            if (!a.Hostile || a.Def == null || a.Is(EnemyKind.Camera) || a.Stun > 0) continue;
            float d = V2.Distance(a.Pos, pos);
            if (d > radius) continue;
            // Sleepers sleep through anything that isn't loud and close.
            if (a.Mood == Mood.Asleep && (quiet ? d > Tables.WakeQuiet : d > radius * Tables.WakeLoud)) continue;
            // Walls muffle.
            if (!L.Los(a.Pos, pos) && d > radius * 0.6f) continue;
            if (a.Alert)
            {
                if (byPlayer && !quiet) { a.LastKnown = Player.Pos; a.LostT = MathF.Min(a.LostT, 2); }
                continue;
            }
            if (quiet)
            {
                a.Detect = MathF.Max(a.Detect, 0.45f);
                a.State = AiState.Suspicious;
                a.Goal = pos;
                a.StateT = 0;
                continue;
            }
            if (byPlayer && d < radius * 0.5f) BecomeAlert(a, Player.Pos);
            else
            {
                a.State = AiState.Suspicious;
                a.Detect = MathF.Max(a.Detect, 0.6f);
                a.Goal = pos;
                a.StateT = 0;
            }
        }
    }
}
