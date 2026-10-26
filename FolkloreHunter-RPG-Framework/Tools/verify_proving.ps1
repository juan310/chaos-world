# Proving Grounds check in the real player: injected Xbox pad states drive the hunt combat; isolated save folder;
# a SightForge actor track of the session is written next to the results.
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
$exe = (Get-ChildItem "$p\Build-*\*.exe" | Where-Object { $_.Name -notmatch 'Crash' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
$cap = "$p\Captures\Proving"; $saves = "$p\Captures\Proving-saves"
Remove-Item $cap, $saves -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $cap, $saves | Out-Null
$a = Start-Process $exe -ArgumentList "--verify-proving -screen-width 1600 -screen-height 900 -screen-fullscreen 0 --save-dir `"$saves`" --eyes-track `"$cap\proving_track.jsonl`" -logFile `"$p\Logs\verify-proving.log`"" -PassThru -Wait
"PROVING_EXIT=$($a.ExitCode)  ($exe)"
Get-Content "$cap\verification.txt" -ErrorAction SilentlyContinue
Get-Content "$p\Logs\verify-proving.log" | Select-String -Pattern 'Exception|NullReference|FH_PROVING' | Select-Object -First 12 | ForEach-Object { $_.Line }
