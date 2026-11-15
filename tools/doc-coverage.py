#!/usr/bin/env python3
"""Class-header coverage (2026-11-14): every hand-written type declaration (class, record, struct, enum, interface) in
src/, app/Scripts/ and tests/ should be preceded by an XML doc comment (///) on its primary declaration. Partial
declarations in other files count as covered when any declaration of that type has a header; they may carry a short
// or /// note about the file's role. Prints the uncovered types and a summary; exit 1 when any are missing.
   tools/doc-coverage.py [--list] [paths...]"""
import re, sys, pathlib
ROOT = pathlib.Path(__file__).resolve().parent.parent
args = [a for a in sys.argv[1:] if not a.startswith('--')]
paths = [ROOT / p for p in (args or ['src', 'app/Scripts', 'tests'])]
DECL = re.compile(r'^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|file|ref|unsafe|new)\s+)*(class|record struct|record class|record|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)')
types = {}  # name -> list of (file, line, documented, partial)
for base in paths:
    for f in sorted(base.rglob('*.cs')):
        if any(part.startswith('obj') or part.startswith('bin') for part in f.parts) or '.claude' in f.parts: continue
        lines = f.read_text(encoding='utf-8', errors='replace').split('\n')
        for i, line in enumerate(lines):
            m = DECL.match(line)
            if not m or line.strip().startswith('//'): continue
            if '(' in line.split(m.group(2))[0]: continue  # a local function or expression, not a declaration
            j = i - 1
            while j >= 0 and (lines[j].strip().startswith('[') or lines[j].strip() == ''): j -= 1
            documented = j >= 0 and lines[j].strip().startswith('///')
            key = (m.group(2), str(f.relative_to(ROOT)).split('/')[0])
            types.setdefault(m.group(2), []).append((str(f.relative_to(ROOT)), i + 1, documented, 'partial' in line))
missing = []
for name, decls in sorted(types.items()):
    if any(d[2] for d in decls): continue
    missing.append((name, decls[0]))
total = len(types)
print(f"types: {total}, with a header: {total - len(missing)} ({100 * (total - len(missing)) / max(total, 1):.0f} %), without: {len(missing)}")
if '--list' in sys.argv:
    for name, (f, line, _, _) in missing: print(f"  {f}:{line}  {name}")
sys.exit(1 if missing else 0)
