# Changes

## 2026-07-20
- Session start (Linux): verified build clean, 955 unit checks pass, real-play logs error-free.
- Round 30: fixed a rung-2 softlock — tapping the HUD pause button during the post-battle fade-out no longer hijacks the transition and strands the run (ScreenManager.GoPause now only pauses from live play).
- Round 30: deploy mode no longer offers cracked (about-to-collapse) tiles, so a reserve piece can't be dropped onto a doomed tile that the gold highlight had painted safe.
- Round 30: a corrupt user://hex.cfg now self-heals to valid defaults on load instead of erroring every boot (progress no longer silently resets forever on a bad save file).
- Round 30: added a UI-flow harness probe that fails if the post-battle pause softlock ever returns.
- Round 31 [feel]: floating +$N money pops now use a pool, so a capture payout and the battle-clear bonus (and Bishop Echo double-captures) each rise from their own hex instead of collapsing into one.
- Round 31 [feel]: capture spark bursts now use a pool, so a capture and its counter-capture no longer teleport a single spark system mid-flight.
- Round 31 [feel]: the HUD score pop no longer stacks overlapping scale tweens on multi-score actions (kills the prior pop and resets first).
- Round 32 [ui]: deploy mode now paints death tiles red — a reserve piece dropped into an enemy's capture range gets the same warning a normal move does (previously deploy tiles were all safe-gold).
- Round 32 [ui]: shop piece cards read "PIECE → RESERVE" when the army is at its cap, so it's clear a purchase will overflow to the reserve.
- Round 33 [qol]: abandoning a run from the pause menu now asks for confirmation ("Abandon this run?") so a mis-tap on ABANDON (right under RESUME) can't discard a whole run.
- Round 34 [audio]: a successful shop purchase now plays a coin "ka-ching" over the tap tick, so buying feels rewarding and distinct from an unaffordable tap.
- Round 34 [feel]: the menu camera drift no longer fights a lingering defeat shake when returning from the Game Over screen quickly.
- Round 35 [ui]: the shop's money/army/reserve line now wraps instead of overrunning the screen when the army and reserve are large.
- Round 35 [ui]: added deploy-mode screenshot coverage to the UI-flow harness (verifies the deploy death-tile telegraph renders).
- Round 36 [fairness]: the death-tile (red) warning no longer marks tiles you've protected as lethal — a Shield tile or Royal Guard (for the King) now correctly reads as safe, since the enemy's single next capture there is blocked. Resolution and telegraph now share one predicate so they can't disagree.
