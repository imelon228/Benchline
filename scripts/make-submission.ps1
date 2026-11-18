$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$parent = Split-Path -Parent $root
$target = Join-Path $parent 'IT-2504_Yerzhanov_Yeskendirov_FinalProject.zip'
$changes = git -C $root status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Run this script from a Git checkout.' }
if ($changes) { throw 'Commit the intended submission files before packaging.' }

git -C $root archive --format=zip --output=$target HEAD
if ($LASTEXITCODE -ne 0) { throw 'Git could not write the submission archive.' }
Write-Output $target
