# Creates the desktop shortcut for the framework milestone build (does not touch the sample's shortcuts).
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
$d = [Environment]::GetFolderPath('Desktop')
$w = New-Object -ComObject WScript.Shell
$s = $w.CreateShortcut((Join-Path $d 'Folklore Hunter RPG - Framework Build.lnk'))
$s.TargetPath = "$p\Build-RPG\FolkloreHunterRPG.exe"
$s.WorkingDirectory = "$p\Build-RPG"
$s.Description = 'Folklore Hunter: Rise of the Estate - RPG framework milestone 0.4.0'
$s.IconLocation = "$p\Build-RPG\FolkloreHunterRPG.exe,0"
$s.Save()
Get-Item (Join-Path $d 'Folklore Hunter RPG - Framework Build.lnk') | Select-Object FullName
