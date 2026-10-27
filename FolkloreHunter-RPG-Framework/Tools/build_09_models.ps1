# Builds the 0.9 game models from finished Forge jobs (local GPU results) with Blender:
#   hunters      Hunter_Male / Hunter_Female (the main character sheet) on the KayKit hunter skeleton, every clip
#   hair         Hunter_Male_NN / Hunter_Female_NN from the hairstyle model jobs (HairMNN / HairFNN)
#   monsters     Gravebound_Warrior / _Knight / _Mage and GraveWarden on the KayKit skeleton rig, weapons held rigidly
#   props        the lair's static props (skull pedestal, sarcophagus, knight statue, candelabra) and the banner picture
# The older models (Hunter.fbx, Gravebound.fbx) are never touched: the game falls back to them.
param([string]$Stage = 'all')
$b = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
$r = "C:\Users\juana\Documents\FolkloreHunter-RPG-Framework"
$F = "$env:LOCALAPPDATA\ClaudeEyesStudio\Forge"
$log = "C:\AW\briarhound_work\models09"; New-Item -ItemType Directory -Force $log | Out-Null
function Latest($pattern) { (Get-ChildItem $F -Directory -Filter $pattern | Where-Object { (Get-Content "$($_.FullName)\status.json" -Raw | ConvertFrom-Json).state -eq 'done' } | Sort-Object Name | Select-Object -Last 1).FullName }
function Sides($name) {
    # the profile pictures for a model (edit jobs <name>Left / <name>Right), as the builder's --sides argument
    $l = Latest "edit_${name}Left_*"; $r2 = Latest "edit_${name}Right_*"
    if ($l -and $r2 -and (Test-Path "$l\edit_1.png") -and (Test-Path "$r2\edit_1.png")) { return @('--sides', "$l\edit_1.png|$r2\edit_1.png") }
    return @()
}
function Blend($base, $argList, $name) {
    $p = Start-Process $b -ArgumentList (@('-b', "$r\SourceArt\$base", '--python', "$r\Tools\build_hunter_variant.py", '--') + $argList) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput "$log\$name.log" -RedirectStandardError "$log\$name.err"
    $line = Select-String -Path "$log\$name.log" -Pattern 'HUNTER_VARIANT' | Select-Object -Last 1
    if ($line) { "BUILT $name $($line.Line.Substring(15))" } else { "FAILED $name (see $log\$name.err)" }
}
if ($Stage -in 'all', 'hunters') {
    Blend 'Hunter.blend' (@((Latest 'model_HunterMale2_*'), 'Male') + (Sides 'HunterMale')) 'Hunter_Male'
    Blend 'Hunter.blend' (@((Latest 'model_HunterFemale2_*'), 'Female') + (Sides 'HunterFemale')) 'Hunter_Female'
}
if ($Stage -in 'all', 'hair') {
    foreach ($g in 'M', 'F') { for ($i = 1; $i -le 10; $i++) {
        $n = '{0:00}' -f $i; $job = Latest "model_Hair$g${n}_*"
        if ($job) { Blend 'Hunter.blend' (@($job, ($(if ($g -eq 'M') { 'Male' } else { 'Female' }) + "_$n")) + (Sides "Hair$g$n")) "Hunter_${g}_$n" } else { "MISSING hair $g$n" }
    } }
}
if ($Stage -in 'all', 'monsters') {
    $sw = Latest 'static_WarriorSword_*'; $ss = Latest 'static_WarriorShield_*'; $kg = Latest 'static_KnightGreatsword_*'
    $ms = Latest 'static_MageStaff_*'; $wg = Latest 'static_WardenGreatsword_*'
    Blend 'Gravebound.blend' ((Sides 'SkeletonWarrior') + @((Latest 'model_SkeletonWarriorB_*'), 'Gravebound_Warrior', '1', '--monster', '--prop', "$sw|handslot.r|blade|0.44|0.07|Sword", '--prop', "$ss|handslot.l|shield|0.34|0.5|Shield")) 'Gravebound_Warrior'
    Blend 'Gravebound.blend' ((Sides 'SkeletonKnight') + @((Latest 'model_SkeletonKnightB_*'), 'Gravebound_Knight', '1', '--monster', '--prop', "$kg|handslot.r|blade|0.64|0.1|GreatSword")) 'Gravebound_Knight'
    Blend 'Gravebound.blend' ((Sides 'SkeletonMage') + @((Latest 'model_SkeletonMageB_*'), 'Gravebound_Mage', '1', '--monster', '--prop', "$ms|handslot.r|staff|1.02|0.42|Staff")) 'Gravebound_Mage'
    Blend 'Gravebound.blend' ((Sides 'GraveWarden') + @((Latest 'model_GraveWardenB_*'), 'GraveWarden', '1', '--monster', '--prop', "$wg|handslot.r|blade|0.6|0.08|GreatSword")) 'GraveWarden'
}
if ($Stage -in 'all', 'props') {
    $dst = "$r\Assets\Resources\Models\Lair"; New-Item -ItemType Directory -Force $dst | Out-Null
    foreach ($n in 'SkullPedestal', 'Sarcophagus', 'KnightStatue', 'Candelabra') {
        $job = Latest "static_${n}_*"
        if (-not $job) { "MISSING prop $n"; continue }
        $fbx = Get-ChildItem $job -Filter *.fbx | Select-Object -First 1
        $alb = Get-ChildItem $job -Filter *_albedo.png | Select-Object -First 1
        if (-not $alb) { $alb = Get-Item "$job\texture.png" -ErrorAction SilentlyContinue }
        Copy-Item $fbx.FullName "$dst\$n.fbx" -Force
        if ($alb) { Copy-Item $alb.FullName "$dst\${n}_texture.png" -Force }
        "PROP $n $($fbx.Name) $($alb.Name)"
    }
    if (Test-Path C:\AW\briarhound_work\prep\banner_art.png) { Copy-Item C:\AW\briarhound_work\prep\banner_art.png "$dst\banner_art.png" -Force; 'PROP banner_art' }
}

