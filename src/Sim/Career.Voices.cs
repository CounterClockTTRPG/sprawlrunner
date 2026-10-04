using System.Collections.Generic;

namespace Sprawl;

/// <summary>
/// Who is asking: each employer opens a brief their own way, and a Johnson on
/// the line adds their manner. Picked by the job's seed, so a board reads the
/// same every time it is drawn.
/// </summary>
public sealed partial class Career
{
    public static readonly Dictionary<string, string[]> EmployerVoices = new()
    {
        ["fixer"] = new[]
        {
            "Juno: 'Clean, quick, quiet. Pick two.'",
            "Juno, not looking up from her drink: 'This one pays. Don't make me regret it.'",
            "Juno: 'I vouched for you. Try to make that true.'",
        },
        ["arms"] = new[]
        {
            "Sal, over the sound of a bench grinder: 'YOU! GOT A JOB FOR YOU! BRING MY CRATES BACK IN ONE PIECE!'",
            "Sal: 'Friend of a friend. Nice people. I said you'd do it cheap. I lied, don't worry.'",
            "Sal, shouting from the back: 'TAKE THE BIG GUN. TRUST ME. TAKE THE BIG GUN.'",
        },
        ["decker"] = new[]
        {
            "null: 'job. details below. don't call me.'",
            "null: 'someone is paying for this. i didn't ask why. neither should you.'",
            "null: 'the cameras on this site are older than you. that's the good news.'",
        },
        ["rigger"] = new[]
        {
            "Torque, under a car: 'Hand me the -- no, the other one. Right. Got a job.'",
            "Torque: 'Easy money, if you drive like I taught you. Which you don't.'",
            "Torque: 'Bring the car back with all the doors on it this time.'",
        },
        ["doc"] = new[]
        {
            "Ichikawa, peeling off his gloves: 'A patient's family asked me who to call. I gave them your name. Don't make me stitch you after.'",
            "Ichikawa: 'I don't like this kind of work. I like the people who need it done.'",
            "Ichikawa: 'Come back breathing. That is my whole fee.'",
        },
        ["talismonger"] = new[]
        {
            "Sabine, shuffling cards: 'The Tower came up three times this morning, darling. That means you.'",
            "Sabine: 'A client with old money and older enemies. Be charming, if you can.'",
            "Sabine: 'The spirits are restless about this one. So am I. Go anyway.'",
        },
        ["fence"] = new[]
        {
            "Wen: 'Somebody owes somebody. You're the somebody who collects.'",
            "Wen, counting: 'Simple job. Simple pay. I don't do complicated any more.'",
            "Wen: 'I knew the man who asked for this when he was a boy. He was a thief then, too.'",
        },
        ["cop"] = new[]
        {
            "Dace, very quietly: 'Off the books. If you're caught, I've never seen you.'",
            "Dace: 'The Star can't touch these people. You can.'",
            "Dace: 'My captain would hang me for this. Make it worth it.'",
        },
        ["press"] = new[]
        {
            "Quill: 'Get me the story and I'll get you the pay. Get me pictures and I'll love you.'",
            "Quill: 'Somebody's lying to the whole city. I'd like to know who.'",
            "Quill: 'My editor says no. My source says yes. I'm asking you.'",
        },
    };

    public static readonly Dictionary<string, string> JohnsonVoices = new()
    {
        ["grey"] = "Mr. Grey reads the terms off a slate in a voice like a dial tone. He will not repeat them.",
        ["vale"] = "Dr. Vale apologises twice before he gets to the point, and once after.",
        ["mei"] = "Auntie Mei pours you tea before she tells you what she wants, and watches whether you drink it.",
        ["tusk"] = "Mr. Tusk laughs at his own jokes and slaps the table: 'Make some noise! I like reading about my money.'",
        ["silk"] = "Ms. Silk smiles the whole time. She has very good teeth and no last name.",
    };

    /// <summary>The employer's line before the brief, and the Johnson's when there is one.</summary>
    private static void Voice(Job j)
    {
        string say = "";
        if (j.Employer != null && EmployerVoices.TryGetValue(j.Employer, out string[] lines))
            say = lines[(int)((j.Seed >> 7) % (ulong)lines.Length)];
        if (j.JohnsonId != null && JohnsonVoices.TryGetValue(j.JohnsonId, out string jl)) say = (say + " " + jl).Trim();
        if (say.Length > 0) j.Brief = say + " " + j.Brief;
        if (j.Client >= 0) j.Brief += $" (The money is the {Tables.FactionNames[j.Client]}'s.)";
    }
}
