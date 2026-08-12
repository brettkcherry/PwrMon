# Generates docs/assets/social-card.png — the GitHub social preview card (1280x640).
#
# Design: the card IS the instrument. PwrMon's differentiator is the live wattage, so the
# hero is a real-looking discharge readout rather than an icon-and-title lockup — at the
# ~640x320 most link previews actually render, a giant number reads and a small icon doesn't.
# Palette is the app's own "Volt" theme so the card and the app agree.
#
# GitHub wants 1280x640 (Settings -> General -> Social preview). Under 1 MB.
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$outDir = Join-Path $PSScriptRoot "..\docs\assets"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outPath = Join-Path $outDir "social-card.png"

$W = 1280; $H = 640

# ---- Volt palette (src/PwrMon/Services/ThemeService.cs) ----
function C([int]$r, [int]$g, [int]$b, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
$bg       = C 0x0F 0x11 0x15
$card     = C 0x17 0x1A 0x21
$text     = C 0xE8 0xEA 0xF0
$textDim  = C 0x8B 0x93 0xA7
$amber    = C 0xF5 0xB6 0x2E
$orange   = C 0xF0 0x88 0x3E
$grid     = C 0x23 0x28 0x33

$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear($bg)

# ---- ambient glow: warm bloom behind the readout, cool nothing elsewhere ----
function Add-Glow([int]$cx, [int]$cy, [int]$radius, [System.Drawing.Color]$colour, [int]$alpha) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($cx - $radius, $cy - $radius, $radius * 2, $radius * 2)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush($path)
    $brush.CenterColor = [System.Drawing.Color]::FromArgb($alpha, $colour.R, $colour.G, $colour.B)
    $brush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $colour.R, $colour.G, $colour.B))
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}
Add-Glow -cx 250 -cy 300 -radius 620 -colour $orange -alpha 34
Add-Glow -cx 980 -cy 250 -radius 460 -colour $amber  -alpha 16

