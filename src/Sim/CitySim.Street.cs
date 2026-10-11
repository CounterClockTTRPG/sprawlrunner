using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>What a street encounter looks like from the car.</summary>
public enum StreetKind { Waving, Wreck, Toll, Patrol, Racer, Van, Ghoul }

/// <summary>Something on the street ahead: a storylet waiting for you to stop beside it.</summary>
public sealed class StreetEncounter
{
    public string Story;
    public StreetKind Kind;
    public V2 At;
    public float Heading, Life;
}

/// <summary>
/// A passenger or a package aboard (from a street encounter): stop at the
/// kerb of <see cref="Lot"/> and it pays. It lapses at the end of the day.
/// </summary>
public sealed class Fare
{
    public int Lot, Pay, Karma, Day;
    /// <summary>Who or what is aboard (for the HUD), what they say when you arrive, and an extra on arrival ("lead": a job tip).</summary>
    public string Who, Arrive, Extra;
}

public sealed partial class CitySim
{
    /// <summary>Street encounters happen only on a real drive (not the title's drifting camera, not the checks).</summary>
    public bool Encounters;
    public StreetEncounter Street;
    private float _streetT = Tables.StreetFirst;

    /// <summary>
    /// Driving, with nobody on you: now and then the street has something for
    /// you -- placed on a kerb (or across the lane) ahead, kept a while, gone
    /// once you're far past it.
    /// </summary>
    private void StepStreet(float dt)
    {
        StepFare();
        if (Street != null)
        {
            Street.Life -= dt;
            if (Street.Life <= 0 || V2.Distance(Street.At, Player.Pos) > Tables.StreetGone) Street = null;
            return;
        }
        if (!Encounters || C.Day < 2 || Chasers.Count > 0 || C.Courier || C.Passenger != null || Race != null || Player.Speed < 3 || City.OnCauseway(Player.Pos)) return;
        if ((_streetT -= dt) > 0) return;
        _streetT = Tables.StreetEvery;
        if (C.Flag("street:day") == C.Day && C.Flag("street:n") >= Tables.StreetPerDay) return;
        string district = City.DistrictAt[(int)M.Clamp(Player.Pos.Y, 0, City.H - 1) * City.W + (int)M.Clamp(Player.Pos.X, 0, City.W - 1)];
        Storylet s = C.PickStreet(district);
        if (s == null) return;
        StreetKind kind = Enum.Parse<StreetKind>(s.Place[7..], true);
        if (!Ahead(kind is not (StreetKind.Waving or StreetKind.Wreck), out V2 at, out float heading)) return;
        if (C.Flag("street:day") != C.Day) { C.Set("street:day", C.Day); C.Set("street:n", 0); }
        C.Inc("street:n");
        Street = new StreetEncounter { Story = s.Id, Kind = kind, At = at, Heading = heading, Life = Tables.StreetLife };
        Events.Add(new CityEvent(CityEv.Message, at, 1, kind switch
        {
            StreetKind.Wreck => "Hazard lights ahead. Somebody's had a bad night.",
            StreetKind.Toll => "Bikes across the lane ahead. A toll, Viper style.",
            StreetKind.Patrol => "Blue lights ahead. A Lone Star cruiser, waving cars over.",
            StreetKind.Racer => "A lime-green hotshot idling at the lights, revving at you.",
            StreetKind.Van => "An armoured van on its side ahead, doors buckled.",
            StreetKind.Ghoul => "Something hunched in the road ahead. It looks up into your lights.",
            _ => "Someone ahead is waving you down.",
        }));
    }

