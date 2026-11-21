#!/usr/bin/env python3
"""Summarise a FileScape per-frame CSV (--diag-frames=PATH, PLANET-179, AD-96).

  tools/frame-diag-summary.py FILE.csv [--worst N] [--segment PREFIX]

Prints, per segment (the probe's route label; "-" for frames outside one) and overall: frames, p50/p95/p99/max of the
gap between frames (what the player sees), counts over 33/100/300 ms, then the N worst frames with their largest
sections, GC pause, allocation, GPU time and notes (builds, composes, swaps). The CSV carries ids and milliseconds only,
never names, so the summary of a real-root run can be shared.
"""
import csv
import sys

THRESHOLDS_MS = (33, 100, 300)
FIXED = {"frame", "tS", "gapMs", "processMs", "otherMs", "gc0", "gc1", "gc2", "gcPauseMs", "allocKB", "renderCpuMs",
         "renderGpuMs", "segment", "notes"}


def pct(sorted_values, q):
    return sorted_values[min(len(sorted_values) - 1, int(len(sorted_values) * q))]


def stats(rows):
    gaps = sorted(float(r["gapMs"]) for r in rows)
    if not gaps:
        return "no frames"
    over = ", ".join(f">{t} ms {sum(1 for g in gaps if g > t)}" for t in THRESHOLDS_MS)
    gpu = sorted(float(r.get("renderGpuMs") or 0) for r in rows)
    return (f"{len(gaps)} frames, p50 {pct(gaps, .5):.1f} ms, p95 {pct(gaps, .95):.1f}, p99 {pct(gaps, .99):.1f}, "
            f"max {gaps[-1]:.1f}; {over}; GPU p50 {pct(gpu, .5):.1f} ms")


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__)
        return 2
    path, worst, prefix = argv[0], 8, None
    if "--worst" in argv:
        worst = int(argv[argv.index("--worst") + 1])
    if "--segment" in argv:
        prefix = argv[argv.index("--segment") + 1]
    with open(path, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    if prefix is not None:
        rows = [r for r in rows if (r.get("segment") or "").startswith(prefix)]
    sections = [k for k in (rows[0].keys() if rows else []) if k not in FIXED and k.endswith(tuple("abcdefghijklmnopqrstuvwxyz"))]
    # Section columns are the ones between otherMs and gc0 in the header.
    if rows:
        keys = list(rows[0].keys())
        sections = keys[keys.index("otherMs") + 1:keys.index("gc0")]
    segments = {}
    for r in rows:
        segments.setdefault(r.get("segment") or "-", []).append(r)
    print(f"{path}")
    print(f"  all: {stats(rows)}")
    for name, seg in segments.items():
        print(f"  {name}: {stats(seg)}")
    print(f"  worst {worst}:")
    for r in sorted(rows, key=lambda r: -float(r["gapMs"]))[:worst]:
        top = sorted(((float(r[s] or 0), s) for s in sections), reverse=True)[:3]
        tops = ", ".join(f"{s} {v:.0f}" for v, s in top if v >= 1)
        print(f"    {float(r['gapMs']):7.1f} ms at {float(r['tS']):9.2f} s [{r.get('segment') or '-'}] process {float(r['processMs']):.0f}"
              f" ({tops}); gc {r['gc0']}/{r['gc1']}/{r['gc2']} pause {float(r['gcPauseMs']):.0f} ms, alloc {int(r['allocKB']) // 1024} MB,"
              f" GPU {float(r['renderGpuMs'] or 0):.1f} ms; {(r.get('notes') or '')[:240]}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
