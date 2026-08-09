[CmdletBinding()]
param(
    [string]$SourceRoot = "",
    [string]$ChromePath = "",
    [string]$FfmpegPath = "",
    [int]$Seconds = 15,
    [int]$Fps = 30,
    [switch]$SkipRender
)

$ErrorActionPreference = "Stop"
$toolRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$developmentRoot = (Resolve-Path -LiteralPath (Join-Path $toolRoot "..\..")).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $developmentRoot "..\..")).Path
$outputRoot = Join-Path $developmentRoot "assets\backgrounds"

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Join-Path $repoRoot "wallpaper_conversion"
}
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path

if ([string]::IsNullOrWhiteSpace($ChromePath)) {
    $candidates = @(
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    )
    $ChromePath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($FfmpegPath)) {
    $ffmpegCommand = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($ffmpegCommand) {
        $FfmpegPath = $ffmpegCommand.Source
    } else {
        $FfmpegPath = "C:\Program Files (x86)\Tencent Games\VALORANT\ACLOS\Cross\recorder-release\ffmpeg.exe"
    }
}
$ffprobePath = Join-Path (Split-Path -Parent $FfmpegPath) "ffprobe.exe"

if (-not (Test-Path -LiteralPath $ChromePath)) { throw "Chrome/Edge executable not found: $ChromePath" }
if (-not (Test-Path -LiteralPath $FfmpegPath)) { throw "FFmpeg executable not found: $FfmpegPath" }
if (-not (Test-Path -LiteralPath $ffprobePath)) { throw "FFprobe executable not found next to FFmpeg: $ffprobePath" }

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$madaraSource = Join-Path $SourceRoot "..\羁绊\background.mp4"
$madaraOutput = Join-Path $outputRoot "susanoo-madara.mp4"
$sasukeSource = Join-Path $SourceRoot "sasuke_web_wallpaper\assets\background.jpg"
$sasukeOutput = Join-Path $outputRoot "flowing-sasuke.mp4"

if (-not (Test-Path -LiteralPath $madaraSource)) { throw "Source video not found: $madaraSource" }
if (-not (Test-Path -LiteralPath $sasukeSource)) { throw "Sasuke source image not found: $sasukeSource" }

Write-Host "[1/4] Remuxing 须佐斑 without audio..."
& $FfmpegPath -y -hide_banner -loglevel warning -i $madaraSource -map 0:v:0 -c:v copy -an -movflags +faststart $madaraOutput
if ($LASTEXITCODE -ne 0) { throw "FFmpeg failed while preparing 须佐斑 ($LASTEXITCODE)" }

if (-not $SkipRender) {
    Write-Host "[2/4] Rendering 流年佐助 Canvas animation..."
    $node = (Get-Command node.exe -ErrorAction Stop).Source
    & $node (Join-Path $toolRoot "render-wallpaper.mjs") `
        --source $sasukeSource `
        --output $sasukeOutput `
        --chrome $ChromePath `
        --ffmpeg $FfmpegPath `
        --width 1920 `
        --height 1080 `
        --fps $Fps `
        --seconds $Seconds
    if ($LASTEXITCODE -ne 0) { throw "Canvas renderer failed ($LASTEXITCODE)" }
} elseif (-not (Test-Path -LiteralPath $sasukeOutput)) {
    throw "-SkipRender was specified but output does not exist: $sasukeOutput"
}

function Get-MediaProbe([string]$Path) {
    $json = & $ffprobePath -v error -show_entries format=duration:stream=index,codec_type,codec_name,pix_fmt,width,height,r_frame_rate,avg_frame_rate,channels -of json $Path
    if ($LASTEXITCODE -ne 0) { throw "FFprobe failed for $Path" }
    return ($json | ConvertFrom-Json)
}

Write-Host "[3/4] Verifying media streams..."
$probeMadara = Get-MediaProbe $madaraOutput
$probeSasuke = Get-MediaProbe $sasukeOutput
foreach ($probe in @(@{Name="susanoo-madara.mp4"; Data=$probeMadara}, @{Name="flowing-sasuke.mp4"; Data=$probeSasuke})) {
    $streams = @($probe.Data.streams)
    $video = $streams | Where-Object codec_type -eq "video" | Select-Object -First 1
    $audio = $streams | Where-Object codec_type -eq "audio"
    if (-not $video) { throw "$($probe.Name) has no video stream" }
    if ($audio.Count -ne 0) { throw "$($probe.Name) unexpectedly has an audio stream" }
    if ($video.pix_fmt -ne "yuv420p") { throw "$($probe.Name) is not yuv420p ($($video.pix_fmt))" }
    Write-Host ("  {0}: {1}x{2}, {3}, {4} sec, no audio" -f $probe.Name,$video.width,$video.height,$video.codec_name,$probe.Data.format.duration)
}

Write-Host "[4/4] Writing asset manifest and hashes..."
$manifest = @(
    [PSCustomObject]@{ id="susanoo-madara"; displayName="须佐斑"; fileName="susanoo-madara.mp4"; durationSeconds=[double]$probeMadara.format.duration; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $madaraOutput).Hash.ToLowerInvariant() },
    [PSCustomObject]@{ id="flowing-sasuke"; displayName="流年佐助"; fileName="flowing-sasuke.mp4"; durationSeconds=[double]$probeSasuke.format.duration; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $sasukeOutput).Hash.ToLowerInvariant() }
)
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot "manifest.json") -Encoding UTF8
$manifest | Format-Table -AutoSize
Write-Host "Background build completed: $outputRoot"
