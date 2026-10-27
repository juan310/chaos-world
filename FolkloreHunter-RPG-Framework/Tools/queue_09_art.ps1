# Queues the 0.9 Forge jobs (local GPU): weapon-free skeleton and Warden bodies, their weapons and the lair props as
# static models, and the twenty hairstyle pictures (edit tool on the main character sheet's hunters).
param([string]$Stage = 'all')
$py = "C:\Users\juana\AppData\Local\Programs\Python\Python312\python.exe"
$r = "C:\Users\juana\Documents\FolkloreHunter-RPG-Framework"
$c = "$r\Docs\Concepts\Characters-0.9\crops"
$F = "$env:LOCALAPPDATA\ClaudeEyesStudio\Forge"
$w = "C:\AW\briarhound_work\prep"; New-Item -ItemType Directory -Force $w | Out-Null
Set-Location $r
function Latest($pattern) { (Get-ChildItem $F -Directory -Filter $pattern | Sort-Object Name | Select-Object -Last 1).FullName }
function Item($job, $like) { (Get-ChildItem (Latest $job) -Filter "item_*$like*_hd.png" | Select-Object -First 1).FullName }
if ($Stage -in 'all', 'bodies') {
    foreach ($n in 'SkeletonWarrior', 'SkeletonKnight', 'SkeletonMage', 'GraveWarden') {
        & $py Tools\forge_prep.py pad "$(Latest "edit_${n}Bare_*")\edit_1.png" "$w\${n}_bare.png"
    }
    & $py Tools\forge_queue.py model SkeletonWarriorB "$w\SkeletonWarrior_bare.png" "skeleton warrior, rusty pauldron, torn red cloth hood and loincloth, leather belt and pouch, leather bracers and boots"
    & $py Tools\forge_queue.py model SkeletonKnightB "$w\SkeletonKnight_bare.png" "armoured skeleton knight, dark tattered navy cloak, rusty gold-trimmed plate armour"
    & $py Tools\forge_queue.py model SkeletonMageB "$w\SkeletonMage_bare.png" "skeleton mage in a tattered purple hooded robe, gold amulet, spell book and scroll cases at the belt, bare bony feet"
    & $py Tools\forge_queue.py model GraveWardenB "$w\GraveWarden_bare.png" "giant armoured skeleton lord, horned helmet, glowing orange eyes, tattered crimson cape and loincloth, rusty gold-trimmed plate armour"
}
if ($Stage -in 'all', 'props') {
    & $py Tools\forge_queue.py static WardenGreatsword (Item 'items_GraveWardenWeapons_*' 'greatsword') "huge rusty greatsword, spiked gold cross-guard, red-wrapped grip" 1.9
    & $py Tools\forge_queue.py static KnightGreatsword (Item 'items_SkeletonKnightWeapons_*' 'greatsword') "rusty greatsword, ornate gold cross-guard" 1.7
    & $py Tools\forge_queue.py static WarriorSword (Item 'items_SkeletonWarriorWeapons_*' 'one_handed') "rusty one-handed longsword, gold cross-guard, leather grip" 1.0
    & $py Tools\forge_queue.py static WarriorShield (Item 'items_SkeletonWarriorWeapons_*' 'shield') "round red wooden shield, steel rim and boss, gold star" .8
    & $py Tools\forge_queue.py static MageStaff (Item 'items_SkeletonMageWeapons_*' 'staff') "gnarled dark wooden staff with a glowing purple crystal and chains" 1.9
    & $py Tools\forge_queue.py static SkullPedestal (Item 'items_LairProps_*' 'pedestal') "dark stone pedestal with a carved skull, iron fire brazier bowl on top" 1.9
    & $py Tools\forge_queue.py static Sarcophagus (Item 'items_LairProps_*' 'sarcophagus') "dark stone sarcophagus with carved lid and skulls" 1.1
    & $py Tools\forge_queue.py static KnightStatue (Item 'items_LairProps_*' 'statue') "grey stone statue of an armoured knight in a horned helm holding a greatsword point down" 3.4
    & $py Tools\forge_queue.py static Candelabra (Item 'items_LairProps_*' 'candelabra') "wrought iron candelabra with five candles" .8
    & $py Tools\forge_prep.py cutout (Item 'items_LairProps_*' 'banner') "$w\banner_art.png"
}
if ($Stage -in 'all', 'hair') {
    $keep = 'the face, eyes, skin, outfit, armour, pose, body proportions and background stay exactly the same'
    $female = @('shoulder-length voluminous wavy curls worn loose, no braid', 'a short tousled pixie cut with the ears showing',
        'a straight chin-length bob with side-swept bangs', 'very long straight sleek hair down to the waist with a side part',
        'a high ponytail tied at the crown with a long wavy tail and side bangs', 'a low wavy ponytail at the nape draped over her left shoulder',
        'one thick braid over her left shoulder with side bangs', 'two long braids, one over each shoulder',
        'a messy bun on top of the head with loose strands framing the face', 'a half-up braided crown with long wavy hair down the back')
    $male = @('a short tousled messy crop', 'medium side-swept hair with a long fringe', 'short tight curls',
        'an undercut: shaved sides with a longer swept-back top', 'shoulder-length loose wavy hair', 'hair tied back in a short low ponytail at the nape',
        'hair pulled up into a topknot bun', 'a long single warrior braid over his right shoulder with a side-swept top', 'slicked-back hair', 'a very short buzz cut')
    for ($i = 0; $i -lt 10; $i++) {
        $n = '{0:00}' -f ($i + 1)
        & $py Tools\forge_queue.py edit "HairF$n" "$c\female_body.png" "change only her hairstyle to $($female[$i]), the same chestnut brown hair colour; $keep"
        & $py Tools\forge_queue.py edit "HairM$n" "$c\male_body.png" "change only his hairstyle to $($male[$i]), the same chestnut brown hair colour; $keep"
    }
}
if ($Stage -in 'sides') {
    # profile pictures for the flanks (build_hunter_variant.py --sides): the camera on the character's left sees them
    # facing the picture's left edge; on the right, facing its right edge
    $views = @{ 'Left' = "turn the character 90 degrees to show the left side of the body in full profile, facing the left edge of the picture; same outfit, armour, hair and colours, standing straight, arms relaxed, full body, plain dark grey background";
                'Right' = "turn the character 90 degrees to show the right side of the body in full profile, facing the right edge of the picture; same outfit, armour, hair and colours, standing straight, arms relaxed, full body, plain dark grey background" }
    $src = @{ 'HunterMale' = "$c\male_body.png"; 'HunterFemale' = "$c\female_body.png"; 'SkeletonWarrior' = "$w\SkeletonWarrior_bare.png";
              'SkeletonKnight' = "$w\SkeletonKnight_bare.png"; 'SkeletonMage' = "$w\SkeletonMage_bare.png"; 'GraveWarden' = "$w\GraveWarden_bare.png" }
    foreach ($k in $src.Keys) { foreach ($v in 'Left', 'Right') { & $py Tools\forge_queue.py edit "$k$v" $src[$k] $views[$v] } }
}
if ($Stage -in 'hairviews') {
    # for each finished hairstyle picture: its back and both profiles (the hair differs on every side)
    $back = 'turn the character around: the same person seen from directly behind, full body, same outfit and the same hairstyle seen from the back; plain dark grey background'
    $left = "turn the character 90 degrees to show the left side of the body in full profile, facing the left edge of the picture; same outfit, armour, hairstyle and colours, standing straight, arms relaxed, full body, plain dark grey background"
    $right = "turn the character 90 degrees to show the right side of the body in full profile, facing the right edge of the picture; same outfit, armour, hairstyle and colours, standing straight, arms relaxed, full body, plain dark grey background"
    foreach ($g in 'F', 'M') { for ($i = 1; $i -le 10; $i++) {
        $n = '{0:00}' -f $i; $front = Latest "edit_Hair$g${n}_*"
        if (-not $front -or -not (Test-Path "$front\edit_1.png")) { "MISSING hair picture $g$n"; continue }
        & $py Tools\forge_prep.py pad "$front\edit_1.png" "$w\Hair$g${n}_front.png" .08
        & $py Tools\forge_queue.py edit "Hair$g${n}Back" "$w\Hair$g${n}_front.png" $back
        & $py Tools\forge_queue.py edit "Hair$g${n}Left" "$w\Hair$g${n}_front.png" $left
        & $py Tools\forge_queue.py edit "Hair$g${n}Right" "$w\Hair$g${n}_front.png" $right
    } }
}
if ($Stage -in 'hairmodels') {
    foreach ($g in 'F', 'M') { for ($i = 1; $i -le 10; $i++) {
        $n = '{0:00}' -f $i; $b = Latest "edit_Hair$g${n}Back_*"
        if (-not $b -or -not (Test-Path "$b\edit_1.png")) { "MISSING hair back $g$n"; continue }
        $who = $(if ($g -eq 'M') { 'young man, chestnut brown hair, brown leather vest, white shirt, steel pauldrons with green cloth, red sash, dark trousers, steel knee guards, brown boots' } else { 'young woman, chestnut brown hair, brown leather corset, white blouse, steel pauldrons with green cloth, red sash, dark trousers, steel knee guards, brown boots' })
        & $py Tools\forge_queue.py model "Hair$g$n" "$w\Hair$g${n}_front.png" $who "$b\edit_1.png"
    } }
}

