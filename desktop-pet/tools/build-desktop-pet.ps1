# Build the native pet using the Windows .NET Framework compiler.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src\Program.cs'
$output = Join-Path $root 'WhalePet.exe'
$csc = @(
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw '.NET Framework v4 compiler was not found.' }
& $csc /nologo /target:winexe /platform:x64 /optimize+ /warn:4 `
  "/out:$output" /reference:System.dll /reference:System.Core.dll `
  /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  "/reference:$(Join-Path $root 'Microsoft.Web.WebView2.Core.dll')" `
  "/reference:$(Join-Path $root 'Microsoft.Web.WebView2.WinForms.dll')" `
  $source (Join-Path $root 'src\WhaleMenuRenderer.cs') (Join-Path $root 'src\MiniPanel.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Write-Host "Built $output"
