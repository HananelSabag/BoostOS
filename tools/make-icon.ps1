# Draws the BoostOS icon (a boost gauge on arena navy) and writes app.ico with 256/48/32/16 px images.
param([string]$Out = (Join-Path $PSScriptRoot '..\src\BoostOS\app.ico'))
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$s) {
  $bmp = New-Object Drawing.Bitmap $s, $s
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([Drawing.Color]::Transparent)

  $r = [Math]::Max(2, [int]($s * 0.22))
  $path = New-Object Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90); $path.AddArc($s - $r * 2 - 1, 0, $r * 2, $r * 2, 270, 90)
  $path.AddArc($s - $r * 2 - 1, $s - $r * 2 - 1, $r * 2, $r * 2, 0, 90); $path.AddArc(0, $s - $r * 2 - 1, $r * 2, $r * 2, 90, 90)
  $path.CloseFigure()
  $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(11, 24, 56))), $path)

  $w = [single]($s * 0.13)
  $m = [single]($s * 0.2)
  $box = New-Object Drawing.RectangleF $m, $m, ($s - 2 * $m), ($s - 2 * $m)
  $track = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(40, 255, 255, 255)), $w
  $g.DrawEllipse($track, $box)
  $grad = New-Object Drawing.Drawing2D.LinearGradientBrush $box, ([Drawing.Color]::FromArgb(255, 122, 26)), ([Drawing.Color]::FromArgb(255, 210, 63)), 45
  $pen = New-Object Drawing.Pen $grad, $w
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
  $g.DrawArc($pen, $box, -90, 290)

  $dot = [single]($s * 0.16)
  $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(61, 139, 255))), ($s - $dot) / 2, ($s - $dot) / 2, $dot, $dot)
  $g.Dispose()

  $ms = New-Object IO.MemoryStream
  $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return , $ms.ToArray()
}

$sizes = 256, 48, 32, 16
$frames = $sizes | ForEach-Object { , (New-Frame $_) }
$fs = [IO.File]::Create($Out)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
  $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$frames[$i].Length); $bw.Write([uint32]$offset)
  $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Dispose()
"wrote $Out"
