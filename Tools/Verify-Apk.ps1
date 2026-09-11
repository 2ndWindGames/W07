param([string]$Apk = (Join-Path $PSScriptRoot '../Builds/Android/RunBeat-1.0.0.apk'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$androidRoot = 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer'
$buildTools = Join-Path $androidRoot 'SDK/build-tools/36.0.0'
$reportDir = Join-Path $taskRoot 'BuildArtifacts/QA/APK'
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$Apk = (Resolve-Path -LiteralPath $Apk).Path

$badging = & "$buildTools/aapt2.exe" dump badging $Apk
if ($LASTEXITCODE -ne 0) { throw 'APK metadata extraction failed' }
$badging | Set-Content -LiteralPath "$reportDir/badging.txt"
foreach ($expected in @("package: name='com.secondwindgames.runbeat'", "minSdkVersion:'26'", "targetSdkVersion:'36'", "native-code: 'arm64-v8a'")) {
    if (!($badging | Select-String -SimpleMatch $expected)) { throw "APK metadata missing: $expected" }
}
$permissions = & "$buildTools/aapt2.exe" dump permissions $Apk
$permissions | Set-Content -LiteralPath "$reportDir/permissions.txt"
if ($permissions | Select-String -SimpleMatch 'android.permission.INTERNET') { throw 'Unexpected Internet permission' }
foreach ($permission in @('FOREGROUND_SERVICE', 'FOREGROUND_SERVICE_MEDIA_PLAYBACK', 'WAKE_LOCK', 'POST_NOTIFICATIONS')) {
    if (!($permissions | Select-String -SimpleMatch "android.permission.$permission")) { throw "Missing permission: $permission" }
}
$manifest = & "$buildTools/aapt2.exe" dump xmltree $Apk --file AndroidManifest.xml
$manifest | Set-Content -LiteralPath "$reportDir/manifest.txt"
if (!($manifest | Select-String -Pattern 'android:screenOrientation\([^)]*\)=(1|0x0*1)$')) { throw 'Portrait activity missing' }
if (!($manifest | Select-String -SimpleMatch 'com.secondwindgames.runbeat.RunBeatService')) { throw 'Native foreground service missing' }

& "$androidRoot/OpenJDK/bin/java.exe" -jar "$buildTools/lib/apksigner.jar" verify --verbose --print-certs $Apk > "$reportDir/signature.txt"
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed' }
& "$buildTools/zipalign.exe" -c -P 16 -v 4 $Apk > "$reportDir/zipalign.txt"
if ($LASTEXITCODE -ne 0) { throw 'APK 16 KB alignment verification failed' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($Apk)
$elfReport = [Collections.Generic.List[string]]::new()
try {
    $libraries = @($archive.Entries | Where-Object { $_.FullName -match '^lib/arm64-v8a/[^/]+\.so$' })
    if ($libraries.Count -eq 0) { throw 'ARM64 native libraries missing' }
    foreach ($entry in $libraries) {
        $libraryPath = Join-Path $reportDir $entry.Name
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $libraryPath, $true)
        $headers = & "$androidRoot/NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-readelf.exe" --program-headers --wide $libraryPath
        if ($LASTEXITCODE -ne 0) { throw "ELF inspection failed: $($entry.Name)" }
        $loads = @($headers | Select-String -Pattern '^\s*LOAD\s')
        if ($loads.Count -eq 0) { throw "ELF load segments missing: $($entry.Name)" }
        foreach ($load in $loads) {
            $alignment = ($load.Line.Trim() -split '\s+')[-1]
            if ([Convert]::ToInt64($alignment, 16) -lt 16384) { throw "ELF segment alignment below 16 KB: $($entry.Name)" }
        }
        $elfReport.Add("PASS $($entry.Name): every LOAD segment aligned to at least 16 KB")
    }
} finally { $archive.Dispose() }
$elfReport | Set-Content -LiteralPath "$reportDir/elf-alignment.txt"
$hash = (Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash
$summary = @('PASS package, API levels, ARM64, portrait and foreground service', 'PASS no Internet permission; playback and notification permissions present', 'PASS APK signature and 16 KB ZIP alignment') + $elfReport + @("SHA256 $hash", 'Signing identity is reported in signature.txt; local debug signing is not a store release signature.')
$summary | Set-Content -LiteralPath "$reportDir/summary.txt"
$summary
