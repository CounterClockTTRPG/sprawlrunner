using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Election week: every month or so the city votes for its council. Three
/// candidates -- Halcyon's "renewal" man, the Lotus' bought councillor, and a
/// reformer nobody backs -- and a week of jobs to rig it or protect her. The
/// heaviest campaign wins and sets the city's season for twenty days.
/// State lives in <see cref="Flags"/>: vote:n (this cycle, 1-based), vote:held
/// (the last cycle voted), vote:&lt;candidate&gt; (weight), vote:helped, vote:won.
/// </summary>
public sealed partial class Career
{
    public static readonly (string id, string name, string side, string season)[] Candidates =
    {
        ("halcyon", "Adrian Vell", "Halcyon's renewal candidate", "renewal"),
        ("lotus", "Henry Tsao", "the Red Lotus' councillor", "council"),
        ("reform", "Rosa Ibarra", "the reform candidate (nobody's)", "reform"),
    };

    public static (string id, string name, string side, string season) Candidate(string id) => Candidates.First(x => x.id == id);

    /// <summary>The morning election week <paramref name="k"/> (0-based) opens: about every 30 days from day 25, a seeded day or three late.</summary>
    public int ElectionStart(int k) => Tables.ElectionFirst + Tables.ElectionEvery * k + new Rng(Seed ^ 0xE1EC7104UL ^ (ulong)(k + 1) * 0x9E3779B1UL).Int(Tables.ElectionJitter + 1);

    public int VoteDay(int k) => ElectionStart(k) + Tables.ElectionWeek;

    /// <summary>The cycle in progress (1-based; 0 before the first).</summary>
    public int ElectionCycle => Flag("vote:n");

    public int VoteWeight(string candidate) => Flag("vote:" + candidate);

    /// <summary>Days until the count, while the week runs (-1 otherwise).</summary>
    public int DaysToVote => ElectionCycle > Flag("vote:held") ? VoteDay(ElectionCycle - 1) - Day : -1;

    /// <summary>The election line in the happenings list: the candidates, their weight, the days left.</summary>
    public string ElectionStatus()
    {
        int left = Math.Max(0, DaysToVote);
        string runners = string.Join(", ", Candidates.Select(x => $"{x.name} ({(x.id == "reform" ? "reform" : Tables.FactionNames[x.id == "halcyon" ? (int)Faction.Halcyon : (int)Faction.Lotus])}) {VoteWeight(x.id)}"));
        return $"the council votes in {left} day{(left == 1 ? "" : "s")}. {runners}. Every election job done is a vote; a tie goes to the Lotus' money.";
    }

    /// <summary>Called every morning: open the week, or count the votes.</summary>
    private void ElectionMorning()
    {
        if (Day < Tables.ElectionFirst) return;
        int k = (Day - Tables.ElectionFirst) / Tables.ElectionEvery;
        if (Day == ElectionStart(k) && ElectionCycle < k + 1) OpenElection(k + 1);
        if (Day >= VoteDay(k) && ElectionCycle == k + 1 && Flag("vote:held") < k + 1) CountVotes();
    }

    /// <summary>Election week <paramref name="n"/> begins: the news, the mail, the jobs on the board.</summary>
    public void OpenElection(int n)
    {
        Set("vote:n", n);
        foreach (var x in Candidates) Set("vote:" + x.id, 0);
        Set("vote:helped", 0);
        Begin("election", Tables.ElectionWeek, news: "KBN: Election week. Halcyon's Adrian Vell promises 'renewal', Councillor Henry Tsao promises more of the same, and reformer Rosa Ibarra promises to look in Lone Star's books. The count is in seven days.");
        OfferVoteJobs(n);
        Send("vote-open");
    }

