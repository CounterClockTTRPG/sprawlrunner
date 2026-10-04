using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>When a storylet may fire: the start of a day, after a job is settled, on walking in somewhere, in your sleep.</summary>
public enum Hook { Morning, Debrief, Place, Sleep }

/// <summary>
/// A message for the runner: mail in the inbox, a call that rings as soon as
/// you are back on the street, or a scene played when you walk into a place.
/// Its words are fixed when it fires (<see cref="Vars"/> fill the blanks);
/// the choices are read live off the storylet, so what they need is checked
/// against the career as it is now.
/// </summary>
public sealed class Mail
{
    public int Id;
    public string Story;
    public string From, Subject, Text;
    public int Day;
    public bool Read, Call;
    /// <summary>A scene at this place (contact id or "home"), shown when you walk in.</summary>
    public string Place;
    public Dictionary<string, string> Vars = new();
    /// <summary>The choice taken (-1: none yet), and what came of it.</summary>
    public int Answer = -1;
    public string Reply;
}

/// <summary>An answer to a storylet: what it says, what it does (returns the reply), and what stops it.</summary>
public sealed record Choice(string Text, Func<Career, Mail, string> Then, Func<Career, Mail, string> Block = null, string Effect = null);

/// <summary>
/// A small piece of story: a hook, a test on the career, words, and choices.
/// Once per career unless <see cref="Cooldown"/> (days) says otherwise.
/// </summary>
public sealed record Storylet(
    string Id, Hook Hook, string From, string Subject, string Text, Func<Career, bool> When,
    Choice[] Choices = null, float Weight = 1, int Cooldown = -1, string Place = null, bool Call = false,
    Func<Career, Dictionary<string, string>> Bind = null, Action<Career, Mail> Fire = null);

/// <summary>What the last settled job was, for the storylets that answer it.</summary>
public sealed class LastRun
{
    public Job Job;
    public bool Success, Alarm, Died, Trap, Stiffed;
    public int Kills, Pay;
    /// <summary>What became of the named target: killed, fled, taken, rescued, dead (a hostage), or null.</summary>
    public string Fate;
    /// <summary>Why the extraction was burned, when it was.</summary>
    public string Exfil;
    /// <summary>Decisions made on the run.</summary>
    public List<string> Choices = new();

    public bool Asked(string id) => Choices.Any(c => c.StartsWith(id + ":"));
    public bool Picked(string choice) => Choices.Contains(choice);
}

public sealed partial class Career
{
    /// <summary>Story state: named counters (0 = unset).</summary>
    public Dictionary<string, int> Flags = new();
    /// <summary>The day each storylet last fired.</summary>
    public Dictionary<string, int> Fired = new();
    public List<Mail> Inbox = new();
    public int NextMailId = 1;
    public LastRun Last;
    /// <summary>Bad deeds the street remembers (0-10, never fade), and how famous you are (0-10, -1 every four days).</summary>
    public int Notoriety, Awareness;
    /// <summary>Social codes you can talk: Street, Corporate, Gang, Security, Magic.</summary>
    public List<string> Etiquettes = new();

    public static readonly string[] AllEtiquettes = { "Street", "Corporate", "Gang", "Security", "Magic" };
    public const int EtiquetteCost = 12;

    public int Flag(string k) => Flags.GetValueOrDefault(k);
    public void Set(string k, int v = 1) { if (v == 0) Flags.Remove(k); else Flags[k] = v; }
    public void Inc(string k, int by = 1) => Set(k, Flag(k) + by);
    /// <summary>A contact thinks a little better of you (trust +1, up to 5).</summary>
    public void Befriend(string contact) => Loyalty[contact] = Math.Min(5, Loyalty.GetValueOrDefault(contact) + 1);
    /// <summary>A hash that is the same on every run (string.GetHashCode is not).</summary>
    public static uint Stable(string s) { uint h = 2166136261; foreach (char ch in s ?? "") h = (h ^ ch) * 16777619; return h; }
    public bool Talks(string etiquette) => Etiquettes.Contains(etiquette);

    public void Infamy(int by)
    {
        Notoriety = M.Clamp(Notoriety + by, 0, 10);
        if (by > 0) Explain("notoriety", "Notoriety: the street remembers what you did. It never fades -- the clean contacts charge more, and Johnsons set you up more often.");
    }

