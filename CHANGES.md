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
