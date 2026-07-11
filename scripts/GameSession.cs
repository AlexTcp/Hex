// =============================================================================
// GameSession
// =============================================================================
// Purpose:
//   Autoload Node that is the single source of truth for cross-screen game
//   state. Owns the live RunState (money, army, reserve, gambits, tile
//   upgrades, battle number) plus the persisted meta-progression (best battle,
//   high score, tutorial-seen flag). Meta state is loaded from user://hex.cfg
//   on _Ready and written back on the events that matter (a finished run, the
//   tutorial being completed, and the app being backgrounded) so a force-kill
//   on mobile never loses progress.
//
// Interactions:
//   - ScreenManager: calls StartNewRun when a run begins and CommitRun when it
//     ends (defeat or victory); reads BestBattle/HighScore for the Title.
//   - HexBoard / ShopScreen / Hud: mutate + read CurrentRun directly.
// =============================================================================

using Godot;
using HexGame.Chess;

namespace HexGame;

public partial class GameSession : Node
{
    public const string SavePath = "user://hex.cfg";   // UiFlowDriver backs this file up
    private const string Section = "progress";

    private readonly System.Random _rng = new();

    // ----- Live run state (transient) -------------------------------------
    public RunState CurrentRun { get; private set; }

    // ----- Persisted meta-progression --------------------------------------
    public int BestBattle { get; private set; } = 0;
    public int HighScore { get; private set; } = 0;
    public int Crowns { get; private set; } = 0;      // runs won (battle 12 cleared)
    public bool TutorialSeen { get; private set; } = false;

    public override void _Ready()
    {
        Load();
    }

    // Persist when the OS backgrounds the app or asks it to close, so an
    // Android task-swipe / force-kill keeps the player's records.
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest ||
            what == NotificationWMGoBackRequest ||
            what == NotificationApplicationPaused)
        {
            Save();
        }
    }

    // Roll a fresh run (3 random starter pieces, starting purse).
    public RunState StartNewRun()
    {
        CurrentRun = RunState.NewRun(_rng);
        return CurrentRun;
    }

    // Re-roll the pending starter army (used by the New Run screen before the
    // first battle begins).
    public RunState RerollRun() => StartNewRun();

    // Fold the just-ended run into the persistent records and write to disk.
    // Returns true if a new global record was set so the end screen can show a
    // "NEW BEST!" flourish. `battlesCleared` is the number of battles won.
    public bool CommitRun(bool wonRun = false)
    {
        if (CurrentRun == null) return false;
        int battlesCleared = CurrentRun.Battle - 1;
        bool newGlobalRecord = false;
        if (battlesCleared > BestBattle) { BestBattle = battlesCleared; newGlobalRecord = true; }
        if (CurrentRun.Score > HighScore) { HighScore = CurrentRun.Score; newGlobalRecord = true; }
        if (wonRun) Crowns++;
        Save();
        return newGlobalRecord;
    }

    public void MarkTutorialSeen()
    {
        if (TutorialSeen) return;
        TutorialSeen = true;
        Save();
    }

    private void Load()
    {
        var cfg = new ConfigFile();
        // First run (no file) leaves every field at its default — never throws.
        if (cfg.Load(SavePath) != Error.Ok) return;

        BestBattle = (int)cfg.GetValue(Section, "best_battle", 0);
        HighScore = (int)cfg.GetValue(Section, "high_score", 0);
        Crowns = (int)cfg.GetValue(Section, "crowns", 0);
        TutorialSeen = (bool)cfg.GetValue(Section, "tutorial_seen", false);
    }

    private void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "best_battle", BestBattle);
        cfg.SetValue(Section, "high_score", HighScore);
        cfg.SetValue(Section, "crowns", Crowns);
        cfg.SetValue(Section, "tutorial_seen", TutorialSeen);
        cfg.Save(SavePath);
    }
}