    public void Fame(int by)
    {
        Awareness = M.Clamp(Awareness + by, 0, 10);
        if (by > 0) Explain("fame", "Public awareness: your face is getting known. It fades on its own; above 3, every morning starts with heat, and at 6 they come to your door.");
    }

    /// <summary>One-line explainers, each shown once per career the first time its system changes something.</summary>
    public List<string> Explainers = new();

    public void Explain(string id, string text)
    {
        if (Flag("seen:" + id) > 0) return;
        Set("seen:" + id);
        Explainers.Add(text);
    }

    public int Unread => Inbox.Count(m => !m.Read);

    public string LearnEtiquette(string e)
    {
        if (Talks(e)) return "You already talk it.";
        int cost = (int)MathF.Round(EtiquetteCost * KarmaMul);
        if (Karma < cost) return $"Needs {cost} karma.";
        Karma -= cost;
        Etiquettes.Add(e);
        return null;
    }

    // ---- firing ---------------------------------------------------------------------

    private bool Ready(Storylet s) => Fired.TryGetValue(s.Id, out int day) ? s.Cooldown >= 0 && Day - day >= s.Cooldown : true;

    /// <summary>
    /// The storylets that fit now, at this hook (and place). Their own random
    /// stream, from the seed, the day and the hook: never <see cref="Rng"/>,
    /// so story never reshuffles the job board.
    /// </summary>
    public List<Storylet> Eligible(Hook hook, string place = null) =>
        Storylets.All.Where(s => s.Hook == hook && s.Place == place && Ready(s) && Safe(s.When)).ToList();

    private bool Safe(Func<Career, bool> when)
    {
        try { return when(this); }
        catch (Exception) { return false; }
    }

    private Rng StoryRng(Hook hook, string place) =>
        new(Seed ^ ((ulong)Day * 0x9E3779B1UL + (ulong)hook * 0x51ED27UL + (ulong)Stable(place) * 0x2545UL + 0x5707UL));

    /// <summary>Fire at most <paramref name="max"/> storylets at a hook; returns the mail they sent.</summary>
    public List<Mail> Tell(Hook hook, string place = null, int max = 1)
    {
        var sent = new List<Mail>();
        Rng r = StoryRng(hook, place);
        for (int k = 0; k < max; k++)
        {
            var ok = Eligible(hook, place);
            if (ok.Count == 0) break;
            float roll = r.Float() * ok.Sum(s => s.Weight);
            Storylet pick = ok[^1];
            foreach (Storylet s in ok) { roll -= s.Weight; if (roll <= 0) { pick = s; break; } }
            sent.Add(Send(pick));
        }
        return sent;
    }

    /// <summary>Fire one storylet now, whatever its test says (story beats call this; so do the checks).</summary>
    public Mail Send(Storylet s)
    {
        Fired[s.Id] = Day;
        var m = new Mail { Id = NextMailId++, Story = s.Id, From = s.From, Subject = s.Subject, Day = Day, Call = s.Call, Place = s.Place };
        if (s.Bind != null) foreach (var kv in s.Bind(this)) m.Vars[kv.Key] = kv.Value;
        m.From = Fill(s.From, m);
        m.Subject = Fill(s.Subject, m);
        m.Text = Fill(s.Text, m);
        // A storylet with nothing to answer has said everything when it is read.
        if (s.Choices == null || s.Choices.Length == 0) m.Answer = 0;
        s.Fire?.Invoke(this, m);
        Inbox.Add(m);
        if (Inbox.Count > 60) Inbox.Remove(Inbox.FirstOrDefault(x => x.Read && x.Answer >= 0) ?? Inbox[0]);
        return m;
    }

    public Mail Send(string id) => Send(Storylets.ById(id));

    public static string Fill(string text, Mail m)
    {
        if (text == null || m.Vars.Count == 0) return text;
        foreach (var kv in m.Vars)
        {
            // {¥key}: a sum of money, printed as one.
            if (int.TryParse(kv.Value, out int n)) text = text.Replace("{¥" + kv.Key + "}", M.Money(n));
            text = text.Replace("{" + kv.Key + "}", kv.Value);
        }
        return text;
    }

    public Storylet StoryOf(Mail m) => Storylets.All.FirstOrDefault(s => s.Id == m.Story);