# The starting weapon set (static jobs Weapon_*): the game swaps them in for the KayKit pieces (WeaponSkin.cs).
if ($Stage -in 'all', 'weapons') {
    $dst = "$r\Assets\Resources\Models\Weapons"; New-Item -ItemType Directory -Force $dst | Out-Null
    foreach ($n in 'Weapon_Sword', 'Weapon_RoundShield', 'Weapon_Bow', 'Weapon_Quiver', 'Weapon_Spear', 'Weapon_KiteShield') {
        $job = Latest "static_${n}_*"
        if (-not $job) { "MISSING weapon $n"; continue }
        $fbx = Get-ChildItem $job -Filter *.fbx | Select-Object -First 1
        $alb = Get-ChildItem $job -Filter *_albedo.png | Select-Object -First 1
        if (-not $alb) { $alb = Get-Item "$job\texture.png" -ErrorAction SilentlyContinue }
        Copy-Item $fbx.FullName "$dst\$n.fbx" -Force
        if ($alb) { Copy-Item $alb.FullName "$dst\${n}_texture.png" -Force }
        "WEAPON $n $($fbx.Name) $($alb.Name)"
    }
}

# 0.9: the skeletons and the Grave Warden reconstructed for the Codex combat test (Forge rigs, weight-repaired where
# Codex repaired them, the Warden's separate horned helm), copied read-only into a staging folder and built onto the
# game's own monster skeleton and clips with our weapons, like the monsters stage above.
if ($Stage -in 'codexmonsters') {
    $cx = 'C:\Users\juana\Documents\FolkloreHunter-CombatTest-Codex\SourceArt'
    $st = 'C:\AW\briarhound_work\codex_bodies'
    foreach ($b in @(@('Boneguard', "$cx\Skeletons\Boneguard\Model"), @('IronRevenant', "$cx\Skeletons\IronRevenant\Model\WeightRepair"),
                     @('GraveHexer', "$cx\Skeletons\GraveHexer\Model\WeightRepair"), @('GraveWardenBody', "$cx\GraveWarden\Body\Model\WeightRepair"),
                     @('WardenHelmet', "$cx\GraveWarden\Helmet\Model"))) {
        New-Item -ItemType Directory -Force "$st\$($b[0])" | Out-Null
        foreach ($f in 'rigged.glb', 'model.glb') { if (Test-Path "$($b[1])\$f") { Copy-Item "$($b[1])\$f" "$st\$($b[0])\$f" -Force } }
    }
    $sw = Latest 'static_WarriorSword_*'; $ss = Latest 'static_WarriorShield_*'; $kg = Latest 'static_KnightGreatsword_*'
    $ms = Latest 'static_MageStaff_*'; $wg = Latest 'static_WardenGreatsword_*'
    Blend 'Gravebound.blend' @("$st\Boneguard", 'Gravebound_Warrior', '1', '--monster', '--prop', "$sw|handslot.r|blade|0.44|0.07|Sword", '--prop', "$ss|handslot.l|shield|0.34|0.5|Shield") 'Gravebound_Warrior'
    Blend 'Gravebound.blend' @("$st\IronRevenant", 'Gravebound_Knight', '1', '--monster', '--prop', "$kg|handslot.r|blade|0.64|0.1|GreatSword") 'Gravebound_Knight'
    Blend 'Gravebound.blend' @("$st\GraveHexer", 'Gravebound_Mage', '1', '--monster', '--prop', "$ms|handslot.r|staff|1.02|0.42|Staff") 'Gravebound_Mage'
    Blend 'Gravebound.blend' @("$st\GraveWardenBody", 'GraveWarden', '1', '--monster', '--prop', "$wg|handslot.r|blade|0.6|0.08|GreatSword", '--prop', "$st\WardenHelmet|head|helm|1.35|0.35|Helmet") 'GraveWarden'
}
