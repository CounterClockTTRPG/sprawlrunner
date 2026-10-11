using System;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>Loose ends from docs/NEXT3.md section 1: things promised and never done.</summary>
public partial class SmokeTest
{
    private void LooseEnds()
    {
        // The second-skin suit's soles: a guard just behind you hears your boots, not the suit's.
        string[] hall =
        {
            "##############",
            "#......P.....#",
            "##############",
        };
        foreach (bool suit in new[] { false, true })
        {
            Career c = TestCareer();
            if (suit) c.ArmorId = "sneaksuit";
            Run r = Make(Site(hall), c: c);
            Actor g = r.AddEnemy(EnemyKind.Guard, r.Player.Pos + new V2(1.6f, 0), 0);
            Steps(r, 0.05f, new RunInput { Slot = -1, Move = new V2(-1, 0), Aim = r.Player.Pos + new V2(-1, 0) });
            if (suit) Eq(g.State, AiState.Idle, "in the suit, footsteps behind a guard go unheard");
            else Check(g.State == AiState.Suspicious, "in ordinary clothes, the guard turns round at your footsteps");
        }

        // The collector named Sorensen-Aoki: a few days on, their card, and a job.
        Career cc = TestCareer();
        cc.CityRef = City.Generate(5);
        cc.RefreshBoard(cc.CityRef);
        Storylet lead = global::Sprawl.Storylets.ById("collector-lead");
        cc.Set("lead:collector", cc.Day);
        Check(!lead.When(cc), "not the morning after");
        cc.Day += 3;
        Check(lead.When(cc), "three days on, Sorensen-Aoki leave a card");
        int board = cc.Board.Count;
        Check(cc.Choose(cc.Send(lead), 0) == null && cc.Board.Count == board + 1 && cc.Board.Last().Type == JobType.Retrieve, "calling the number puts their job on the board");
        Check(!lead.When(cc), "and the card comes once");

        // A Johnson who sold the exit without a trap: Juno knows who, and you can cut them off.
        Career jc = TestCareer();
        Storylet sold = global::Sprawl.Storylets.ById("burned-johnson");
        jc.Last = new LastRun { Job = new Job { JohnsonId = "tusk" } };
        jc.Set("burned:johnson", Math.Max(1, jc.Day));
        Check(sold.When(jc), "a sold exit gets Juno's word on it");
        jc.Set("crossed", Math.Max(1, jc.Day));
        Check(!sold.When(jc), "unless the double-cross call already covers it");
        jc.Set("crossed", 0);
        Mail sm = jc.Send(sold);
        Check(sm.Text.Contains(Career.JohnsonById("tusk").Name), "Juno names the Johnson");
        Check(jc.Choose(sm, 0) == null && jc.JohnsonBurned("tusk"), "cut off: that Johnson never calls again");

        // Back from the dead: the morning after a TraumaLink pick-up, once per death.
        Career dc = TestCareer();
        Storylet flat = global::Sprawl.Storylets.ById("flatlined");
        dc.Last = new LastRun { Job = TestJob(), Died = true };
        dc.Deaths = 1;
        dc.Injuries = 1;
        dc.Cash = 5000;
        Check(flat.When(dc), "Ichikawa calls after a death you came back from");
        Check(dc.Choose(dc.Send(flat), 0) == null && dc.Injuries == 0 && dc.Cash == 4000, "and mends an injury for ¥1,000");
        Check(!flat.When(dc), "once per death");
        dc.Deaths = 2;
        Check(flat.When(dc), "and again after the next one");

        // Back out of a menu: Escape, the pause key, or B -- but a letter bound to pause doesn't close a text field.
        Check(Controls.Back(new InputEventKey { Pressed = true, Keycode = Key.Escape }), "Escape backs out");
        Check(Controls.Back(new InputEventJoypadButton { Pressed = true, ButtonIndex = JoyButton.B }) == !AgentRunner.Active, "B on a pad backs out");
        Check(!Controls.Back(new InputEventKey { Pressed = true, Keycode = Key.E }), "other keys don't");
        Check(!Controls.Back(new InputEventKey { Pressed = true, Keycode = Key.Escape, Echo = true }), "a held Escape backs out once");
    }
}
