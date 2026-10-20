#!/usr/bin/env python3
"""Fail closed if legacy DJ Library public artifacts/releases are re-enabled."""
from pathlib import Path
import sys
root = Path(__file__).resolve().parents[1]
workflow = (root / ".github/workflows/windows-ci.yml").read_text(encoding="utf-8").splitlines()
release = (root / ".github/workflows/release-qualified.yml").read_text(encoding="utf-8").splitlines()
errors = []

def gated(lines, label):
    starts = [i for i, line in enumerate(lines) if line.strip() == label]
    return len(starts) == 1 and any(x.strip().startswith("if: false")
                                    for x in lines[starts[0]+1:starts[0]+6])

for label in ("- name: Upload qualified artifact", "- name: Upload failure diagnostics"):
    if not gated(workflow, label):
        errors.append(label + " not gated")
if sum("uses: actions/upload-artifact@" in line for line in workflow) != 2:
    errors.append("unexpected upload action count")
if not gated(release, "release:"):
    errors.append("manual release job not gated")
if errors:
    sys.exit("FAIL public artifacts/releases: " + "; ".join(errors))
print("PASS: legacy DJ Library builds/tests only; no public artifacts or releases")
