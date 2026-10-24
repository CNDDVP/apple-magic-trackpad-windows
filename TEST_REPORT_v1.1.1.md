# v1.1.1 validation

Windows11 x64; Bluetooth Apple USB-C Magic Trackpad A3120/PID0324; signed driver2026.3982.1.1000; Directory Opus13.25 x64. Raw traces and user file names remain outside the repository.

| Check | Result | Scope |
|---|---|---|
| Behavior tests |59 passed| Scan timing, pressure/drag interlock, calibrated curve, pressure dwell/rise/fast path, preview routing, virtual desktop pixel mapping |
| Integration |5 passed| Reload, invalid config protection/recovery,4 switches/9 adjustments, Windows INPUT ABI; no input injection |
| Update/rollback |5 groups passed| Upgrade, exact restore, corrupt snapshot rejection, invalid config rejection, failed-start auto-recovery |
| v1.1.1 ->v1.1.0 |passed|8 original runtime files restored byte for byte; personal settings retained; new preview source removed; isolated fixture |
| Original pressure replay |passed|2810 raw frames/20 presses/2587 valid scan intervals;4 drags rejected,7 single deep intents,ordinary clicks rejected |
| Comfort pressure replay |passed|1322 raw frames; ordinary clicks and2 comfortable long holds do not preview; deliberate deep press produces1 intent |
| Pointer regression capture |passed in this run| Before:2080 frames,22 large jumps opposite input direction; after:2028 frames,0 such jumps;0 failed input submissions |
| Opus preview |user confirmed| Deep press opens preview without manually pressing Space |
| Gentle pressure and drag |user confirmed| Preview easier; ordinary clicks and physical press-drag normal |
| Final cross-screen feel |user confirmed| Curve3.5/3.8/2.0 comfortable; targeting and continuous motion normal; no return to original monitor |

The optional preset uses threshold90,180ms stability,15 additional pressure above button-down,and immediate180 for deliberate hard presses. Movement/multi-contact/replacement/reload cancellation applies to both paths. Low thresholds without stability and additional-force protection are rejected. Defaults remain180/0ms with both additional options disabled.

Pointer output uses absolute normalized SendInput coordinates for the full virtual desktop. Tests cover every pixel over x=-1920..6399/y=0..2162 and native ABI layouts. The after-trace contains minor-axis sign changes across staggered displays, but none of the large jumps opposite overall movement seen before. Frame counts do not establish subjective comfort; actual user feedback supplies that evidence.

USB,sleep/reconnect,other Opus versions and monitor layouts need separate physical checks. No independent second-stage haptic support is available. Full driver installation was not rerun on this working PC.