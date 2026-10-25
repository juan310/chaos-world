# Sends real Windows key and mouse messages to the Folklore Hunter RPG window only (by process id), without
# taking focus or touching any other window on the desktop. Script lines: "key E 0.1", "hold W 1.5", "click x y", "wait 1".
param([int]$ProcessId, [string]$Script)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint c, uint t);
}
"@
$h = (Get-Process -Id $ProcessId).MainWindowHandle
if ($h -eq 0) { throw "No window for $ProcessId" }
$vk = @{ W=0x57; A=0x41; S=0x53; D=0x44; E=0x45; Q=0x51; I=0x49; J=0x4A; K=0x4B; M=0x4D; SPACE=0x20; TAB=0x09; ESC=0x1B; ENTER=0x0D; SHIFT=0x10; UP=0x26; DOWN=0x28; LEFT=0x25; RIGHT=0x27; '1'=0x31 }
function KeyLParam([int]$v, [bool]$up) { $sc = [W]::MapVirtualKey($v, 0); $l = 1 -bor ($sc -shl 16); if ($up) { $l = $l -bor (3 -shl 30) }; return [IntPtr]$l }
function Down($k) { $v = $vk[$k]; [W]::PostMessage($h, 0x100, [IntPtr]$v, (KeyLParam $v $false)) | Out-Null }
function Up($k) { $v = $vk[$k]; [W]::PostMessage($h, 0x101, [IntPtr]$v, (KeyLParam $v $true)) | Out-Null }
foreach ($line in ($Script -split ';')) {
  $p = $line.Trim() -split '\s+'
  switch ($p[0]) {
    'key'   { Down $p[1]; Start-Sleep -Milliseconds ([int]([double]$p[2] * 1000)); Up $p[1] }
    'hold'  { $t = [DateTime]::Now.AddSeconds([double]$p[2]); while ([DateTime]::Now -lt $t) { Down $p[1]; Start-Sleep -Milliseconds 30 }; Up $p[1] }
    'click' { $l = [IntPtr](([int]$p[2] -shl 16) -bor [int]$p[1]); [W]::PostMessage($h, 0x200, [IntPtr]0, $l) | Out-Null; Start-Sleep -Milliseconds 60; [W]::PostMessage($h, 0x201, [IntPtr]1, $l) | Out-Null; Start-Sleep -Milliseconds 80; [W]::PostMessage($h, 0x202, [IntPtr]0, $l) | Out-Null }
    'wait'  { Start-Sleep -Milliseconds ([int]([double]$p[1] * 1000)) }
  }
  Start-Sleep -Milliseconds 120
}
"SENT"
