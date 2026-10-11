#!/usr/bin/env bash
# SSH.NET package (Renci.SshNet) built from the pinned commit of this fork, verified after the clone. The package
# version is computed by Nerdbank.GitVersioning from the commit and its history (hence the full clone), without the
# GitHub Actions variables so that it does not depend on the branch that runs the build.
#
# Output in out/: SSH.NET.<version>.nupkg and .snupkg. The release workflow publishes them as release "v<version>".
set -euo pipefail

repository=https://github.com/muller-camille/SSH.NET
commit=c3245ef61d4bf06c986d7e486d1b7de4686320d7
version=2026.0.1-zillaterm.2.gc3245ef61d

root=$(cd "$(dirname "$0")" && pwd)
out="$root/out"

if [ "${1:-}" = "--tag" ]; then
  echo "v$version"
  exit 0
fi
if [ "${1:-}" = "--commit" ]; then
  echo "$commit"
  exit 0
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
git clone --quiet "$repository" "$work/sshnet"
git -C "$work/sshnet" checkout --quiet --detach "$commit"
test "$(git -C "$work/sshnet" rev-parse HEAD)" = "$commit"

rm -rf "$out"
env -u GITHUB_ACTIONS -u GITHUB_REF dotnet pack "$work/sshnet/src/Renci.SshNet" -c Release -o "$out"
test -f "$out/SSH.NET.$version.nupkg"
echo "SSH.NET $version: built from $repository at commit $commit."
