"""Summarize MansionGameplayVerification CSV exports into comparison tables.

    python Research/summarize_benchmarks.py <results.csv> [samples.csv] [container_stats.csv]

results.csv has one row per run (backend, policy, scenario, seed, agents, outcomes,
decision counts, latency percentiles). samples.csv has every latency sample, so
distributions are computed from raw data rather than averaged percentiles.
"""

import csv
from collections import defaultdict
import statistics
import sys


def number(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def config(row):
    return "Behavior Tree" if row["backend"] == "BehaviorTree" else f"s(CASP) {row['policy'].lower()}"


def percentile(values, p):
    if not values:
        return None
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, max(0, int(round(p * len(ordered) + 0.5)) - 1))]


def spread(values):
    values = [v for v in values if v is not None]
    if not values:
        return "-"
    if len(values) == 1:
        return f"{values[0]:.1f}"
    return f"{statistics.median(values):.1f} [{min(values):.1f}-{max(values):.1f}]"


def main(results_path, samples_path=None, stats_path=None):
    rows = list(csv.DictReader(open(results_path, encoding="utf-8")))
    groups = defaultdict(list)
    for row in rows:
        groups[(row["scenario"], int(row["agents"]), config(row))].append(row)

    print("| Scenario | NPCs | AI | Runs | Won | Win time s | Levers | Barricades repaired | Hits | Alive at end | Escapes | Zombies in walls | Requests (skipped) | Commands |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for (scenario, agents, name), runs in sorted(groups.items()):
        won = sum(r["won"] == "True" for r in runs)
        requests = sum(int(r["requests"] or 0) for r in runs)
        skipped = sum(int(r["skipped"] or 0) for r in runs)
        print(f"| {scenario} | {agents} | {name} | {len(runs)} | {won}/{len(runs)} | "
              f"{spread([number(r['win_time']) for r in runs])} | "
              f"{spread([number(r['levers_pulled']) for r in runs])} | "
              f"{spread([number(r['barricades_repaired']) for r in runs])} | "
              f"{spread([number(r['hits']) for r in runs])} | "
              f"{spread([number(r['alive_end']) for r in runs])} | "
              f"{sum(int(r['escapes'] or 0) for r in runs)} | {sum(int(r['zombie_in_wall'] or 0) for r in runs)} | "
              f"{requests if requests else '-'}{f' ({skipped})' if requests else ''} | "
              f"{sum(int(r['commands'] or 0) for r in runs)} |")

    print()
    print("| Scenario | NPCs | AI | Stale | Errors | Timeouts | Decisions/s | Memory delta MB | hunt_lever | move_to_lever | pull_lever | run_from | move_to_barricade | hold_barricade | wait |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for (scenario, agents, name), runs in sorted(groups.items()):
        actions = [sum(int(r[a] or 0) for r in runs) for a in ("hunt_lever", "move_to_lever", "pull_lever", "run_from", "move_to_barricade", "hold_barricade", "wait")]
        print(f"| {scenario} | {agents} | {name} | {sum(int(r['stale'] or 0) for r in runs)} | {sum(int(r['errors'] or 0) for r in runs)} | "
              f"{sum(int(r['timeouts'] or 0) for r in runs)} | {spread([number(r['decisions_per_real_s']) for r in runs])} | "
              f"{spread([number(r['memory_delta_mb']) for r in runs])} | " + " | ".join(map(str, actions)) + " |")

    if samples_path:
        by_run = {r["run_id"]: r for r in rows}
        latency = defaultdict(list)
        for sample in csv.DictReader(open(samples_path, encoding="utf-8")):
            run = by_run.get(sample["run_id"])
            if run is None:
                continue
            label = f"{run['scenario']}/{run['agents']}/{config(run)}{'/no-skip' if run.get('unchanged_skip') == '0' else ''}{'/explain' if run.get('explain') == 'True' else ''}"
            latency[(label, sample["kind"])].append(float(sample["ms"]))
        print()
        print("| Runs | Measure | Samples | p50 ms | p90 ms | p95 ms | p99 ms | max ms |")
        print("|---|---|---|---|---|---|---|---|")
        for (label, kind), values in sorted(latency.items()):
            digits = 4 if kind == "bt_decision" else 0
            cells = " | ".join(f"{percentile(values, p):.{digits}f}" for p in (0.5, 0.9, 0.95, 0.99, 1.0))
            print(f"| {label} | {kind} | {len(values)} | {cells} |")

    if stats_path:
        cpu, memory = [], []
        for line in open(stats_path, encoding="utf-8-sig"):
            parts = line.strip().split(",")
            if len(parts) >= 3 and parts[1].endswith("%"):
                cpu.append(float(parts[1].rstrip("%")))
                memory.append(parts[2].split("/")[0].strip())
        if cpu:
            print(f"\nSolver container CPU over the matrix: median {statistics.median(cpu):.0f}%, p95 {percentile(cpu, 0.95):.0f}%, max {max(cpu):.0f}% "
                  f"(100% = one core; {len(cpu)} samples). Memory samples: {memory[0]} ... {memory[-1]}")


if __name__ == "__main__":
    main(*sys.argv[1:4])