    /// <summary>The choices of a mail, filled in, with what blocks each (null: free to take).</summary>
    public List<(string text, string block)> Options(Mail m)
    {
        Storylet s = StoryOf(m);
        if (s?.Choices == null) return new();
        return s.Choices.Select(ch => (Fill(ch.Text, m), m.Answer >= 0 ? "Answered." : ch.Block?.Invoke(this, m))).ToList();
    }

    public bool Waiting(Mail m) => m.Answer < 0 && StoryOf(m)?.Choices is { Length: > 0 };

    /// <summary>What a choice does to the numbers, when it says (shown beside the button).</summary>
    public string EffectOf(Mail m, int k) => StoryOf(m)?.Choices is { } ch && k < ch.Length ? Fill(ch[k].Effect, m) : null;

    /// <summary>Storyline mail (the campaigns, the contacts' own stories, the heist, the crew): marked so it doesn't drown.</summary>
    public static bool Storyline(Mail m) => m.Story is not null && (m.Story.StartsWith("story-") || m.Story.StartsWith("tide-") || m.Story.StartsWith("cut-") || m.Story.StartsWith("wyrm-")
        || m.Story.StartsWith("fork-") || m.Story.StartsWith("heist") || m.Story.StartsWith("crew"));

    /// <summary>Answer a mail. Returns what happened, or why it could not.</summary>
    public string Choose(Mail m, int k)
    {
        Storylet s = StoryOf(m);
        m.Read = true;
        if (s?.Choices == null || k < 0 || k >= s.Choices.Length) return "No such answer.";
        if (m.Answer >= 0) return "Already answered.";
        string block = s.Choices[k].Block?.Invoke(this, m);
        if (block != null) return block;
        m.Answer = k;
        m.Reply = Fill(s.Choices[k].Then(this, m) ?? "Done.", m);
        Say($"{m.From}: {m.Subject} -- {Fill(s.Choices[k].Text, m)}.");
        return null;
    }

    /// <summary>The next call waiting to ring (unread), if any.</summary>
    public Mail NextCall() => Inbox.FirstOrDefault(m => m.Call && !m.Read);

    /// <summary>Walking in somewhere: a scene waiting there, or a new one.</summary>
    public Mail SceneAt(string place)
    {
        if (Shut(place) != null) return null;
        Mail waiting = Inbox.FirstOrDefault(m => m.Place == place && !m.Read);
        if (waiting != null) return waiting;
        if (Fired.Any(kv => kv.Value == Day && Storylets.ById(kv.Key)?.Place == place)) return null;
        return Tell(Hook.Place, place).FirstOrDefault();
    }

    // ---- jobs from the story ---------------------------------------------------------

    /// <summary>
    /// Put a hand-made job on the board (for storylets): a free lot in the
    /// district, the pay of a contact-story job times <paramref name="payMul"/>.
    /// </summary>
    public Job OfferSpecial(JobType type, SiteStyle style, Faction faction, string district, int threat, string title, string brief,
        string employer = "fixer", float payMul = 1.4f, string tag = null, string target = null, string item = null, int expires = 10, int lot = -1)
    {
        City city = CityRef;
        if (city == null) return null;
        var taken = new HashSet<int>(Board.Select(b => b.Lot).Concat(Board.Select(b => b.Lot2)));
        if (lot < 0) lot = city.FreeSite(district, Rng, taken);
        if (lot < 0) lot = city.FreeSite(Tables.Districts.First(d => d.MaxThreat >= threat).Id, Rng, taken);
        if (lot < 0) return null;
        var j = new Job
        {
            Id = NextJobId++, Type = type, Style = style, Faction = faction, District = city.Lots[lot].District, Threat = M.Clamp(threat, 1, 6),
            Seed = Rng.Next(), Expires = Day + expires, Lot = lot, Employer = employer, Title = title, Brief = brief, Tag = tag,
            Target = target ?? Tables.TargetNames[(int)(Rng.Next() % (ulong)Tables.TargetNames.Length)], Item = item ?? "package", Count = 3,
        };
        j.Pay = (int)(MathF.Round(800 * MathF.Pow(j.Threat, 1.6f) * payMul / 50) * 50);
        j.Karma = 3 + 2 * j.Threat;
        Board.Add(j);
        return j;
    }
}
