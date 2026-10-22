#!/usr/bin/env python3
"""Fails unless every release artifact bundles the latest .NET 10 runtime patch.

The binaries and .debs are self-contained single-file executables, so each carries the runtime
of whichever SDK built it, and an operating system update never patches it. A stale SDK would
ship months of security fixes behind and nothing would notice. This reads the runtime version
out of each program (the executable embeds its deps.json, which names the runtime pack as
runtimepack.Microsoft.NETCore.App.Runtime.<rid>/<version>) and compares it with the latest 10.0
runtime Microsoft lists.

Usage: scripts/check-bundled-runtime.py <packet-term-binary|package.deb>...
"""
import json
import re
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

INDEX = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"
CHANNEL = "10.0"
RUNTIME = re.compile(rb"runtimepack\.Microsoft\.NETCore\.App\.Runtime\.[a-z0-9-]+/(\d+\.\d+\.\d+)")


def latest_runtime():
    with urllib.request.urlopen(INDEX, timeout=30) as response:
        index = json.load(response)
    for channel in index["releases-index"]:
        if channel["channel-version"] == CHANNEL:
            return channel["latest-runtime"]
    sys.exit(f"no .NET {CHANNEL} channel in {INDEX}")


def program_bytes(artifact):
    if artifact.suffix != ".deb":
        return [(artifact.name, artifact.read_bytes())]
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run(["dpkg-deb", "-x", str(artifact), tmp], check=True)
        return [(str(p.relative_to(tmp)), p.read_bytes())
                for p in Path(tmp).rglob("packet-term") if p.is_file() and not p.is_symlink()]


def main(artifacts):
    if not artifacts:
        sys.exit(__doc__)
    want = latest_runtime()
    print(f"latest .NET {CHANNEL} runtime: {want}")
    bad = False
    for artifact in map(Path, artifacts):
        programs = program_bytes(artifact)
        if not programs:
            print(f"::error::{artifact.name}: no program found to check")
            bad = True
        for name, data in programs:
            found = sorted({m.decode() for m in RUNTIME.findall(data)})
            print(f"{artifact.name}: {name} bundles {', '.join(found) or 'nothing recognisable'}")
            if found != [want]:
                print(f"::error::{artifact.name} bundles .NET runtime {', '.join(found) or 'unknown'}, "
                      f"not {want}. Update the .NET 10 SDK on the machine that built it.")
                bad = True
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
