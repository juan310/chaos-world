param([switch]$Force)
# Pulls the framework source from git and mirrors it into the Unity project (Unity .meta files are kept).
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
$src = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework-src'
if (-not (Test-Path $src)) { git clone -q -b claude/new-session-xoe3g1 https://github.com/juan310/chaos-world $src }
$r = "$src\FolkloreHunter-RPG-Framework"
# Never overwrite someone else's work: another session has edited this clone and this project before.
$dirty = git -C $src status --porcelain
if ($dirty) { 'SYNC REFUSED: uncommitted changes in ' + $src + ' (commit or back them up first):'; $dirty; exit 1 }
git -C $src fetch -q origin claude/new-session-xoe3g1
# A project file may be replaced only if it equals the last synced commit or the commit being pulled.
$known = @{}
foreach ($rev in 'HEAD', 'origin/claude/new-session-xoe3g1') {
    git -C $src ls-tree -r $rev -- FolkloreHunter-RPG-Framework/Assets/Framework FolkloreHunter-RPG-Framework/Assets/Resources/Data | ForEach-Object { $f = $_ -split '\s+', 4; $known[$f[3] + '|' + $f[2]] = 1 }
}
$edited = foreach ($d in 'Assets\Framework', 'Assets\Resources\Data') {
    if (-not (Test-Path "$p\$d")) { continue }
    Get-ChildItem "$p\$d" -Recurse -File | Where-Object { $_.Extension -ne '.meta' } | ForEach-Object {
        $repoPath = 'FolkloreHunter-RPG-Framework/' + $_.FullName.Substring($p.Length + 1).Replace('\', '/')
        $h = git -C $src hash-object --path=$repoPath $_.FullName
        if (-not $known[$repoPath + '|' + $h]) { $_.FullName }
    }
}
if ($edited -and -not $Force) { 'SYNC REFUSED: these project files are not in git (edited outside it); back them up or commit them, then rerun (-Force only after that):'; $edited; exit 1 }
git -C $src reset -q --hard origin/claude/new-session-xoe3g1
robocopy "$r\Assets\Framework" "$p\Assets\Framework" /MIR /XF *.meta /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy "$r\Assets\Resources\Data" "$p\Assets\Resources\Data" /MIR /XF *.meta /NFL /NDL /NJH /NJS /NP | Out-Null
# Art made in git for 0.9 (lair textures, hairstyle portraits): copied in, nothing in the project is deleted.
foreach ($d in 'Assets\Resources\Environment','Assets\Resources\UI') { if (Test-Path "$r\$d") { robocopy "$r\$d" "$p\$d" /E /XF *.meta /NFL /NDL /NJH /NJS /NP | Out-Null } }
foreach ($d in 'Docs','Tools') { if (Test-Path "$r\$d") { robocopy "$r\$d" "$p\$d" /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null } }
foreach ($f in 'AGENTS.md','README.txt') { if (Test-Path "$r\$f") { Copy-Item "$r\$f" "$p\$f" -Force } }
'SYNCED ' + (git -C $src log -1 --format='%h %s')