# The hunter's starting weapon set (Docs/Concepts/Weapons-0.9/starting-weapon-set.png): clean item pictures in its
# style, then (once they are done) static models the game swaps in for the KayKit weapons (Models/Weapons/<piece>).
if ($Stage -in 'weapons') {
    & $py Tools\forge_queue.py items StartingWeapons "$r\Docs\Concepts\Weapons-0.9\starting-weapon-set.png" "longsword, steel blade, curved iron cross-guard, brown leather-wrapped grip, iron pommel" "round shield, dark wooden planks, iron rim and cross straps, studded iron boss, painted tree emblems" "recurve bow, carved wooden limbs with vine pattern, leather-wrapped grip and limb bands, taut string" "leather quiver with four fletched arrows, carved tree emblem, iron bands, shoulder strap" "spear, long leaf-shaped steel head, wooden shaft with leather wraps and iron bands, iron butt spike" "kite shield, wooden planks, riveted iron rim and spine, studded boss, painted antlered stag and vines"
}
if ($Stage -in 'weaponmodels') {
    & $py Tools\forge_queue.py static Weapon_Sword (Item 'items_StartingWeapons_*' 'longsword') "longsword with a steel blade, curved iron cross-guard and leather-wrapped grip" 1.1
    & $py Tools\forge_queue.py static Weapon_RoundShield (Item 'items_StartingWeapons_*' 'round_shield') "round wooden shield with an iron rim, cross straps and a central boss" .8
    & $py Tools\forge_queue.py static Weapon_Bow (Item 'items_StartingWeapons_*' 'recurve_bow') "wooden recurve bow with carved limbs, leather grip and a taut string" 1.5
    & $py Tools\forge_queue.py static Weapon_Quiver (Item 'items_StartingWeapons_*' 'quiver') "leather quiver holding four fletched arrows" .8
    & $py Tools\forge_queue.py static Weapon_Spear (Item 'items_StartingWeapons_*' 'spear') "long spear with a leaf-shaped steel head and banded wooden shaft" 2.4
    & $py Tools\forge_queue.py static Weapon_KiteShield (Item 'items_StartingWeapons_*' 'kite_shield') "wooden kite shield with an iron rim and spine, studded boss, stag emblem" 1.3
}