    /// <summary>
    /// A spot 16-26 tiles ahead: a pavement tile beside the road (or, for a
    /// toll, the road itself), clear of the doors you need.
    /// </summary>
    private bool Ahead(bool onRoad, out V2 at, out float heading)
    {
        at = default;
        heading = 0;
        V2 f = Player.Forward;
        var doors = Destinations().Select(l => l.Door.Centre).ToList();
        for (float d = 16; d <= 26; d += 2)
        {
            V2 p = Player.Pos + f * d;
            for (int r = 0; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                int x = (int)p.X + dx, y = (int)p.Y + dy;
                CTile t = City[x, y];
                bool ok = onRoad ? t == CTile.Road : t == CTile.Sidewalk && Pt.Dirs4.Any(n => City[x + n.X, y + n.Y] == CTile.Road);
                if (!ok) continue;
                var c = new V2(x + 0.5f, y + 0.5f);
                if (doors.Any(o => V2.Distance(o, c) < 10)) continue;
                at = c;
                heading = Player.Heading;
                return true;
            }
        }
        return false;
    }

    /// <summary>Stopped beside the encounter: the scene can play.</summary>
    public bool AtStreet() => Street != null && MathF.Abs(Player.Speed) < 3.5f && V2.Distance(Street.At, Player.Pos) < 3.2f;

    /// <summary>Stopped at the fare's kerb: it pays, and they get out.</summary>
    private void StepFare()
    {
        Fare f = C.Passenger;
        if (f == null || MathF.Abs(Player.Speed) > 2 || f.Lot < 0 || f.Lot >= City.Lots.Count) return;
        if (V2.Distance(City.Kerb(City.Lots[f.Lot]).pos, Player.Pos) > 3) return;
        Events.Add(new CityEvent(CityEv.Pickup, Player.Pos, 1, C.Deliver(f)));
    }

    /// <summary>Where the fare is going, for the HUD and the agent.</summary>
    public V2? FareKerb => C.Passenger is Fare f && f.Lot >= 0 && f.Lot < City.Lots.Count ? City.Kerb(City.Lots[f.Lot]).pos : null;
}

public sealed partial class Career
{
    /// <summary>A passenger or package aboard, from the street.</summary>
    public Fare Passenger;
    /// <summary>The district the car is in when the street is asked for an encounter (not saved).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string Here;

    /// <summary>
    /// The street encounter for this moment, if any: drawn by weight from the
    /// story's own random stream (seed, day, and how many the street has had today).
    /// </summary>
    public Storylet PickStreet(string district)
    {
        Here = district;
        // Street storylets say what they look like in their place: "street:waving", "street:wreck", "street:toll".
        var ok = Storylets.All.Where(s => s.Hook == Hook.Drive && s.Place?.StartsWith("street:") == true && Ready(s) && Safe(s.When)).ToList();
        if (ok.Count == 0) return null;
        Rng r = StoryRng(Hook.Drive, "street" + (Flag("street:day") == Day ? Flag("street:n") : 0));
        float roll = r.Float() * ok.Sum(s => s.Weight);
        foreach (Storylet s in ok) { roll -= s.Weight; if (roll <= 0) return s; }
        return ok[^1];
    }

    /// <summary>Take someone (or something) aboard to a lot.</summary>
    public void TakeAboard(int lot, int pay, string who, string arrive, int karma = 0, string extra = null) =>
        Passenger = new Fare { Lot = lot, Pay = pay, Who = who, Arrive = arrive, Karma = karma, Extra = extra, Day = Day };

    /// <summary>At the kerb: pay, karma, the extra; returns what's said.</summary>
    public string Deliver(Fare f)
    {
        Passenger = null;
        Cash += f.Pay;
        Count("earned", f.Pay);
        Karma += f.Karma;
        KarmaEarned += f.Karma;
        string extra = "";
        if (f.Extra == "lead" && CityRef != null)
        {
            Job j = OfferSpecial(JobType.Retrieve, SiteStyle.Office, Faction.Halcyon, "industrial", Math.Min(5, 1 + Rep / 3 + 1),
                "A runner's tip", "The runner you drove across town: 'Halcyon courier, a briefcase, a night shift that sleeps. I can't do it with my car on fire. You can.'", payMul: 1.6f);
            if (j != null) extra = $" A new job is on your board: {j.Title}.";
        }
        Count("fares");
        string paid = f.Pay > 0 ? $" {M.Money(f.Pay)}." : "";
        string karma = f.Karma > 0 ? $" Karma +{f.Karma}." : "";
        return $"{f.Arrive}{paid}{karma}{extra}";
    }
}
