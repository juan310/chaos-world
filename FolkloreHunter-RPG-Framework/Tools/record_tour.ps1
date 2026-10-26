# Records the gameplay video: in-engine frames at a fixed 30 fps (UI included), encoded with the installed ffmpeg.
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
$saves = "$p\Captures\Tour-saves"
Remove-Item $saves -Recurse -Force -ErrorAction SilentlyContinue
$g = Start-Process "$p\Build-Proving\FolkloreHunterProving.exe" -ArgumentList "--tour-rpg --save-dir `"$saves`" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile `"$p\Logs\tour.log`"" -PassThru -Wait
"TOUR_EXIT=$($g.ExitCode)"
$ff = (Get-Command ffmpeg).Source
& $ff -y -loglevel error -framerate 30 -i "$p\Captures\Tour\f%05d.jpg" -c:v libx264 -pix_fmt yuv420p -crf 20 -movflags +faststart "$p\Folklore-Hunter-RPG-Framework-Gameplay.mp4"
Get-Item "$p\Folklore-Hunter-RPG-Framework-Gameplay.mp4" | Select-Object Name, Length
Get-Content "$p\Captures\Tour\receipt.json"