# Realistic villagers (no chibi figures: one body size in the game). Mira, Bram and Lady Elowen made from the main
# character sheet's hunters with the edit tool, then a back view, then rigged models built like the hunters.
$npcs = @(
    @('NpcMira', "$c\female_body.png", 'turn her into a village herbalist: a long green linen dress with a brown leather apron, a shawl over the shoulders, a satchel of herbs and dried flowers at the belt; keep her face, hair, build and standing pose; no armour, no weapons; plain light grey background', 'young village herbalist woman, chestnut hair, long green linen dress, brown leather apron, shawl, herb satchel'),
    @('NpcBram', "$c\male_body.png", 'turn him into a burly village blacksmith: a short beard, rolled-up linen shirt with bare muscular forearms, a heavy scorched leather smith apron, leather gloves tucked in the belt, work trousers and boots; keep his face, build and standing pose; no armour, no weapons; plain light grey background', 'burly bearded village blacksmith, linen shirt with rolled sleeves, heavy leather smith apron, work trousers, boots'),
    @('NpcElowen', "$c\female_body.png", 'turn her into a noble lady steward: a long deep purple and silver gown, a fur-trimmed dark cloak, a slim silver circlet, hair worn up; keep her face, build and standing pose; no armour, no weapons; plain light grey background', 'noble lady steward, long deep purple and silver gown, fur-trimmed dark cloak, silver circlet, hair worn up')
)
if ($Stage -in 'npcs') { foreach ($n in $npcs) { & $py Tools\forge_queue.py edit $n[0] $n[1] $n[2] } }
if ($Stage -in 'npcbacks') {
    $back = 'turn the character around: the same person seen from directly behind, full body, same clothes and hair seen from the back; plain dark grey background'
    foreach ($n in $npcs) {
        $front = Latest "edit_$($n[0])_*"
        if (-not $front -or -not (Test-Path "$front\edit_1.png")) { "MISSING villager picture $($n[0])"; continue }
        & $py Tools\forge_prep.py pad "$front\edit_1.png" "$w\$($n[0])_front.png" .08
        & $py Tools\forge_queue.py edit "$($n[0])Back" "$w\$($n[0])_front.png" $back
    }
}
if ($Stage -in 'npcmodels') {
    foreach ($n in $npcs) {
        $bk = Latest "edit_$($n[0])Back_*"
        if (-not $bk -or -not (Test-Path "$bk\edit_1.png")) { "MISSING villager back $($n[0])"; continue }
        & $py Tools\forge_queue.py model $n[0] "$w\$($n[0])_front.png" $n[3] "$bk\edit_1.png"
    }
}