    /// <summary>One job per campaign, from each side's people: rig it for Halcyon or the Lotus, or keep Ibarra alive to the count.</summary>
    private void OfferVoteJobs(int n)
    {
        var r = new Rng(Seed ^ 0xB0A7UL ^ (ulong)n * 0x51ED27UL);
        int threat = M.Clamp(MaxThreat, 2, 5);
        int days = Tables.ElectionWeek - 1;
        var halcyon = new (JobType type, SiteStyle style, Faction fac, string district, string title, string brief, string target)[]
        {
            (JobType.Datasteal, SiteStyle.Office, Faction.Lotus, "strip", "Vote: Tsao's tallies", "Juno, for a Halcyon Johnson: 'Councillor Tsao's people keep the precinct tallies in a Lotus office before they reach the count. Halcyon would like a copy -- and would like a few numbers changed on the way out. For Adrian Vell.'", null),
            (JobType.Plant, SiteStyle.Club, Faction.Lotus, "strip", "Vote: dirt on Tsao", "Juno, for a Halcyon Johnson: 'Henry Tsao takes his meetings in a Lotus club. Halcyon wants evidence found there -- evidence you will bring with you. KBN will be tipped by morning. For Adrian Vell.'", null),
            (JobType.Kidnap, SiteStyle.Club, Faction.Lotus, "strip", "Vote: Tsao's fixer", "Juno, for a Halcyon Johnson: 'The man who buys Tsao's votes is drinking in a Lotus club. Halcyon would like a long talk with him, somewhere quiet, until the count is over. For Adrian Vell.'", "Kenji Lau"),
        };
        var lotus = new (JobType type, SiteStyle style, Faction fac, string district, string title, string brief, string target)[]
        {
            (JobType.Plant, SiteStyle.Office, Faction.Halcyon, "spire", "Vote: Vell's skeleton", "Juno, for Auntie Mei: 'Adrian Vell's campaign office is a Halcyon floor in the Spire. The Lotus would like a few interesting files found on his desk. Councillor Tsao thanks you in advance.'", null),
            (JobType.Eliminate, SiteStyle.Office, Faction.Halcyon, "spire", "Vote: Vell's fixer", "Juno, for Auntie Mei: 'Corwin Hale runs Adrian Vell's money. Without him, Halcyon's renewal has nobody to pay the canvassers. The Lotus would like him to stop working. Permanently.'", "Corwin Hale"),
            (JobType.Datasteal, SiteStyle.Office, Faction.Halcyon, "industrial", "Vote: the count machines", "Juno, for Auntie Mei: 'Halcyon's polling contractor services the count machines out of an Industrial office. A little code in the right server and Councillor Tsao wins by a comfortable margin.'", null),
        };
        var reform = new (JobType type, SiteStyle style, Faction fac, string district, string title, string brief, string target)[]
        {
            (JobType.Escort, SiteStyle.Den, Faction.Vipers, "barrens", "Vote: walk Ibarra in", "Mara Quill: 'Rosa Ibarra wants to speak in the Barrens, in a Viper hall, because nobody else will. Half the city wants her quiet before the count. Walk her in and out alive. She can't pay much. I'm chipping in.'", "Rosa Ibarra"),
            (JobType.Defend, SiteStyle.Warehouse, Faction.Lotus, "docks", "Vote: hold Ibarra's office", "Mara Quill: 'Ibarra's campaign runs out of a warehouse on the Docks. Sgt. Dace says Lotus muscle are coming tonight to burn the ballots she's collected. Dace can't be seen there. You can.'", "Rosa Ibarra"),
        };
        void Offer((JobType type, SiteStyle style, Faction fac, string district, string title, string brief, string target) o, string cand, string employer) =>
            OfferSpecial(o.type, o.style, o.fac, o.district, threat, o.title, o.brief, employer: employer, payMul: Tables.VotePay, tag: $"vote:{cand}:{n}", target: o.target, item: o.type == JobType.Plant ? "bundle of planted evidence" : null, expires: days);
        Offer(r.Pick(halcyon), "halcyon", "fixer");
        Offer(r.Pick(lotus), "lotus", "fixer");
        Offer(r.Pick(reform), "reform", "press");
    }

    /// <summary>An election job finished: a vote for its candidate.</summary>
    private void Ballot(Job j, RunResult r, Debrief d)
    {
        if (j.Tag?.StartsWith("vote:") != true || !r.Success) return;
        string[] p = j.Tag.Split(':');
        if (p.Length < 3 || !int.TryParse(p[2], out int n) || n != ElectionCycle || Flag("vote:held") >= n) return;
        string cand = p[1];
        Inc("vote:" + cand);
        if (cand == "reform") Set("vote:helped");
        d.Lines.Add(($"{Candidate(cand).name}'s campaign", $"+1 (now {VoteWeight(cand)}); the council votes on day {VoteDay(n - 1)}"));
    }

