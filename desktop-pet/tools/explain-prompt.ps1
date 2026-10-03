# Diagnose why Windows shows a permission prompt for the pet executable.
#
# ASCII ONLY. Windows PowerShell 5.1 (powershell.exe) reads .ps1 files with the
# ANSI code page, so a UTF-8 script with Chinese comments is parsed as garbage.
# Keep every .ps1 in this project ASCII-only.

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'WhalePet.exe'

function Line($t) { Write-Host $t }

Line '== 1. executable =='
if (Test-Path $exe) {
  $item = Get-Item $exe
  Line ('   path      : ' + $item.FullName)
  Line ('   size      : ' + $item.Length + ' bytes')
  Line ('   created   : ' + $item.CreationTime)
  Line ('   write time: ' + $item.LastWriteTime)
} else {
  Line '   MISSING'
}

Line ''
Line '== 2. requested execution level (manifest) =='
$bytes = [System.IO.File]::ReadAllBytes($exe)
$text = [System.Text.Encoding]::ASCII.GetString($bytes)
if ($text -match 'requireAdministrator') {
  Line '   FOUND requireAdministrator -> this is why UAC prompts every launch'
} elseif ($text -match 'highestAvailable') {
  Line '   FOUND highestAvailable -> prompts UAC for admin accounts'
} elseif ($text -match 'asInvoker') {
  Line '   asInvoker (normal; must not prompt UAC)'
} else {
  Line '   no manifest entry -> defaults to asInvoker (must not prompt UAC)'
}

Line ''
Line '== 3. mark of the web =='
$zone = Get-Content -LiteralPath ($exe + ':Zone.Identifier')
if ($zone) { $zone | ForEach-Object { Line ('   ' + $_) } } else { Line '   none (not downloaded; SmartScreen should not block)' }

Line ''
Line '== 4. signature =='
$sig = Get-AuthenticodeSignature $exe
Line ('   status: ' + $sig.Status)
Line ('   signer: ' + $sig.SignerCertificate.Subject)

Line ''
Line '== 5. elevation / UAC policy =='
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
Line ('   session is admin : ' + $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
Line ('   user             : ' + $identity.Name)
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name EnableLUA, ConsentPromptBehaviorAdmin, PromptOnSecureDesktop |
  ForEach-Object {
    Line ('   EnableLUA=' + $_.EnableLUA + '  ConsentPromptBehaviorAdmin=' + $_.ConsentPromptBehaviorAdmin + '  PromptOnSecureDesktop=' + $_.PromptOnSecureDesktop)
  }

Line ''
Line '== 6. defender controlled folder access =='
Line ('   EnableControlledFolderAccess = ' + (Get-MpPreference).EnableControlledFolderAccess + '  (0=off 1=on 2=audit)')

Line ''
Line '== 7. can this folder be written (preferences file) =='
$probe = Join-Path $root 'write-probe.tmp'
try {
  [System.IO.File]::WriteAllText($probe, 'x')
  Line '   writable: yes'
  Remove-Item $probe -Force
} catch {
  Line ('   writable: NO -> ' + $_.Exception.Message)
}
