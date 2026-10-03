param([switch]$Recycle, [string]$ArtifactRoot)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$petRoot = Join-Path $repoRoot 'desktop-pet'
if (-not $ArtifactRoot) {
    $ArtifactRoot = if ($Recycle) { [IO.Path]::GetTempPath() } else { Join-Path $repoRoot '.inspect' }
}
$testRoot = Join-Path $ArtifactRoot ('feeding-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $testRoot 'art') -Force | Out-Null
Copy-Item -Path (Join-Path $petRoot 'art/*.png') -Destination (Join-Path $testRoot 'art')
Copy-Item -Path (Join-Path $petRoot '*.dll') -Destination $testRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
}
$testExe = Join-Path $testRoot 'FeedingCheck.exe'
& $compiler /nologo /target:exe /platform:x64 /warn:4 /main:WhalePet.FeedingCheck "/out:$testExe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    "/reference:$(Join-Path $petRoot 'Microsoft.Web.WebView2.Core.dll')" `
    "/reference:$(Join-Path $petRoot 'Microsoft.Web.WebView2.WinForms.dll')" `
    (Join-Path $petRoot 'src/Program.cs') (Join-Path $petRoot 'src/Feeding.cs') `
    (Join-Path $petRoot 'src/WhaleMenuRenderer.cs') (Join-Path $petRoot 'src/MiniPanel.cs') `
    (Join-Path $petRoot 'tools/FeedingCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Feeding test compilation failed' }
if ($Recycle) { & $testExe --recycle } else { & $testExe }
if ($LASTEXITCODE -ne 0) { throw "Feeding tests failed. Artifacts: $testRoot" }
if ($Recycle) {
    # Restore only the uniquely named fixture created by this test, never any other bin item.
    $shell = New-Object -ComObject Shell.Application
    $bin = $shell.Namespace(10)
    foreach ($original in [IO.File]::ReadAllLines((Join-Path $testRoot 'recycled-paths.txt'))) {
        if (-not $original.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Unexpected integration fixture path'
        }
        $matching = @($bin.Items() | Where-Object {
            $_.ExtendedProperty('System.Recycle.DeletedFrom') -eq [IO.Path]::GetDirectoryName($original) -and
            $_.Name -like ([IO.Path]::GetFileNameWithoutExtension($original) + '*')
        })
        if ($matching.Count -ne 1) { throw 'Recycled fixture was not uniquely found in the Recycle Bin' }
        $shell.Namespace([IO.Path]::GetDirectoryName($original)).MoveHere($matching[0], 20)
        for ($i = 0; $i -lt 50 -and -not (Test-Path -LiteralPath $original); $i++) { Start-Sleep -Milliseconds 100 }
        if (-not (Test-Path -LiteralPath $original) -or
            [IO.File]::ReadAllText($original) -ne 'whale feeding integration fixture') {
            throw 'Recycle Bin restore/content verification failed'
        }
        Write-Host 'PASS: actual Recycle Bin item restored with original content'
    }
}
Write-Host "Artifacts: $testRoot"
