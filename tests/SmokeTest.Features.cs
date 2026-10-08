using Godot;

namespace Sprawl;

public partial class SmokeTest
{
    /// <summary>Walking away: only with a story to end on (a campaign won, or enough saved), never twice.</summary>
    private void Retiring()
    {
        Career c = TestCareer();
        Check(c.RetireReason() == null, "a new runner has nothing to retire on");
        c.Cash = Tables.RetireSavings;
        Check(c.RetireReason()?.Contains("put away") == true, $"enough saved is a reason to stop ({c.RetireReason()})");
        Check(!c.RetiresALegend, "savings alone retire you quietly, not a legend");
        c.Cash = 100;
        c.Set("tide:won", 30);
        Check(c.RetireReason()?.Contains("Toxic Tide") == true && c.RetiresALegend, "a finished campaign is a story to end on, and a legend's");
        c.Retired = true;
        Check(c.RetireReason() == null, "a retired runner can't retire again");
    }
}
