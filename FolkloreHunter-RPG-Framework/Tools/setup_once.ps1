# One-time restructuring of the isolated copy (run before the first sync). Safe to re-run.
$ErrorActionPreference = 'Stop'
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
$prov = "$p\Docs\Provenance\SampleScripts"
New-Item -ItemType Directory -Force $prov | Out-Null
# Sample runtime scripts are replaced by the framework; keep them for reference outside Assets.
Get-ChildItem "$p\Assets\Scripts\FH3*.cs" -ErrorAction SilentlyContinue | ForEach-Object { Move-Item $_.FullName "$prov\$($_.Name)" -Force; Remove-Item "$($_.FullName).meta" -Force -ErrorAction SilentlyContinue }
foreach ($f in 'FH3Build.cs','FH3Inspect.cs') { if (Test-Path "$p\Assets\Editor\$f") { Move-Item "$p\Assets\Editor\$f" "$prov\$f" -Force; Remove-Item "$p\Assets\Editor\$f.meta" -Force -ErrorAction SilentlyContinue } }
# The sample scene and its generated assets referenced the old scripts; the original sample still has them.
Remove-Item "$p\Assets\Scenes\CottageValley3D.unity","$p\Assets\Scenes\CottageValley3D.unity.meta" -Force -ErrorAction SilentlyContinue
Get-ChildItem "$p\Assets\Generated" -File -ErrorAction SilentlyContinue | Remove-Item -Force
# The 2D werewolf comparison frames stay in the project but out of Resources, so they are not shipped as a monster.
New-Item -ItemType Directory -Force "$p\Assets\ArtReference" | Out-Null
if (Test-Path "$p\Assets\Resources\Werewolf") { Move-Item "$p\Assets\Resources\Werewolf" "$p\Assets\ArtReference\Werewolf" -Force; Move-Item "$p\Assets\Resources\Werewolf.meta" "$p\Assets\ArtReference\Werewolf.meta" -Force }
# Built-in packages bundled with the editor (no download): uGUI and the Test Framework.
$m = Get-Content "$p\Packages\manifest.json" -Raw | ConvertFrom-Json
$m.dependencies | Add-Member -NotePropertyName 'com.unity.ugui' -NotePropertyValue '2.6.0' -Force
$m.dependencies | Add-Member -NotePropertyName 'com.unity.test-framework' -NotePropertyValue '1.8.0' -Force
$json = $m | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText("$p\Packages\manifest.json", $json, (New-Object System.Text.UTF8Encoding($false)))
'SETUP_DONE'
