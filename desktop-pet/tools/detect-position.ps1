# 位置实测：启动桌宠，比较"启动前/启动后"的屏幕像素差异，算出她的真实包围盒。
#
# 不依赖 MainWindowHandle（透明窗口上它不可靠），用来回答两个问题：
#   1. 她到底有没有被画出来？
#   2. 画在哪个位置、是否和记忆的位置一致？

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'WhalePet.exe'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$screenW = $screen.Width
$screenH = $screen.Height

function Grab([System.Drawing.Rectangle] $rect) {
  $bitmap = New-Object System.Drawing.Bitmap $rect.Width, $rect.Height
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($rect.X, $rect.Y, 0, 0, $rect.Size)
  $graphics.Dispose()
  return $bitmap
}

function SaveShot($bitmap, $path) {
  $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

Write-Host ("work area: {0}x{1}" -f $screenW, $screenH)
Remove-Item (Join-Path $root 'pet-prefs.txt') -ErrorAction SilentlyContinue

Write-Host 'capturing baseline (pet not running)...'
$before = Grab $screen

Write-Host 'launching pet...'
$proc = Start-Process -FilePath $exe -ArgumentList '--smoke=25000' -PassThru
Start-Sleep -Seconds 4

$alive = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
Write-Host ("alive={0}" -f ($alive -ne $null))

$after = Grab $screen
SaveShot $after (Join-Path $env:TEMP 'pet-after.png')

$minX = [int]::MaxValue
$minY = [int]::MaxValue
$maxX = -1
$maxY = -1
$changed = 0

for ($y = 0; $y -lt $screenH; $y++) {
  for ($x = 0; $x -lt $screenW; $x++) {
    $p1 = $before.GetPixel($x, $y)
    $p2 = $after.GetPixel($x, $y)
    $delta = [Math]::Abs($p1.R - $p2.R) + [Math]::Abs($p1.G - $p2.G) + [Math]::Abs($p1.B - $p2.B)
    if ($delta -gt 24) {
      $changed++
      if ($x -lt $minX) { $minX = $x }
      if ($x -gt $maxX) { $maxX = $x }
      if ($y -lt $minY) { $minY = $y }
      if ($y -gt $maxY) { $maxY = $y }
    }
  }
}

if ($changed -gt 0) {
  Write-Host ("CHANGED PIXELS: {0}" -f $changed)
  Write-Host ("BOUNDING BOX  : x={0}..{1} y={2}..{3}  size {4}x{5}" -f $minX, $maxX, $minY, $maxY, ($maxX - $minX + 1), ($maxY - $minY + 1))
} else {
  Write-Host 'CHANGED PIXELS: 0  -> she is NOT drawn on screen'
}

Write-Host 'prefs:'
Get-Content (Join-Path $root 'pet-prefs.txt') -ErrorAction SilentlyContinue | ForEach-Object { Write-Host ('  ' + $_) }

if ($changed -gt 0) {
  $pad = 24
  $cropX = [Math]::Max(0, $minX - $pad)
  $cropY = [Math]::Max(0, $minY - $pad)
  $cropW = [Math]::Min($screenW - 1, $maxX + $pad) - $cropX + 1
  $cropH = [Math]::Min($screenH - 1, $maxY + $pad) - $cropY + 1
  $crop = New-Object System.Drawing.Rectangle $cropX, $cropY, $cropW, $cropH
  SaveShot (Grab $crop) (Join-Path $env:TEMP 'pet-detected.png')
  Write-Host ("saved detected crop: {0}x{1} at {2},{3}" -f $cropW, $cropH, $cropX, $cropY)
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Host 'pet stopped.'
