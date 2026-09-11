$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$androidRoot = 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer'
$testOutput = Join-Path $taskRoot 'BuildArtifacts/QA/Native'
New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
$synth = Join-Path $taskRoot 'Assets/Plugins/Android/RunBeat.androidlib/src/com/secondwindgames/runbeat/RhythmSynth.java'
& "$androidRoot/OpenJDK/bin/javac.exe" -encoding UTF-8 -d $testOutput $synth "$PSScriptRoot/RhythmSynthTest.java"
if ($LASTEXITCODE -ne 0) { throw 'Native rhythm compiler failed' }
& "$androidRoot/OpenJDK/bin/java.exe" -cp $testOutput RhythmSynthTest | Tee-Object -FilePath "$testOutput/results.txt"
if ($LASTEXITCODE -ne 0) { throw 'Native rhythm tests failed' }
