param([int]$PetId, [ValidateSet('hide', 'visible')][string]$Mode)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Runtime.InteropServices;
public class PetTestWindow {
 public delegate bool Callback(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback f, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 public static IntPtr Find(uint pid) {
  IntPtr found = IntPtr.Zero;
  EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h,out p);
   if(p == pid && IsWindowVisible(h)) { found=h; return false; } return true;
  }, IntPtr.Zero);
  return found;
 }
}
'@
$handle = [PetTestWindow]::Find($PetId)
if ($handle -eq [IntPtr]::Zero) { throw 'No visible test window' }
if ($Mode -eq 'hide') {
    [void][PetTestWindow]::ShowWindowAsync($handle, 0)
    Start-Sleep -Milliseconds 150
    if ([PetTestWindow]::IsWindowVisible($handle)) { throw 'Window did not hide' }
}
