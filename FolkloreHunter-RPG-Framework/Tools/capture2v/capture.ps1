# Two-view capture on this PC (local, free): fetch the tool from git, then run a step.
# Kept in git; the copy that runs lives at C:\AW\capture2v\capture.ps1 (runs in C:\AW\capture2v\runs\<creature>).
#   capture.ps1 prep    -creature troll                    model -> rig_info.json, rigged.blend, reference views
#   capture.ps1 film    -creature troll -move roar -action "..." -seeds 1,2   Wan 2.2 clip(s)
#   capture.ps1 dataset -creature troll [-count 2200]      random poses rendered from both cameras, joints known
#   capture.ps1 train   -creature troll [-epochs 20]       the joint reader (DINOv2) for this creature
#   capture.ps1 read    -creature troll -move roar [-seeds 1]   joints read in both views of the clip -> det.npz, overlay.mp4
#   capture.ps1 solve   -creature troll -move roar [-seeds 1]   the skeleton fitted to them -> motion.json
param([string]$step, [string]$creature = 'troll', [string]$move = '', [string]$action = '', [string[]]$seeds = @('1'),
      [int]$frames = 97, [int]$count = 2200, [int]$epochs = 20, [int]$batch = 12)
$seeds = $seeds -join ','
$src = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework-src'
$code = 'C:\AW\capture2v\code'; $runs = 'C:\AW\capture2v\runs'
$proj = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
git -C $src fetch -q origin claude/new-session-xoe3g1
if (Test-Path $code) { Remove-Item $code -Recurse -Force }
New-Item -ItemType Directory -Force $code | Out-Null
git -C $src archive -o "$code\c.zip" origin/claude/new-session-xoe3g1 FolkloreHunter-RPG-Framework/Tools/capture2v; Expand-Archive "$code\c.zip" -DestinationPath $code -Force
$tool = "$code\FolkloreHunter-RPG-Framework\Tools\capture2v"
$cdir = "$tool\creatures\$creature"
$run = "$runs\$creature"; New-Item -ItemType Directory -Force $run | Out-Null
$env:MOCAP_CREATURE = $cdir
$env:PYTHONPATH = "$tool\anymove"
$c = Get-Content "$cdir\creature.json" -Raw | ConvertFrom-Json
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$py = 'C:\AW\forge3d\venv\Scripts\python.exe'
$filter = 'CAPTURE|Error|Traceback|line \d|loading|generating|epoch|val mean|sample [0-9]*00 |frames at|calibration|sync|reprojection|saved|paw|swaps'
switch ($step) {
  'prep'    { & $blender -b --python "$tool\prep_blender.py" -- "$proj\$($c.model)" $run 2>&1 | Select-String $filter | % Line }
  'film'    { & $py "$tool\film.py" $cdir $run $move $action --seeds $seeds --frames $frames 2>&1 | Select-String $filter | % Line }
  'dataset' { & $blender -b --python "$tool\anymove\blender\render_dataset.py" -- "$run\rigged.blend" "$run\rig_info.json" "$run\dataset" 0 $count 7 0.6 2>&1 | Select-String $filter | % Line }
  'train'   { Push-Location "$tool\anymove"; & $py -m mocap.train "$run\dataset" "$run\rig_info.json" "$run\reader.pt" $epochs $batch 2>&1 | Select-String $filter | % Line; Pop-Location }
  'read'    { Push-Location "$tool\anymove"; foreach ($s in $seeds.Split(',')) { $v = "$run\videos\$move\s$s"
              & $py -m mocap.detect "$v\clip.mp4" "$run\reader.pt" "$run\ref\ref.png" "$v\det.npz" "$v\overlay.mp4" 2>&1 | Select-String $filter | % Line }; Pop-Location }
  'solve'   { Push-Location "$tool\anymove"; foreach ($s in $seeds.Split(',')) { $v = "$run\videos\$move\s$s"
              & $py -m mocap.solve "$v\det.npz" "$run\rig_info.json" "$v\motion.json" 2>&1 | Select-String $filter | % Line }; Pop-Location }
}
"== done $step"
