extends RefCounted
## P10: finite positive frame intervals; nearest-rank quantiles, no gameplay.
var limit: int = 12000
var values: Array[float] = []

func _init(max_count: int = 12000) -> void:
    limit = maxi(1, max_count)

func add_ms(dt_ms: float) -> bool:
    if not is_finite(dt_ms) or dt_ms <= 0.0 or values.size() >= limit:
        return false
    values.append(dt_ms)
    return true

func count() -> int:
    return values.size()

func _percentile(sorted_values: Array[float], p: float) -> float:
    return sorted_values[clampi(ceili(float(sorted_values.size()) * p) - 1, 0, sorted_values.size() - 1)]

func summary() -> Dictionary:
    if values.is_empty():
        return {"ok": false, "reason": "no_valid_samples"}
    var sorted_values: Array[float] = values.duplicate()
    sorted_values.sort()
    var sum_ms: float = 0.0
    for v in values:
        sum_ms += v
    var mean_ms: float = sum_ms / values.size()
    var p95: float = _percentile(sorted_values, 0.95)
    return {"ok": true, "count": values.size(),
        "mean_ms": mean_ms, "p50_ms": _percentile(sorted_values, 0.50),
        "p95_ms": p95, "p99_ms": _percentile(sorted_values, 0.99),
        "max_ms": sorted_values.back(), "fps_from_mean_ms": 1000.0 / mean_ms,
        "fps_at_p95_ms": 1000.0 / p95,
        "percentile_method": "nearest_rank_ceil_n_times_p"}