    /// <summary>
    /// Who wins: the heaviest campaign. Ties and empty weeks go by a seeded
    /// baseline that favours the Lotus' money (then Halcyon's, then nobody's);
    /// a single job outweighs it.
    /// </summary>
    public string ElectionWinner()
    {
        var r = new Rng(Seed ^ 0xBA110UL ^ (ulong)ElectionCycle * 0x2545F491UL);
        float[] baseline = { 0.4f, 0.6f, 0.2f };
        string best = null;
        float top = -1;
        for (int k = 0; k < Candidates.Length; k++)
        {
            float s = VoteWeight(Candidates[k].id) + baseline[k] + r.Float() * 0.3f;
            if (s > top) { top = s; best = Candidates[k].id; }
        }
        return best;
    }

    /// <summary>The count: the winner's season starts today.</summary>
    public void CountVotes()
    {
        string win = ElectionWinner();
        var who = Candidate(win);
        Set("vote:held", ElectionCycle);
        Set("vote:won", Array.FindIndex(Candidates, x => x.id == win) + 1);
        int days = Tables.SeasonDays;
        switch (win)
        {
            case "halcyon":
                Say("KBN: Adrian Vell takes the council seat. 'Renewal starts tonight,' says Halcyon-Matsuda. Lone Star announces new patrols.");
                Season("renewal", days);
                Tick("lonestar", Tables.RenewalPolice);
                var r = new Rng(Seed ^ 0x4E7EUL ^ (ulong)ElectionCycle);
                var pick = Tables.Districts.Where(x => Holder(x.Id) != Faction.Halcyon).Select(x => x.Id).ToList();
                if (pick.Count < 2) pick = Tables.Districts.Select(x => x.Id).ToList();
                for (int k = 0; k < 2 && pick.Count > 0; k++)
                {
                    string dd = r.Pick(pick);
                    pick.Remove(dd);
                    float[] t = TurfOf(dd);
                    Faction before = Holder(dd);
                    t[(int)Faction.Halcyon] += Tables.RenewalTurf;
                    Normalise(t);
                    Changed(dd, before);
                }
                break;
            case "lotus":
                Say("KBN: Councillor Henry Tsao is re-elected by a 'comfortable margin'. Observers note that the count machines were serviced last week.");
                Season("council", days);
                break;
            default:
                Say("KBN: Upset at the count -- reformer Rosa Ibarra takes the seat. Her first motion: an inquiry into Lone Star's brass. Landlords 'deeply concerned' about the rent cap.");
                Season("reform", days);
                Tick("lonestar", -Tables.ReformPolice);
                if (Flag("vote:helped") > 0 && Notoriety > 0)
                {
                    Infamy(-1);
                    Say("The street remembers who walked Ibarra to the count: notoriety -1.");
                }
                break;
        }
        Send("vote-result");
    }

    /// <summary>
    /// What the council does to a new job: Vell's renewal pays 10 % more against Halcyon; under
    /// Ibarra nobody runs a skeleton crew and every site doubles its patrols (Shift change, its usual 10 %).
    /// (Tsao's council is the Lotus' "council" happening, priced where it always was.)
    /// </summary>
    private void ElectionMods(Job j)
    {
        if (j.Faction == Faction.Halcyon && On("renewal")) j.Pay = (int)(MathF.Round(j.Pay * Tables.RenewalPay / 50) * 50);
        if (!On("reform")) return;
        if (j.Mods.Remove(RunMod.Skeleton)) j.Pay = (int)(MathF.Round(j.Pay / Tables.RunMods.First(x => x.mod == RunMod.Skeleton).pay / 50) * 50);
        if (!j.Has(RunMod.ShiftChange))
        {
            j.Mods.Add(RunMod.ShiftChange);
            j.Pay = (int)(MathF.Round(j.Pay * Tables.RunMods.First(x => x.mod == RunMod.ShiftChange).pay / 50) * 50);
        }
    }

    /// <summary>Ibarra's rent cap: 15 % off the week, to the nearest ¥10.</summary>
    private int ReformRent(int rent) => On("reform") ? (int)MathF.Round(rent * Tables.ReformRent / 10) * 10 : rent;

    /// <summary>A season that may already be on (the Lotus council): it runs until the end of the new one.</summary>
    private void Season(string id, int days)
    {
        Happening h = Happenings.FirstOrDefault(x => x.Id == id && x.Start <= Day && Day <= x.End && x.District == null);
        if (h != null) { h.End = Math.Max(h.End, Day + days - 1); return; }
        Happenings.Add(new Happening { Id = id, Start = Day, End = Day + days - 1 });
        Explain("happening", "Something is going on in the city: it changes the rules for a few days. The terminal and the PDA's City tab say what.");
    }
}
