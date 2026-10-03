# 桌宠自检：启动、观察窗口、记录现场
#
# 打印每个阶段的事实（进程是否存活、主窗口句柄、异常信息），
# 便于判断"没出现"是崩了、还是画出来了但看不见。

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'WhalePet.exe'

function Line($text) { Write-Host $text }

Line ('== exe ==')
Line ('  ' + $exe + '  exists=' + (Test-Path $exe))

$out = Join-Path $env:TEMP 'whalepet-selfcheck-out.txt'
$err = Join-Path $env:TEMP 'whalepet-selfcheck-err.txt'
Remove-Item $out, $err -ErrorAction SilentlyContinue

Line ('== launch (--smoke=8000) ==')
$proc = Start-Process -FilePath $exe -ArgumentList '--smoke=8000' -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
Line ("  pid=" + $proc.Id)

for ($i = 1; $i -le 4; $i++) {
  Start-Sleep -Seconds 2
  $alive = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
  if ($alive) {
    Line ("  t+" + ($i * 2) + "s alive=true hwnd=" + $alive.MainWindowHandle + " title='" + $alive.MainWindowTitle + "' memMB=" + [math]::Round($alive.WorkingSet64 / 1MB, 1))
  } else {
    Line ("  t+" + ($i * 2) + "s alive=false exited=" + $proc.HasExited + " code=" + $proc.ExitCode)
    break
  }
}

Start-Sleep -Seconds 4
Line ('== after smoke window ==')
Line ('  still running: ' + ((Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) -ne $null))

Line ('== stdout ==')
Get-Content $out -ErrorAction SilentlyContinue | ForEach-Object { Line ('  ' + $_) }
Line ('== stderr ==')
Get-Content $err -ErrorAction SilentlyContinue | ForEach-Object { Line ('  ' + $_) }

Line ('== prefs ==')
$prefs = Join-Path $root 'pet-prefs.txt'
if (Test-Path $prefs) { Get-Content $prefs | ForEach-Object { Line ('  ' + $_) } } else { Line '  (none)' }

Line ('== windows event log (application, last 5 min) ==')
Get-WinEvent -LogName Application -MaxEvents 40 -ErrorAction SilentlyContinue |
  Where-Object { $_.TimeCreated -gt (Get-Date).AddMinutes(-5) -and $_.Message -like '*WhalePet*' } |
  Select-Object -First 5 |
  ForEach-Object { Line ('  ' + $_.TimeCreated + ' ' + $_.LevelDisplayName + ': ' + ($_.Message -replace '\s+', ' ').Substring(0, [Math]::Min(200, $_.Message.Length))) }
