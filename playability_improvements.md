# Playability Improvements — Worklog

_Started: 2026-07-10_

Loop driven by `/improve`. Bar: "is this game playable and getting better?"
Verification: `dotnet build Hex.csproj` + headless boot of `scenes/game.tscn` +
the autoplay bot (`dev/autoplay.tscn`, added in Round 1) which plays full runs
bot-style and exits non-zero on crash/softlock/inconsistency.

Godot binary used: `C:\Users\AlexT\Downloads\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe`

## Round 1

- [x] **Headless autoplay harness** — `dev/autoplay.tscn`, `scripts/Dev/AutoPlayDriver.cs`, `scripts/Board/HexBoard.Debug.cs`.
      Observed problem: the game is tap-driven; nothing can exercise a full run headlessly, so
      crashes/softlocks in battle resolution are invisible until manual play. Done: `#if DEBUG`
      tap/inspection hooks on HexBoard + a bot Node that plays N complete runs (battles, shop
      purchases, deploys) synchronously through the real OnTileTapped input path. Danger-aware
      move scoring (safe captures > captures > safe approach). Non-zero exit on failure.
      Usage: `godot --headless --path . res://dev/autoplay.tscn -- runs=100`.
- [x] **Fix no-legal-action softlock** — `HexBoard.AfterPlayerAction` now auto-passes
      ("NO MOVES — TURN PASSES") while the player has no legal move and no possible deploy,
      with a 64-pass paralysis guard ending in STALEMATE loss. `PlayerHasAnyAction()` added.
- [x] **Fix mutual-wipe zombie state** (found by harness) — when one ring collapse killed the
      last enemy AND the last player piece, the win branch ran first, rebuilding an EMPTY army;
      the next battle then started with no player pieces and hung forever. `CheckBattleEnd`
      now checks the loss condition first; win/loss refactored into `WinBattle`/`LoseBattle`.
- [x] **Fix unresolvable endgames** (found by harness) — bishop-vs-bishop on the collapsed
      radius-1 board is mutually uncapturable (bishops never attack adjacent hexes) → battles
      that could never end. Two mechanics added: (a) stranded-pawn promotion — a pawn whose
      every forward hex left the board promotes on the spot, both sides, picking a kind that
      can actually move from that tile; (b) STANDOFF adjudication — once the crumble is spent
      and 16 actions pass with no piece death, the battle resolves by remaining force
      (reserve counts, player wins ties).
- [x] **Verified** — `dotnet build` clean; 100 bot runs: 0 failures, exit 0 (1 bot victory,
      99 defeats); main scene headless boot clean.

**Balance note for a later round:** defeat histogram over 99 bot defeats peaks hard at
battle 4 (first boss, Lockmaker): 32/99. The bot is weaker than a human, but watch the
first-boss difficulty cliff.

## Round 2

- [x] **Announce boss modifiers** — added `BossCatalog` (name + effect copy) in
      `BattlePlanner.cs`; battle-start StatusNote ("LOCKMAKER: 3 TILES FROZEN FOR 2 TURNS"),
      HUD battle label names the boss ("BATTLE 4 — LOCKMAKER"), shop heading warns
      ("THE EXCHEQUER — LOCKMAKER AWAITS") with a danger-red effect line under it.
- [x] **Crumble chip terminal state** — `Hud.SetCrumble` shows muted "CRUMBLED" when
      turnsLeft==0 and nothing is cracking (was danger-red "CRUMBLE IN 0" forever).
- [x] **Show the shop's tile-upgrade target on the board** — `HexBoard.SetShopPreviewTile`
      gold-lights the claimed hex behind the shop (cleared on continue and battle reset);
      card text now says "Claims the tile lit gold on the board" instead of raw coords.
- [x] **List owned gambits on Pause** — `PauseOverlay.Refresh(RunState)` lists owned gambit
      names, hidden when none.
- [x] **Verified** — build clean; 60 bot runs 0 failures (2 victories); headless boot clean.
      Note: the bot bypasses the UI screens, so Round 3 should add a UI-flow smoke test that
      drives the real screens (Title → NewRun → battle → Shop → Pause → GameOver).

## Round 3

- [x] **UI-flow smoke test** — `dev/uiflow.tscn`, `scripts/Dev/UiFlowDriver.cs`,
      `scripts/Dev/BotBrain.cs` (bot logic extracted from AutoPlayDriver; shared by both
      harnesses). Drives the real `game.tscn` by pressing actual buttons: PLAY → REROLL →
      BEGIN RUN → tutorial skip → pause/resume → battle (bot) → Shop buy + NEXT BATTLE →
      pause → ABANDON → Title, then a deliberately-lost run (suicidal bot mode) → Game Over →
      NEW RUN → back → Title. PASS, exit 0. Run wrapper must back up/restore
      `%APPDATA%\Godot\app_userdata\Hex\hex.cfg` (the test touches the real save).
- [x] **Fix Lockmaker battle-start softlock** (found by the 60-run sweep) — a lone corner
      bishop's only two on-board diagonals could both be locked; battle started with zero
      player actions and the lock timer (which ticks on player actions) never expired.
      `ApplyLockmaker` now re-rolls the lock set until the player has an action (8 tries,
      then locks nothing). Verified: 150 autoplay runs, 0 failures.
- [x] **Fix stale project description** — `project.godot` now describes the chess-battle
      roguelike instead of the old token hunt.
