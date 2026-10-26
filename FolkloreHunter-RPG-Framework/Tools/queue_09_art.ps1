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
    & $py Tools\forge_queue.py static WarriorSword (Item 'items_SkeletonWarriorWeapons_*' 'longsword') "rusty one-handed longsword, gold cross-guard, leather grip" 1.0
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
