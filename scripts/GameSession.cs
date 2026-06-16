// =============================================================================
// GameSession
// =============================================================================
// Purpose:
//   Autoload Node that is the single source of truth for cross-screen game
//   state. Holds the live run (selected piece, score, wave) plus the
//   persisted meta-progression (best wave, high score, per-token best waves,
//   tutorial-seen flag). Meta state is loaded from user://hex.cfg on _Ready
//   and written back on the events that matter (a finished run, the tutorial
//   being completed, and the app being backgrounded) so a force-kill on
//   mobile never loses progress.
//
// Interactions:
//   - ScreenManager / GameScreen: read live + meta state to drive the HUD,
//     Title, Character-Select and Game-Over screens; call ResetRun on run
//     start, CommitRun on PlayerCaught, and MarkTutorialSeen on onboarding
//     completion.
//   - HexBoard: emits ScoreChanged / WaveChanged / PlayerCaught; the
//     controller mirrors those into Score / Wave here. HexBoard does not
//     reference GameSession directly (kept decoupled).
// =============================================================================

using Godot;

namespace HexGame;

public partial class GameSession : Node
{
    private const string SavePath = "user://hex.cfg";
    private const string Section = "progress";

    // Mirrors TokenCatalog.All.Length; kept as a local const so this autoload
    // has no compile dependency on the Tokens namespace.
    public const int TokenCount = 14;

    // ----- Live run state (transient; reset every run) -------------------
    public int SelectedTokenIndex { get; set; } = 0;
    public int Score { get; set; } = 0;
    public int Wave { get; set; } = 1;

    // ----- Persisted meta-progression ------------------------------------
    public int BestWave { get; private set; } = 0;
    public int HighScore { get; private set; } = 0;
    public bool TutorialSeen { get; private set; } = false;
    public readonly int[] PerTokenBestWave = new int[TokenCount];

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

    // Zero the live run for a fresh attempt with the given piece.
    public void ResetRun(int tokenIndex)
    {
        SelectedTokenIndex = tokenIndex;
        Score = 0;
        Wave = 1;
    }

    // Fold the just-ended run into the persistent records and write to disk.
    // Returns true if a new global record (best wave or high score) was set,
    // so the Game-Over screen can show a "NEW BEST!" flourish.
    public bool CommitRun()
    {
        bool newGlobalRecord = false;
        if (Wave > BestWave) { BestWave = Wave; newGlobalRecord = true; }
        if (Score > HighScore) { HighScore = Score; newGlobalRecord = true; }

        int idx = SelectedTokenIndex;
        if (idx >= 0 && idx < TokenCount && Wave > PerTokenBestWave[idx])
            PerTokenBestWave[idx] = Wave;

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

        BestWave = (int)cfg.GetValue(Section, "best_wave", 0);
        HighScore = (int)cfg.GetValue(Section, "high_score", 0);
        TutorialSeen = (bool)cfg.GetValue(Section, "tutorial_seen", false);
        for (int i = 0; i < TokenCount; i++)
            PerTokenBestWave[i] = (int)cfg.GetValue(Section, $"per_token_best_{i}", 0);
    }

    private void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "best_wave", BestWave);
        cfg.SetValue(Section, "high_score", HighScore);
        cfg.SetValue(Section, "tutorial_seen", TutorialSeen);
        for (int i = 0; i < TokenCount; i++)
            cfg.SetValue(Section, $"per_token_best_{i}", PerTokenBestWave[i]);
        cfg.Save(SavePath);
    }
}
