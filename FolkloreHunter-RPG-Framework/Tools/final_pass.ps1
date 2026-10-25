# Final milestone pass on one build: build, tests, two-phase verification, SightForge, gameplay video, real input.
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
powershell -ExecutionPolicy Bypass -File "$p\Tools\sync.ps1"
powershell -ExecutionPolicy Bypass -File "$p\Tools\unity.ps1" FHBuild.Build build | Select-String 'FH_BUILD|UNITY_EXIT|error'
powershell -ExecutionPolicy Bypass -File "$p\Tools\unity.ps1" tests tests | Out-Null
[xml]$x = Get-Content "$p\Docs\unity-test-results.xml"; "TESTS total=$($x.'test-run'.total) passed=$($x.'test-run'.passed) failed=$($x.'test-run'.failed)"
powershell -ExecutionPolicy Bypass -File "$p\Tools\verify.ps1" | Select-String -Pattern 'FAIL |FH_VERIFY'
python "$p\Tools\review_sightforge.py" 2>&1 | Select-String 'SIGHTFORGE'
powershell -ExecutionPolicy Bypass -File "$p\Tools\record_tour.ps1" | Select-String 'TOUR_EXIT|frames'
ffmpeg -y -loglevel error -i "$p\Folklore-Hunter-RPG-Framework-Gameplay.mp4" -vf "fps=1/3,scale=400:-1,tile=5x7" -frames:v 1 "$p\Captures\tour-contact-sheet.png"
# Real Windows key messages to this game's window only.
$s = "$p\Captures\RealInput-saves"; Remove-Item $s -Recurse -Force -ErrorAction SilentlyContinue
$g = Start-Process "$p\Build-RPG\FolkloreHunterRPG.exe" -ArgumentList "--input-log --save-dir `"$s`" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile `"$p\Logs\realinput.log`"" -PassThru
Start-Sleep 9
powershell -ExecutionPolicy Bypass -File "$p\Tools\send_input.ps1" -ProcessId $g.Id -Script 'key DOWN 0.08; key UP 0.08; key ENTER 0.08; wait 0.8; key ENTER 0.08; wait 2; hold W 1.5; key SPACE 0.08; wait 0.6; key TAB 0.08; wait 0.3; key E 0.1; wait 0.5; key ESC 0.1; wait 0.4; key I 0.1; wait 1; key RIGHT 0.08; key ENTER 0.08; wait 0.8; key ESC 0.1; wait 0.5; key M 0.1; wait 1; key M 0.1; wait 0.5; key ESC 0.1; wait 0.8; key ESC 0.1; wait 0.5' | Out-Null
Start-Sleep 1; Stop-Process -Id $g.Id
Copy-Item "$p\Captures\input-log.txt" "$p\Docs\real-input-session.txt" -Force
Get-Content "$p\Docs\real-input-session.txt"
'FINAL_PASS_DONE'
