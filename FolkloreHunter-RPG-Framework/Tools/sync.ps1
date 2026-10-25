# Pulls the framework source from git and mirrors it into the Unity project (Unity .meta files are kept).
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
$src = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework-src'
if (-not (Test-Path $src)) { git clone -q -b claude/new-session-xoe3g1 https://github.com/juan310/chaos-world $src }
git -C $src fetch -q origin claude/new-session-xoe3g1
git -C $src reset -q --hard origin/claude/new-session-xoe3g1
$r = "$src\FolkloreHunter-RPG-Framework"
robocopy "$r\Assets\Framework" "$p\Assets\Framework" /MIR /XF *.meta /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy "$r\Assets\Resources\Data" "$p\Assets\Resources\Data" /MIR /XF *.meta /NFL /NDL /NJH /NJS /NP | Out-Null
foreach ($d in 'Docs','Tools') { if (Test-Path "$r\$d") { robocopy "$r\$d" "$p\$d" /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null } }
foreach ($f in 'AGENTS.md','README.txt') { if (Test-Path "$r\$f") { Copy-Item "$r\$f" "$p\$f" -Force } }
'SYNCED ' + (git -C $src log -1 --format='%h %s')
