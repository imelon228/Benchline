#!/usr/bin/env sh
set -eu

root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
out="$(dirname -- "$root")/IT-2504_Yerzhanov_Yeskendirov_FinalProject.zip"

changes=$(git -C "$root" status --porcelain)
if [ -n "$changes" ]; then
    printf '%s\n' 'Commit the intended submission files before packaging.' >&2
    exit 1
fi

git -C "$root" archive --format=zip --output="$out" HEAD
printf '%s\n' "$out"