# ---- fonts: both ship with Windows; Bahnschrift is the app's own numeral face ----
function Font([string]$family, [single]$size, [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Regular) {
    New-Object System.Drawing.Font($family, $size, $style, [System.Drawing.GraphicsUnit]::Pixel)
}
$fNum    = Font "Bahnschrift" 132 ([System.Drawing.FontStyle]::Bold)
$fUnit   = Font "Bahnschrift" 60  ([System.Drawing.FontStyle]::Bold)
$fBrand  = Font "Segoe UI"    40  ([System.Drawing.FontStyle]::Bold)
$fState  = Font "Segoe UI"    21  ([System.Drawing.FontStyle]::Bold)
$fSub    = Font "Segoe UI"    23
$fTag    = Font "Segoe UI"    31
$fChip   = Font "Segoe UI"    19  ([System.Drawing.FontStyle]::Bold)

$bText    = New-Object System.Drawing.SolidBrush($text)
$bDim     = New-Object System.Drawing.SolidBrush($textDim)
$bAmber   = New-Object System.Drawing.SolidBrush($amber)
$bOrange  = New-Object System.Drawing.SolidBrush($orange)

# ---- brand lockup: the same bolt geometry as the app icon, scaled ----
$boltPts = @(
    @(148, 26), @(66, 148), @(122, 148), @(106, 230), @(192, 106), @(132, 106)
)
$boltScale = 0.205; $boltX = 72; $boltY = 46
$poly = $boltPts | ForEach-Object {
    New-Object System.Drawing.PointF(($boltX + $_[0] * $boltScale), ($boltY + $_[1] * $boltScale))
}
$g.FillPolygon($bAmber, [System.Drawing.PointF[]]$poly)
$g.DrawString("PwrMon", $fBrand, $bText, 116, 50)

# ---- hero readout ----
$minus = [char]0x2212      # U+2212, not a hyphen — matches the app's own formatting
$dot   = [char]0x00B7
$g.DrawString("DISCHARGING", $fState, $bOrange, 74, 196)

$heroY = 224
# braces are load-bearing: "$minus51.1" parses as a variable named $minus51
$heroText = "${minus}51.1"
$g.DrawString($heroText, $fNum, $bOrange, 68, $heroY)
$heroW = $g.MeasureString($heroText, $fNum).Width

# Baseline-align "W" to the hero number instead of guessing a pixel offset — DrawString's y
# is the top of the em box, and ascent-to-em ratio differs enough between weights/sizes that
# a fixed offset drifted the unit noticeably below the number's true baseline.
function Get-Baseline([System.Drawing.Font]$font) {
    $family = $font.FontFamily
    $ascentEm = $family.GetCellAscent($font.Style) / $family.GetEmHeight($font.Style)
    return $font.Size * $ascentEm
}
$unitY = $heroY + (Get-Baseline $fNum) - (Get-Baseline $fUnit)
$g.DrawString("W", $fUnit, $bOrange, (68 + $heroW - 18), $unitY)

# numbers are internally consistent, not decorative: 44.5 Wh of a 53.9 Wh pack is 82%, and
# 44.5 Wh at 51.1 W really is ~0:52 left. Someone in this audience will check.
$g.DrawString("82%  $dot  0:52 remaining  $dot  on battery", $fSub, $bDim, 76, 392)

# ---- sparkline: a plausible discharge trace, not a decorative squiggle ----
$chartX = 700; $chartY = 150; $chartW = 508; $chartH = 250
$rand = New-Object System.Random(20260811)   # fixed seed keeps the card reproducible

$series = New-Object System.Collections.Generic.List[double]
$level = 0.46
for ($i = 0; $i -lt 132; $i++) {
    # baseline wander + sensor noise, with two load spikes where the app would show them
    $level += ($rand.NextDouble() - 0.5) * 0.035
    if ($i -eq 44) { $level += 0.30 }
    if ($i -eq 45) { $level += 0.12 }
    if ($i -eq 92) { $level += 0.22 }
    $level = [Math]::Max(0.10, [Math]::Min(0.94, $level * 0.94 + 0.46 * 0.06))
    $noise = ($rand.NextDouble() - 0.5) * 0.05
    $series.Add([Math]::Max(0.06, [Math]::Min(0.97, $level + $noise)))
}

$pts = New-Object System.Collections.Generic.List[System.Drawing.PointF]
for ($i = 0; $i -lt $series.Count; $i++) {
    $x = $chartX + ($i / [double]($series.Count - 1)) * $chartW
    $y = $chartY + $chartH - ($series[$i] * $chartH)
    $pts.Add((New-Object System.Drawing.PointF($x, $y)))
}

# gridlines first, so the trace sits over them
$penGrid = New-Object System.Drawing.Pen($grid, 1)
for ($i = 0; $i -le 4; $i++) {
    $gy = $chartY + ($chartH / 4.0) * $i
    $g.DrawLine($penGrid, $chartX, $gy, ($chartX + $chartW), $gy)
}

# soft fill under the trace
$fillPts = New-Object System.Collections.Generic.List[System.Drawing.PointF]
$fillPts.AddRange($pts)
$fillPts.Add((New-Object System.Drawing.PointF(($chartX + $chartW), ($chartY + $chartH))))
$fillPts.Add((New-Object System.Drawing.PointF($chartX, ($chartY + $chartH))))
$fillBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.PointF($chartX, $chartY)),
    (New-Object System.Drawing.PointF($chartX, ($chartY + $chartH))),
    [System.Drawing.Color]::FromArgb(66, $amber.R, $amber.G, $amber.B),
    [System.Drawing.Color]::FromArgb(0, $amber.R, $amber.G, $amber.B))
$g.FillPolygon($fillBrush, [System.Drawing.PointF[]]$fillPts)

$penTrace = New-Object System.Drawing.Pen($amber, 3.0)
$penTrace.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$g.DrawLines($penTrace, [System.Drawing.PointF[]]$pts)

# live dot on the newest sample
$last = $pts[$pts.Count - 1]
$g.FillEllipse($bAmber, ($last.X - 6), ($last.Y - 6), 12, 12)
$haloBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(56, $amber.R, $amber.G, $amber.B))
$g.FillEllipse($haloBrush, ($last.X - 15), ($last.Y - 15), 30, 30)

