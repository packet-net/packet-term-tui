#!/usr/bin/env python3
"""Fails if any release artifact bundles a .NET 10 runtime older than the latest patch.

The binaries and .debs are self-contained single-file executables, so each carries the runtime
of whichever SDK built it, and an operating system update never patches it. A stale SDK would
ship months of security fixes behind and nothing would notice. This reads the runtime version
out of each program (the executable embeds its deps.json, which names the runtime pack as
runtimepack.Microsoft.NETCore.App.Runtime.<rid>/<version>) and compares it with the latest 10.0
runtime Microsoft lists.

A runtime newer than the latest the index lists passes: SDK workloads can reach nuget.org a few
days before the release index lists that patch (10.0.14 on 2026-10-25, which failed another
packet-net release), and the point is to catch a stale build machine, not one ahead of the index.
It fails when a program bundles an older runtime, or zero or several recognisable versions.

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


def version_key(version):
    return tuple(int(part) for part in version.split("."))


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
            if len(found) != 1:
                print(f"::error::{artifact.name} bundles .NET runtime {', '.join(found) or 'unknown'}, "
                      "not one recognisable version. Check how the program was built.")
                bad = True
            elif version_key(found[0]) < version_key(want):
                print(f"::error::{artifact.name} bundles .NET runtime {found[0]}, older than {want}. "
                      "Update the .NET 10 SDK on the machine that built it.")
                bad = True
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
