#!/usr/bin/env bash
# Builds the packet-term .deb for one architecture.
#
#   debian/build-deb.sh <version> [amd64|arm64|armhf] [outdir]
#
# Produces <outdir>/packet-term_<arch>.deb containing a self-contained single-file build (no
# .NET runtime dependency on the target). Default outdir is <repo>/artifacts.
#
# The version is deliberately not in the file name, though it is in the package's own control
# data, so https://github.com/packet-net/packet-term-tui/releases/latest/download/packet-term_<arch>.deb
# always points at the current release. Same convention as pdn-qso.
#
# packet-net/apt mirrors these from releases/latest; see the dispatch step in release.yml.
#
# A program somebody runs, not a service: no systemd unit, no system user, no seeded config.
# Settings are per user and Packet.Term writes them itself on first run.
#
# Layout: the payload goes in /usr/lib/packet-term/ and /usr/bin/packet-term is a symlink into
# it, so any native shim published loose beside the bundle is still found relative to the real
# path of the running binary.
set -euo pipefail

VERSION="${1:?usage: build-deb.sh <version> [arch] [outdir]}"
ARCH="${2:-amd64}"

case "$ARCH" in
  amd64) RID=linux-x64 ;;
  arm64) RID=linux-arm64 ;;
  armhf) RID=linux-arm ;;
  *) echo "unsupported arch $ARCH" >&2; exit 2 ;;
esac

# dpkg-deb ships in the Essential `dpkg` package, so this only trips on a non-Debian host.
command -v dpkg-deb >/dev/null || { echo "dpkg-deb not found - this needs a Debian-family host" >&2; exit 3; }

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
OUTDIR="${3:-$ROOT/artifacts}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

PKGDIR=/usr/lib/packet-term
DOCDIR=/usr/share/doc/packet-term

# Same publish flags as the standalone release binaries, so the .deb runs the same bits.
dotnet publish "$ROOT/src/Packet.Term/Packet.Term.csproj" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:Version="$VERSION" \
  -p:InformationalVersion="$VERSION" \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -p:GenerateDocumentationFile=false \
  --output "$STAGE/publish"

mkdir -p "$STAGE/root$PKGDIR" \
         "$STAGE/root/usr/bin" \
         "$STAGE/root$DOCDIR" \
         "$STAGE/root/DEBIAN"

install -m 0755 "$STAGE/publish/Packet.Term" "$STAGE/root$PKGDIR/packet-term"
for so in "$STAGE"/publish/*.so; do
  [ -e "$so" ] || continue
  install -m 0644 "$so" "$STAGE/root$PKGDIR/$(basename "$so")"
done
ln -s "..${PKGDIR#/usr}/packet-term" "$STAGE/root/usr/bin/packet-term"

install -m 0644 "$HERE/copyright" "$STAGE/root$DOCDIR/copyright"
install -m 0644 "$ROOT/LICENSE" "$STAGE/root$DOCDIR/LICENSE"
install -m 0644 "$ROOT/README.md" "$STAGE/root$DOCDIR/README.md"

# Debian changelog. A numeric SOURCE_DATE_EPOCH keeps rebuilds of a tag byte-identical.
case "${SOURCE_DATE_EPOCH:-}" in
  ''|*[!0-9]*) CHANGELOG_DATE="$(date -R)" ;;
  *)           CHANGELOG_DATE="$(date -R --date="@$SOURCE_DATE_EPOCH")" ;;
esac
cat > "$STAGE/changelog.Debian" <<EOF
packet-term ($VERSION) unstable; urgency=medium

  * Release $VERSION. See https://github.com/packet-net/packet-term-tui/releases/tag/v$VERSION

 -- Tom Fanning M0LTE <tom@m0lte.uk>  $CHANGELOG_DATE
EOF
gzip -9n -c "$STAGE/changelog.Debian" > "$STAGE/root$DOCDIR/changelog.Debian.gz"
chmod 0644 "$STAGE/root$DOCDIR/changelog.Debian.gz"

INSTALLED_SIZE="$(du -k -s --exclude=DEBIAN "$STAGE/root" | cut -f1)"

# libicu: the build is globalization-enabled (as the standalone binaries are), and .NET refuses
# to start without ICU. Same alternatives list as dapps on the apt repo.
cat > "$STAGE/root/DEBIAN/control" <<EOF
Package: packet-term
Version: $VERSION
Architecture: $ARCH
Maintainer: Tom Fanning M0LTE <tom@m0lte.uk>
Installed-Size: $INSTALLED_SIZE
Depends: libc6, libgcc-s1, libstdc++6, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu69 | libicu68 | libicu67 | libicu66 | libicu63
Section: hamradio
Priority: optional
Homepage: https://github.com/packet-net/packet-term-tui
Description: Terminal UI for AX.25 connected-mode sessions over a KISS modem
 A full-screen terminal for AX.25 connected-mode packet radio: a frame monitor,
 a conversation pane and an input line, driving one session at a time through
 a KISS modem on a USB serial port (--port) or a KISS-over-TCP listener (--tcp).
 .
 Run it as yourself: there is no service and no system-wide configuration.
 Built on the Packet.NET libraries.
EOF

mkdir -p "$OUTDIR"
DEB="$OUTDIR/packet-term_${ARCH}.deb"
dpkg-deb --build --root-owner-group "$STAGE/root" "$DEB"
echo "built: $DEB"
