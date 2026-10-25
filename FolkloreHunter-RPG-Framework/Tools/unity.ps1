# Runs Unity in batch mode against the framework project. Usage: unity.ps1 <method|tests> <logname>
param([string]$What = 'FHBuild.GenerateScene', [string]$Log = 'unity')
$p = 'C:\Users\juana\Documents\FolkloreHunter-RPG-Framework'
$u = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe'
New-Item -ItemType Directory -Force "$p\Logs" | Out-Null
$logFile = "$p\Logs\$Log.log"
if ($What -eq 'tests') {
  $args = "-batchmode -nographics -projectPath `"$p`" -runTests -testPlatform EditMode -testResults `"$p\Docs\unity-test-results.xml`" -logFile `"$logFile`""
} else {
  $args = "-batchmode -nographics -projectPath `"$p`" -executeMethod $What -quit -logFile `"$logFile`""
}
$proc = Start-Process -FilePath $u -ArgumentList $args -PassThru -Wait
"UNITY_EXIT=$($proc.ExitCode)"
Get-Content $logFile | Select-String -Pattern 'error CS|Exception|FH_|Build completed|BuildResult|Compilation failed|Scripts have compiler errors' | Select-Object -First 40 | ForEach-Object { $_.Line }
