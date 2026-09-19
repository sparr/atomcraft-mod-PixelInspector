#!/usr/bin/env bash
# Stage what a release carries, and checksum it. Publishes nothing.
#
#   ./release.sh <version>
#
# Two jobs, and the second is the one that is easy to forget. Build the artifacts under Release
# configuration -- a Debug assembly carries DebuggableAttribute with DisableOptimizations, which
# turns the JIT off for it entirely, so a Debug zip must never be published. And write SHA256SUMS
# beside them, because a consumer pins that value: every project in this family verifies the zip
# it compiles and tests against (see lib/harness.sh), and a release without sums leaves whoever
# does that recording the sha of whatever they happened to download.
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:?usage: ./release.sh <version>   (for example ./release.sh 0.1.0)}"

./build.sh --release

OUT="build/release-$VERSION"
rm -rf "$OUT"
mkdir -p "$OUT"

# What this mod publishes. The LICENSE ships inside each zip as well, so a player who downloads
# only the mod still gets the terms.
cp build/PixelInspector.zip "$OUT/"

# Zips only, deliberately, and the same call the consumer makes. GitHub generates the source
# archives rather than taking an upload and their bytes are not guaranteed stable over time, so
# covering them would promise more than it can keep. Note what this does and does not claim: it
# attests to the bytes that were uploaded, and is not a reproducibility claim, since a .NET
# assembly embeds a path-derived id and so never rebuilds byte for byte elsewhere.
( cd "$OUT" && sha256sum ./*.zip | sed 's| \./| |' > SHA256SUMS )
cat "$OUT/SHA256SUMS"

cat <<EOF

Staged in $OUT. Nothing has been published. Upload every file listed above, SHA256SUMS included:

  gh release create v$VERSION $OUT/* --title "Pixel Inspector $VERSION" --notes-file <notes>
EOF
