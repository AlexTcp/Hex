# Playability Improvements — Worklog

_Started: 2026-07-10_
_Vision (derived — Hex is INACTIVE/no-vision in Godot.md): (1) Premium-slate tactile presentation — carved ivory/oxblood figurines, cinematic board, call-and-response readability on low-spec mobile; (2) Fair, readable hex-chess tactics — death-tile telegraphing, inspection, three termination guarantees, no unresolvable states; (3) Roguelike run arc — shop economy, gambits, tile upgrades, bosses & themed rosters, permadeath army upkeep. Rung-4 biased to polish/feel._
_Logs scanned through: 2026-07-20 (Linux session start; user:// = ~/.local/share/godot/app_userdata/Hex)_

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

(Untracked `scripts/DebugLog.cs.bak` matches no tracked revision — left in place, just kept
out of commits.)

## Round 4

- [x] **Instrument the difficulty curve** — AutoPlayDriver now prints per-battle
      reached/cleared/clear%/avg-army after every sweep.
- [x] **Balance verdict: NO game change needed (data)** — the apparent battle-4 cliff was a
      bot artifact. With piece-first shopping (mirroring the real 2-offer + $2-reroll shop)
      and eager reserve deploys, 300 runs → 27% win rate with a healthy curve:
      b1–3 ≈100%, boss b4 94%, mid 97→82%, finale 68%. Old bot (buys ≤1 piece, hoards
      reserve) starved to 1% wins with armies shrinking 3.5→2. The economy is fine; army
      upkeep is the core strategy.
- [x] **Teach army permadeath in the tutorial** — the game never said losses are permanent
      across battles (the mechanic the curve hinges on). Tutorial step 2 now reads
      "pieces you lose are gone for good — restock your army at the shop".
- [x] **Verified** — build clean; 300 instrumented runs 0 failures, exit 0.

## Round 5

- [x] **Sequence the enemy's answer visually** — enemy move tween now starts 0.22s after the
      player's; a captured player piece shrinks (and sparks) at 0.32s as the attacker lands.
      Logic stays fully synchronous — only TweenInterval prefixes on the visuals
      (`MovePieceTo`/`KillPiece`/`PlayCaptureBurst` gained delay params).
- [x] **Deploy-mode hint** — arming deploy shows "TAP A LIT TILE TO DEPLOY"; a full board
      shows "NO ROOM TO DEPLOY" instead of silently disarming.
- [x] **Verified** — build clean; 100 autoplay runs 0 failures (30 victories — curve steady);
      UI-flow PASS.

## Round 6

- [x] **Floating +$N money pops** — pooled Label3D rising/fading at the earning hex, for
      captures (full computed payout incl. Gold tile / gambit / boss bonuses), Blessed
      deploys, Pawn Ambition promotions, and the battle-clear payout at board centre.
- [x] **"NO LEGAL MOVES" note** — selecting a blocked piece now says why no tiles lit.
- [x] **Verified** — build clean; 100 autoplay runs 0 failures (31 wins); boot clean.

## Round 7

- [x] **Add sound** — the game was fully silent (missing-but-expected). Synthesized a quiet
      tournament-room SFX set offline (`audio/*.wav`, 1–33 KB each; recipe committed as
      `dev/gen_sfx.py`): select tick, felt move thud, capture thud+ring, coin ting, wooden
      crack, collapse rumble, win chime, lose sting. New `Sfx` autoload (round-robin
      6-player pool, safe no-op headless/missing). Wired: select / move (rides the possibly
      delayed move tween) / capture (rides the strike delay) / coin on money pops / crack /
      collapse / win / lose.
- [x] **Verified** — build clean; assets imported (`--headless --import`); 100 autoplay runs
      0 failures; UI-flow PASS; boot clean. (Pre-existing exit-time "resources still in use"
      warning noted — static shared resources by design, exit-only, not a play issue.)

## Round 8

- [x] **UI click sounds** — `UiTheme.MakeButton` wires every factory button; the two
      hand-rolled Hud buttons (pause, reserve bar) wired individually.
- [x] **Sound toggle** — `Sfx.Enabled` persisted to `user://settings.cfg` (separate file so
      GameSession's whole-file save can't clobber it); "Sound: On/Off" button in the
      settings drawer with audible-on-enable confirmation.
- [x] **Teach reserves & bosses** — 4th tutorial step covers deploy costs-your-action and
      the every-4th-battle boss rule.
- [x] **Verified** — build clean; 60 autoplay runs 0 failures (17 wins); UI-flow PASS.

## Round 9

- [x] **Two new gambits** — Quartermaster (battle-clear pays +3, resolved in WinBattle) and
      Stonemason (cracked tiles hold one extra action, resolved in the crack-grace reset).
      Both auto-appear in the shop via GambitCatalog.All.
- [x] **Finale: the enemy crown takes the field** — battle 12 always fields a Queen (paid
      from the normal budget), announced at battle start. Rejected a "two enemies act" boss:
      it would break the death-tile fairness promise.
- [x] **Measured** — 300 runs, 0 failures: win rate 25% (was 27%), battle-12 clear 65%
      (was 68%) — slightly harder, more thematic finale; curve stays smooth. UI-flow PASS.

## Round 10

- [x] **Ambient pad loop** — `audio/ambient.wav` (12s Cm(add9) slate-room pad; every partial
      and LFO completes integer cycles per loop so the wrap is sample-exact; recipe in
      `dev/gen_sfx.py`). `Sfx` plays it on a dedicated player at −16 dB with
      AudioStreamWav Forward loop; the sound toggle stops/starts it.
- [x] **Shop shows army composition** — the money line now lists piece monograms
      ("army Pa Pa Kn Ro · reserve Bi") instead of bare counts.
- [x] **Verified** — build clean; import clean; 60 autoplay runs 0 failures (21 wins);
      UI-flow PASS.

## Round 11

- [x] **Inspect enemy reach** — tapping an enemy piece paints its legal moves cold steel
      blue (read-only; next tap clears; a capture offer on that tile still takes priority).
      Uses the AI scratch buffer so the selection memoization stays intact.
- [x] **Run stats on Game Over** — CapturesMade / PiecesLost (mercy saves excluded) /
      MoneyEarned tallied on RunState and shown as three new result rows.
- [x] **Verified** — build clean; 80 autoplay runs 0 failures (21 wins); UI-flow PASS.

## Round 12

- [x] **Persist victory count ("Crowns")** — `CommitRun(wonRun)` increments a persisted
      `crowns` counter; the title shows a CROWNS chip once the first run is won.
- [x] **Refresh CLAUDE.md** — documented the three termination guarantees (auto-pass,
      stranded promotion, standoff), loss-before-win ordering, finale Queen, enemy-reach
      inspection, staggered enemy visuals, money pops, Sfx autoload + settings.cfg split,
      harness commands with reference difficulty curve, audio regeneration, new layout rows.
- [x] **Verified** — build clean; 60 autoplay runs 0 failures (19 wins); UI-flow PASS.

## Round 13

- [x] **HUD inspection chip** — new `InspectChanged(string)` board signal drives a
      bottom-left autowrapped chip: selecting a piece shows "ROOK — Slides any distance…";
      tapping an enemy shows "ENEMY KNIGHT — Leaps…"; a bare tap on an upgraded tile
      explains its marker ("SNARE TILE — An enemy landing here…"). Hides on deselect.
- [x] **Verified** — build clean; 60 autoplay runs 0 failures (17 wins); UI-flow PASS.

## Round 14

- [x] **Threat audio bed** — seamless 4s heartbeat-pulse loop (`audio/threat.wav`) on a
      dedicated looping player at −13 dB; `Sfx.SetThreatBed` driven by ThreatChanged;
      obeys the sound toggle (resumes if threat still active when re-enabled).
- [x] **Danger tiles pulse at double rate** — the death-tile red now runs on its own looped
      tween at 2× the gold/copper breathing rate: the warning is a rhythm cue, not only a
      colour (colour-blind safe).
- [x] **Boss entrance sting** — low detuned-fifth hit (`audio/boss.wav`) at boss battle
      start alongside the announcement note.
- [x] **Verified** — build clean; import clean (only new WAVs reimported — generator is
      deterministic); 60 autoplay runs 0 failures; UI-flow PASS.

## Round 15

- [x] **Leak canary in the autoplay harness** — prints node/orphan/object counts every 50
      runs. First reading grew ~106 nodes/run, but that was the harness's own artifact:
      it played every run synchronously inside `_Ready`, so QueueFree never flushed. The
      driver now yields two frames between runs; with that fixed, counts are flat
      (~490–580 nodes over 200 runs, orphans always 0) — **no real leak** in battle
      setup/teardown, money pops, tweens, or audio players.
- [x] **Verified** — 200 runs, 0 failures; difficulty curve steady (finale 63–65%).

## Round 16

- [x] **Screenshot mode for the UI-flow driver** — `screenshots=<dir>` user arg captures
      viewport PNGs at 7 key screens during a windowed run
      (`godot --path . res://dev/uiflow.tscn -- screenshots=<dir>`, NOT headless).
      Reviewing the renders found and fixed three real issues:
- [x] **Inspection chip clipped off-screen** — fixed height + downward growth truncated
      long piece descriptions below the viewport. Now bottom-anchored with
      `GrowVertical=Begin` (grows upward); re-shot to confirm.
- [x] **Pieces too small to read** — the hexagonal rook was mistakable for a loose tile
      from the play camera. All six piece meshes scaled up ~25–30% (screenshot-checked).
- [x] **Shop preview tile hidden behind the offer cards** — the claimed hex projects to
      screen centre, exactly where the cards sit. The tile card now embeds a mini hex-map
      diagram (radius-2 schematic, claimed hex gold) so the location is always visible;
      board glow kept for after purchase.
- [x] **Verified** — build clean; windowed UI-flow PASS with 7 shots reviewed; 80 autoplay
      runs 0 failures (25 wins); game-over stat rows render correctly.

## Round 17

- [x] **Fix stale highlight paint (pre-existing visual bug)** — screenshot diffing showed a
      deselected bishop's four gold tiles persisting into the next inspection.
      `ClearHighlights` refreshed each tile BEFORE removing it from `_highlighted`, and
      `RefreshTileVisual` early-returns for highlighted coords ("selection paint wins") —
      so no deselection ever repainted; stale gold accumulated until the next full board
      refresh (crumble/lock/battle-start), silently lying about legal moves. Now empties
      the set into the pooled scratch first, then repaints. Re-shot to confirm only live
      paint remains.
- [x] **Fix inspection chip for real** — the round-16 upward-grow approach still broke when
      autowrapped text changed while hidden (label overflowed both panel edges). Now a
      fixed-size bottom-anchored chip (sized for the longest description) with a
      vertically-centred label. Screenshot-confirmed with the longest text.
- [x] **Verified** — build clean; windowed UI-flow PASS, shots reviewed; 80 autoplay runs
      0 failures (24 wins).

## Round 18

- [x] **Deeper screenshot coverage** — the selection shot now picks the piece nearest an
      enemy (maximizes death-tile odds in frame), and a fast shot (4-frame wait) captures
      the first capture's floating +$N mid-rise. Money pop verified in-render: outlined
      gold "+$2" above the capture hex, HUD money/score/enemies all consistent.
- [x] **Verified** — build clean; windowed UI-flow PASS with 8 shots; no new issues found
      in render review.

## Round 19

- [x] **Victory celebration** — gold confetti (CpuParticles2D with a code-made square
      texture, preprocessed so it's mid-fall when the screen fades in) rains over the
      CROWN CLAIMED panel; stops when the screen hides. Screenshot-verified via the
      driver's new forced-victory presentation shot (09-victory).
- [x] **Survivors bonus** — the final win scores +200 per surviving army piece
      ("SURVIVORS +N"), applied before BattleWon so the end screen shows it.
- [x] **Verified** — build clean; windowed UI-flow PASS with 9 shots (victory confetti
      confirmed in-render); 80 autoplay runs 0 failures (24 wins).

## Round 20

- [x] **Stunned enemies get a visible state** — shared snare-purple alloy material while
      StunTurns > 0 (`RefreshPieceVisual`), applied on Snare landings and Knight Fork,
      reverted when the stun expires. The death-tile logic already excluded stunned
      enemies; now the player can see why a tile isn't red.
- [x] **Deploy sound** — deploys now thud like moves.
- [x] **Verified** — build clean; 80 autoplay runs 0 failures (22 wins); UI-flow PASS.

## Round 21

- [x] **Every battle announces itself** — non-boss battles now flourish "BATTLE N" at start
      (bosses keep their named announcement + sting). The shop's subtitle line now also
      shows "Battle N of 12 awaits." for normal battles (muted) instead of staying blank.
      (icon.svg checked — already custom and on-brand, no work needed.)
- [x] **Verified** — build clean; 60 autoplay runs 0 failures (14 wins); UI-flow PASS.

## Round 22

- [x] **Themed enemy battles** — ~25% of non-boss battles from battle 3 field PAWN HORDE
      (all pawns, cap 10), CAVALRY (all knights) or BISHOP COURT (all bishops), announced
      in the battle-start flourish ("BATTLE 5: CAVALRY"). Never on bosses or the finale.
- [x] **Measured** — 200 runs, 0 failures, 28% win rate; per-battle clear rates within
      variance of the reference curve (finale 60%). UI-flow PASS.

## Round 23

- [x] **CLAUDE.md sync for rounds 19–22** — survivors bonus, themed rosters, stun visual,
      inspection chip, screenshot-mode harness docs.

## Round 24

- [x] **Screenshot the remaining unseen board states** — the flow now plays through real
      shop continues (buying each visit) to battle 4, stalls battle 3 on safe keep-away
      moves (new BotBrain stall mode) until the ring cracks for the cracked-board shot,
      then captures Lockmaker's frozen tiles at battle 4's start. Both render correctly
      (rust smoulder ring + danger vignette; cold-blue locks).
- [x] **Fix invisible-HUD transition race (pre-existing, found by these shots)** — GoState
      never killed in-flight cross-fades: the shop's 0.4s HUD fade-out kept writing after a
      quick NEXT BATTLE's 0.22s fade-in finished, leaving the scrim dark and the HUD fully
      transparent for the whole battle for any fast-tapping player. GoState now kills
      transition tweens and hides ghosted third screens on every state change.
- [x] **Fix status flourish rendering at the screen's top-left (pre-existing)** — Flourish
      reset `Position = (0,0)`, overriding the centre anchors, so every note ("VICTORY",
      boss announcements…) drew from the top-left corner and long text clipped off-screen.
      The note is now a full-rect centred autowrapping label; boss announcement verified
      dead-centre in-render.
- [x] **Verified** — build clean; 80 autoplay runs 0 failures (31 wins); UI-flow PASS both
      windowed (shots reviewed) and headless.

## Round 25

- [x] **Show the collapse countdown** — while a ring is cracked the HUD chip now reads
      "COLLAPSE IN N" (the grace period — 2 actions, 3 with Stonemason — was tracked
      internally but invisible). `TickCrumble` emits the crack countdown through
      CrumbleChanged's turnsLeft while cracking.
- [x] **Verified** — build clean; 80 autoplay runs 0 failures (23 wins); UI-flow PASS.

## Round 26

- [x] **Selection shot prefers pieces with death-tile moves** — the red warning still has
      never been captured in-render; the driver now scans every player piece for a
      dangerous move and selects one when it exists (falls back to nearest-enemy).
      Verified the cracked-board shot renders correctly after the flourish fix
      ("THE BOARD CRACKS" centred over the smouldering ring).
- [x] **Verified** — build clean; windowed UI-flow PASS.

## Round 27

- [x] **Isolate the tutorial fade from the transition-kill sweep** — Round 24's fix kills
      all `_transitionTweens` on every GoState; the tutorial's fade-out was registered
      there, so completing the tutorial and pausing within 0.22s could kill the fade before
      its hide callback — a ghost tutorial scrim would then eat all UI input. The tutorial
      now fades on its own dedicated tween (killed only by its own re-fade / exit).
- [x] **Verified** — build clean; UI-flow PASS on a cleared save (tutorial path exercised);
      60 autoplay runs 0 failures.

## Round 28

- [x] **Reserve bar wraps into rows of 6** — a hoarded reserve (10+ pieces) used to clip
      off both screen edges, making those deploy buttons untappable. The bar is now a
      VBox of centred rows, offset upward per row.
- [x] **Verified** — build clean; 60 autoplay runs 0 failures (15 wins); UI-flow PASS.

## Round 29

- [x] **Export sanity** — `export_presets.cfg` uses all_resources with no filters, so the
      new `audio/*.wav` ship correctly; added `exclude_filter="dev/*"` so the harness
      scenes stay out of APKs (their scripts are release stubs anyway).
- [x] **Verified** — build clean; headless boot clean.

## Round 30

_Linux session resumes the loop (rounds 1-29 were on Windows). Setup: normalized the
Windows CRLF working tree back to LF (content-identical to HEAD); build clean; 955 unit
checks; 100-run autoplay 0 failures / 22 wins; 300-run sweep 23.7% win, finale 57%
(fair, no cliff — NO tuning, per the Round-4 discipline); UI-flow PASS; 4 baseline
screenshots reviewed (UI clear & polished); real-play logs clean. Round-30 problems
found by an adversarially-verified fan-out audit across all six subsystems._

- [x] **[rung 2] Fix pause-during-transition softlock** — `ScreenManager.GoPause`. The HUD
      pause button sits above the scrim and stays clickable during the 0.4s post-battle
      HUD fade-out; a tap there hijacks the in-flight Shop/GameOver transition into Paused,
      and RESUME returns to a board with `_running==false` where every tap/deploy no-ops —
      the run is stranded (only ABANDON escapes). Guard `GoPause` to act only in Playing.
- [x] **[rung 3] Deploy no longer hides the crumble telegraph** — `HexBoard.BeginDeploy`.
      Deploy targets were `IsPlayable && !_occupied`, which does NOT exclude `_cracked`
      tiles; the gold deploy highlight overpaints the cracked material (RefreshTileVisual
      early-returns for highlighted coords), so a reserve piece drops onto an
      about-to-collapse tile painted safe. Exclude `_cracked` tiles from deploy targets.
- [x] **[rung 3] Corrupt-save self-heal** — `GameSession.Load`. A malformed `hex.cfg`
      (verified via a corruption probe) makes `ConfigFile.Load` push a red ERROR + stack
      trace every boot and silently resets ALL meta progress (best battle / high score /
      crowns) to defaults. Load already bails safely to defaults; now on a *present but
      unreadable* file it re-Saves valid defaults so the error can't recur every boot and
      records accumulate again.
- [x] **Harness guard for the softlock** — added a "pause during the post-battle transition"
      probe to `UiFlowDriver.PhaseWinPath`: presses "II" mid-fade and fails if the pause
      overlay opens. Proven red→green — with the guard removed, uiflow exits 1
      ("pause hijacked the post-battle transition"); restored, it prints
      "pause during post-battle fade ignored ok" and PASSes.
- [x] **Verified** — build clean; 955 unit checks; UI-flow PASS (softlock probe green, and
      red when the fix is reverted); 100-run autoplay exit 0 / 0 failures (curve normal,
      finale 58%); corrupt-save probe: boot 1 = 1 parse error then heals, boot 2 = 0 errors.

(Deferred, logged for later: the confirmed rung-3 "tap during the 0.22s/0.32s enemy-visual
lag reads against ahead-of-visual state" is purely cosmetic + self-correcting per the
verifier; any input-lock fix risks the responsiveness pillar, so it is NOT worth it now.
Also noted: normal MOVE highlighting hides the crack telegraph the same way deploy did —
a broader rung-4 enhancement needing a distinct cracked-but-legal highlight material.)

## Round 31

_Rungs 1-3 clear (audit's confirmed bugs fixed; readability-desync deferred). Moving to
rung 4. Theme: multi-event actions must render each event distinctly — the pooled FX are
all single-instance, so concurrent events collapse. Category [feel] (last 2 tagged rounds
were [ui]/[qol], so [feel] respects the variety rule). All three confirmed by reading
`HexBoard.Fx.cs` + `Hud.cs`._

- [x] **[feel] Money pops no longer collapse** (pillar 3 economy readability + pillar 1) —
      `HexBoard.Fx.ShowMoneyPop` reused ONE `Label3D`/tween, so a capture pop and the
      battle-clear centre-bonus pop (which happen on EVERY battle-clearing capture) — and
      Bishop-Echo double captures — collapse into a single +$N, hiding money. Replace with
      a small round-robin pool of Label3D pops, each animating independently.
- [x] **[feel] Capture sparks no longer teleport** (pillar 1 readable exchanges) —
      `HexBoard.Fx.PlayCaptureBurst` reused ONE `CpuParticles3D`; a capture-then-counter-
      capture exchange Restart()s + repositions the same system mid-flight, so the first
      spark burst jumps to the second hex. Small round-robin pool of burst systems.
- [x] **[feel] Score pop doesn't fight itself** (pillar 1 consistent HUD feedback) —
      `Hud.SetScore` started a fresh, unkilled scale tween each call; multi-score actions
      (capture + clear bonus, Bishop Echo) fire overlapping pops. Kill the prior tween and
      reset scale first.
- [x] **Verified** — build clean (0 warnings); 955 unit checks; UI-flow PASS (softlock probe
      still green); 80-run autoplay exit 0 / 0 failures, orphans=0 (pools reuse, no leak).
      Behaviour is correct by construction (independent pooled nodes + tweens) + no
      regression; NOT eyeball-confirmed in-render (headless env — windowed capture on the
      shared :0 display is slow and risks the user's desktop, so it was not run).

## Round 32

_Rung 4 continues. Pillar-2/3 readability. [ui] tag (variety OK — last tagged rounds were
[feel] R31 and rungs-2/3 R30). Both from the audit's opportunity list; both complete an
existing system rather than adding new mechanics._

- [x] **[ui] Deploy mode telegraphs death tiles** (pillar 2 fair/readable tactics) —
      `HexBoard.BeginDeploy` painted every legal deploy tile safe-gold; a normal move gets
      a red death-tile warning but a deploy did not, so a reserve piece could be dropped
      straight into an enemy's capture range unwarned. Refactored `IsDeathTile` into a
      coord-based `IsDeathTileHypo(from,dest)` core and added `IsDeployDeathTile(dest)`
      (hypo origin == destination: a fresh piece appears, no line opens). Deploy tiles an
      enemy could capture next turn now pulse danger-red like move destinations.
- [x] **[ui] Shop shows piece → reserve when the army is full** (pillar 3 economy clarity) —
      at `ArmyCap` a bought piece silently overflows to the reserve; the piece card's tag
      now reads "PIECE → RESERVE" so the purchase's destination is never a surprise.
- [x] **Verified** — build clean (0 warnings); 955 unit checks; 100-run autoplay 27 wins /
      0 failures / exit 0 / orphans=0 (the IsDeathTile refactor left the bot's danger
      scoring unchanged and deploying with the new check never crashed); UI-flow PASS
      (shop still builds/buys). Deploy red-paint is a direct mirror of the verified
      move-selection path; exact render not eyeball-confirmed (headless).

## Round 33

_Rung 4 continues. [qol] (variety: last tagged rounds were [feel] R31, [ui] R32). From the
QoL pack: confirm-before-quit mid-game — the one clearly-destructive action in-run had no
guard._

- [x] **[qol] Confirm before abandoning a run** (pillar 3 — protects run progress) —
      `PauseOverlay` ABANDON RUN sits directly below RESUME and fired immediately, so a
      single mis-tap discarded the whole run. It now swaps to a two-step confirm
      ("Abandon this run? Your progress is lost." → YES, ABANDON / KEEP PLAYING); only YES
      commits. `Refresh` resets to the normal menu each time pause reopens so no stale
      confirm lingers. UI-flow driver updated to drive + guard the new step.
- [x] **Verified** — build clean; 955 unit checks; UI-flow PASS ("abandon confirm shown ok"
      then "abandon → title ok"). Proven red→green: with the confirm removed, uiflow
      exits 1 ("abandon skipped its confirmation"); restored, PASS.
- [ ] (dropped this round) remove orphan `CharacterSelect.cs.uid` — `git rm` hit the
      mount's stat-cache false-positive and would need `-f`; trivial value, not worth the
      friction. Left for a later round.
