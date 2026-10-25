# Runs the in-player verification: phase 1 plays and saves, phase 2 is a fresh launch that continues the save.
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
$exe = "$p\Build-RPG\FolkloreHunterRPG.exe"
$cap = "$p\Captures\RPG"
$saves = "$p\Captures\RPG-test-saves"
Remove-Item $cap, $saves -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $cap, $saves | Out-Null
$common = "-screen-width 1600 -screen-height 900 -screen-fullscreen 0 --save-dir `"$saves`""
$a = Start-Process $exe -ArgumentList "--verify-rpg $common --eyes-track `"$cap\verify_track.jsonl`" -logFile `"$p\Logs\verify-phase1.log`"" -PassThru -Wait
"PHASE1_EXIT=$($a.ExitCode)"
$b = Start-Process $exe -ArgumentList "--verify-rpg --verify-continue $common -logFile `"$p\Logs\verify-phase2.log`"" -PassThru -Wait
"PHASE2_EXIT=$($b.ExitCode)"
Get-Content "$cap\verification.txt" -ErrorAction SilentlyContinue
'---- continue ----'
Get-Content "$cap\verification-continue.txt" -ErrorAction SilentlyContinue
'---- log errors ----'
foreach ($l in 'verify-phase1','verify-phase2') { Get-Content "$p\Logs\$l.log" | Select-String -Pattern 'Exception|NullReference|FH_VERIFY' | Select-Object -First 12 | ForEach-Object { "${l}: " + $_.Line } }
