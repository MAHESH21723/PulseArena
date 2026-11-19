# Runs make_logo.py then make_trailer.py and prints outputs + durations.
# Usage: .\make_all.ps1 [-Showcase <dir>] [-Out <dir>] [-Branding <dir>] [extra args for make_logo.py]
param(
    [string]$Showcase = "D:/GIT-repo/PulseArena/QA/v0.3/Showcase",
    [string]$Out = "D:/GIT-repo/PulseArena/docs/media",
    [string]$Branding = "D:/GIT-repo/PulseArena/Assets/PulseArena/Branding",
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$LogoArgs
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$ffprobe = "C:\Users\Maheshwaran\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe"

python "$here\make_logo.py" $Showcase $Out --out-branding $Branding @LogoArgs
if ($LASTEXITCODE -ne 0) { throw "make_logo.py failed" }
python "$here\make_trailer.py" $Showcase $Out
if ($LASTEXITCODE -ne 0) { throw "make_trailer.py failed" }

Write-Host "`n== Outputs =="
foreach ($f in "trailer.mp4", "trailer-square.mp4") {
    $p = Join-Path $Out $f
    $d = & $ffprobe -v error -show_entries format=duration -of default=nw=1:nk=1 $p
    $s = & $ffprobe -v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate -of csv=p=0 $p
    "{0}  {1:N2}s  {2}  {3:N1} MB" -f $p, [double]$d, $s, ((Get-Item $p).Length / 1MB)
}
Get-ChildItem $Out -Include *.png, *.jpg -Recurse | ForEach-Object { $_.FullName }