# ---- stat row under the chart: fills the right column and shows this reads more than
# ---- the battery. Values are the reference machine's, from the same sample as the hero.
$fStatLabel = Font "Segoe UI" 17 ([System.Drawing.FontStyle]::Bold)
$fStatValue = Font "Bahnschrift" 27 ([System.Drawing.FontStyle]::Bold)
$stats = @(
    @{ Label = "CPU";   Value = "28.1 W" },
    @{ Label = "iGPU";  Value = "0.4 W" },
    @{ Label = "LOAD";  Value = "9%" }
)
$sx = [double]$chartX
foreach ($s in $stats) {
    $g.DrawString($s.Label, $fStatLabel, $bDim, $sx, 426)
    $g.DrawString($s.Value, $fStatValue, $bText, ($sx - 2), 448)
    $sx += 118
}

# ---- accent rule + tagline ----
$g.FillRectangle($bAmber, 74, 452, 116, 5)
# Short, on purpose — the chips below carry the detail, so the tagline doesn't need to repeat
# it (repeating "telemetry" in both the prose and a chip read as a contradiction on a skim,
# not reinforcement). "Computers and laptops" is a little redundant since laptops are
# computers, but it's the literal ask and it costs nothing at this length.
# Auto-fit stays as a safeguard even though this line is short — the previous long version
# silently ran off the right edge of the card, and that's the failure mode worth guarding.
$tagText = "Live power telemetry for Windows computers and laptops"
$tagMaxWidth = $W - 70 - 60
$tagSize = 31.0
do {
    $fTagFit = Font "Segoe UI" $tagSize
    $tagWidth = $g.MeasureString($tagText, $fTagFit).Width
    if ($tagWidth -gt $tagMaxWidth) { $tagSize -= 0.5 }
} while ($tagWidth -gt $tagMaxWidth -and $tagSize -gt 14)
$g.DrawString($tagText, $fTagFit, $bText, 70, 478)

# ---- capability chips ----
# "Admin unlocks extras" is the true claim (verified against MainWindow/HardwareReader): the
# no-admin EMI tier already gives CPU package/cores/iGPU watts; admin + PawnIO *adds* platform
# (PSys) power and CPU temperatures on top. Nothing is gated behind admin, more becomes
# available with it.
#
# The telemetry chip deliberately does NOT say "telemetry" — the word appearing twice with
# opposite meanings (the tagline's "power telemetry" = sensor data the app reads; the chip's
# "no telemetry" = usage data the app would send out) is the actual collision. A qualifier
# ("outside"/"sent"/"collected" telemetry) doesn't fix that, since the eye still catches the
# same word twice on a skim — dropping it avoids the clash outright, and matches the README's
# own claim ("No telemetry, no analytics, no crash reporting") without repeating its noun.
$chips = @("NO KERNEL DRIVER", "ADMIN UNLOCKS EXTRAS", "NO USAGE TRACKING", "OPEN SOURCE")
$cx = 76.0
foreach ($chip in $chips) {
    $tw = $g.MeasureString($chip, $fChip).Width
    $cw = $tw + 26
    $r = 16
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($cx, 546, $r, $r, 180, 90)
    $path.AddArc(($cx + $cw - $r), 546, $r, $r, 270, 90)
    $path.AddArc(($cx + $cw - $r), (546 + 34 - $r), $r, $r, 0, 90)
    $path.AddArc($cx, (546 + 34 - $r), $r, $r, 90, 90)
    $path.CloseFigure()
    $chipBrush = New-Object System.Drawing.SolidBrush($card)
    $chipPen = New-Object System.Drawing.Pen($grid, 1)
    $g.FillPath($chipBrush, $path)
    $g.DrawPath($chipPen, $path)
    $g.DrawString($chip, $fChip, $bDim, ($cx + 12), 553)
    $chipBrush.Dispose(); $chipPen.Dispose(); $path.Dispose()
    $cx += $cw + 12
}

$g.Dispose()
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

$kb = [Math]::Round((Get-Item $outPath).Length / 1KB, 1)
Write-Host "wrote $outPath  (${W}x${H}, $kb KB)"
