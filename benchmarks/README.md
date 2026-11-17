# P10 Windows performance evidence harness (Draft)

This P10 development-only instrument is **NOT** a playable port or FPS benchmark result. The standalone scene `res://benchmarks/p10_benchmark.tscn` instantiates the **P1 primitive scene** plus a passive frame observer. Run **Current Scene** using Godot 4.7.2 stable on an actual Windows desktop. It warms up for 5 seconds and collects 30 seconds of non-headless `_process(delta)` display frame intervals. A JSON report is written in a new `user://neonvoid_p10_reports/` folder. It never edits the original Python JSON saves or Godot gameplay modules.

Frame interval metrics include mean, p50, p95, p99 and worst ms using nearest-rank percentiles, plus `1000 / mean_ms` FPS. This is not GPU kernel time and is affected by VSync/window occlusion/refresh rate. The configured renderer field does not prove the actual GPU backend. Headless test only proves arithmetic/scene wiring and explicitly disables measurements.

Actual P10 stage acceptance also requires:
- P0-P9 acceptance and integration.
- Real Windows exported executable, actual device/driver, Forward+ and Compatibility images, reported GPU utilization/CPU/memory/draw calls from external profiler, identical seed and scene.
- 960x540,1280x720,1920x1080 and 4:3/21:9 plus DPI 100/125/150/200 where feasible; correct UI, mouse and camera, F11/Alt+Tab/audio.
- 30-60 minute actual run through four freely ordered sectors, 5 waves + bosses, CLEAN, one permanent software extraction, safe checkpoints, corrupted save rollback.
- Compare matched Windows CPU/GPU p95 frame time and frame pacing of real integrated combat + P9 100/500/1000/2000 bullet workloads; P1 placeholder timings cannot be used as final game FPS.
- Keep Python/Tk available until an explicit P10 release sign-off.

To validate only calculations/scene under Godot: `godot --headless --path . --editor --import --quit` then `godot --headless --path . --script res://tests/p10_benchmark_smoke.gd`.
