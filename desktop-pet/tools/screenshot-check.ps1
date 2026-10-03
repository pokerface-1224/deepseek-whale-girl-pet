# 截屏验证：启动桌宠，截取她应该在的屏幕区域，存成 PNG。
#
# 这样可以不受"进程句柄是否可枚举"的干扰，直接判断她有没有被画出来。

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'WhalePet.exe'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

function Shot($path, $bounds, $label) {
  $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bounds.Size)
  $graphics.Dispose()
  $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bitmap.Dispose()
  Write-Host ("  {0} -> {1} ({2}x{3} at {4},{5})" -f $label, (Split-Path -Leaf $path), $bounds.Width, $bounds.Height, $bounds.X, $bounds.Y)
}

$area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
Write-Host ("work area: {0}x{1} at {2},{3}" -f $area.Width, $area.Height, $area.X, $area.Y)

# 意图位置：右下角
$expected = New-Object System.Drawing.Rectangle ($area.Right - 154 - 32), ($area.Bottom - 200 - 24), 154, 200

Remove-Item (Join-Path $root 'pet-prefs.txt') -ErrorAction SilentlyContinue
Write-Host 'launching pet...'
$proc = Start-Process -FilePath $exe -ArgumentList '--smoke=20000' -PassThru
Start-Sleep -Seconds 3

$alive = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
Write-Host ("alive={0}" -f ($alive -ne $null))

Shot (Join-Path $env:TEMP 'pet-shot-region.png') $expected 'expected pet region'
Shot (Join-Path $env:TEMP 'pet-shot-full.png') $area 'full work area'

Write-Host 'prefs after launch:'
Get-Content (Join-Path $root 'pet-prefs.txt') -ErrorAction SilentlyContinue | ForEach-Object { Write-Host ('  ' + $_) }

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Host 'pet stopped.'
