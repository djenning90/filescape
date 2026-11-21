#!/usr/bin/env python3
"""Where a Core test run's time went (TEST-13, AD-90), from its TRX file.

    tools/test-times.py RUN.trx [--top N]

Prints the run's span (first test start to last test end), the sum of the tests' own durations and their count, then the
N test classes that finished last (each class is one xUnit collection: its span is what the run waits for) with each
class's own sum, and the N slowest tests. tools/fs.sh prints this after every Core run so the costs are visible in the
normal output. Read only: it never changes the TRX or anything else.
"""
import sys
import datetime
import collections
import xml.etree.ElementTree as ET

NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def seconds(duration):
    h, m, s = duration.split(':')
    return int(h) * 3600 + int(m) * 60 + float(s)


def stamp(text):
    # TRX times carry 7 fractional digits and an offset; fromisoformat takes at most 6 before Python 3.11.
    if '.' in text:
        head, rest = text.split('.', 1)
        digits = ''.join(c for c in rest if c.isdigit())
        offset = rest[len(digits):]
        text = f"{head}.{digits[:6]}{offset}"
    return datetime.datetime.fromisoformat(text)


def main(argv):
    if not argv or argv[0].startswith('-'):
        print(__doc__.strip(), file=sys.stderr)
        return 2
    top = int(argv[argv.index('--top') + 1]) if '--top' in argv else 8
    root = ET.parse(argv[0]).getroot()
    classes = {u.get('id'): u.find('t:TestMethod', NS).get('className').split('.')[-1] for u in root.iterfind('.//t:UnitTest', NS)}
    own = collections.defaultdict(float)
    span = {}
    tests = []
    for r in root.iterfind('.//t:UnitTestResult', NS):
        if not r.get('duration') or not r.get('startTime'):
            continue
        c = classes.get(r.get('testId'), '?')
        d = seconds(r.get('duration'))
        a, b = stamp(r.get('startTime')), stamp(r.get('endTime'))
        own[c] += d
        lo, hi = span.get(c, (a, b))
        span[c] = (min(lo, a), max(hi, b))
        tests.append((d, f"{c}.{r.get('testName').split('.')[-1]}"))
    if not tests:
        print('test-times: no timed results in', argv[0])
        return 0
    t0 = min(a for a, _ in span.values())
    t1 = max(b for _, b in span.values())
    print(f"test time: span {(t1 - t0).total_seconds():.1f} s, sum of tests {sum(own.values()):.1f} s over {len(tests)} tests")
    print(f"  the {top} classes that finished last (span from the first test; own = the class's tests summed):")
    for c, (a, b) in sorted(span.items(), key=lambda kv: kv[1][1], reverse=True)[:top]:
        print(f"    {(b - t0).total_seconds():7.1f} s  own {own[c]:7.1f} s  {c}")
    print(f"  the {top} slowest tests:")
    for d, name in sorted(tests, reverse=True)[:top]:
        print(f"    {d:7.1f} s  {name[:150]}")
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
