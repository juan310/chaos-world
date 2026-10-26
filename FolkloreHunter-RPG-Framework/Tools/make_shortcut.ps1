# Creates the desktop shortcut for the 0.6.0 Proving Grounds build (the 0.4.0 framework shortcut and the sample's are kept).
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
$d = [Environment]::GetFolderPath('Desktop')
$w = New-Object -ComObject WScript.Shell
$s = $w.CreateShortcut((Join-Path $d 'Folklore Hunter RPG - Proving Grounds Build.lnk'))
$s.TargetPath = "$p\Build-Proving\FolkloreHunterProving.exe"
$s.WorkingDirectory = "$p\Build-Proving"
$s.Description = 'Folklore Hunter: Rise of the Estate - 0.6.0 with the Proving Grounds (Monster Hunter-style combat test, Xbox controller)'
$s.IconLocation = "$p\Build-Proving\FolkloreHunterProving.exe,0"
$s.Save()
Get-Item (Join-Path $d 'Folklore Hunter RPG - Proving Grounds Build.lnk') | Select-Object FullName
