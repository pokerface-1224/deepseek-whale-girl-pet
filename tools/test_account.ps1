$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$petRoot = Join-Path $repoRoot 'desktop-pet'
$testRoot = Join-Path $repoRoot ('.inspect/account-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $testRoot 'art') -Force | Out-Null
try {
    Copy-Item -Path (Join-Path $petRoot 'art/*.png') -Destination (Join-Path $testRoot 'art')
    Copy-Item -Path (Join-Path $petRoot '*.dll') -Destination $testRoot
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $testExe = Join-Path $testRoot 'AccountCheck.exe'
    & $compiler /nologo /target:exe /platform:x64 /warn:4 /main:WhalePet.AccountCheck "/out:$testExe" `
        /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll `
        /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
        "/reference:$(Join-Path $petRoot 'Microsoft.Web.WebView2.Core.dll')" `
        "/reference:$(Join-Path $petRoot 'Microsoft.Web.WebView2.WinForms.dll')" `
        (Join-Path $petRoot 'src/Program.cs') (Join-Path $petRoot 'src/Feeding.cs') `
        (Join-Path $petRoot 'src/WhaleMenuRenderer.cs') (Join-Path $petRoot 'src/MiniPanel.cs') `
        (Join-Path $petRoot 'tools/AccountCheck.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Account fixture compilation failed' }
    & node (Join-Path $PSScriptRoot 'test_account.mjs') $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Account pipe/menu regression failed' }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $boundary = [IO.Path]::GetFullPath((Join-Path $repoRoot '.inspect')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
