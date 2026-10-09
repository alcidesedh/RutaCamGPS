param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\env.ps1"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = "$repoRoot\src\RutaCamGPS\RutaCamGPS.csproj"
$keystore = "$repoRoot\signing\rutacam-release.keystore"

$env:RUTACAM_KEY_PASS = "RutaCam2026Pass!"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       Compilando RutaCam GPS APK ($Configuration)        " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($Configuration -eq "Release") {
    dotnet publish $project -c Release -f net10.0-android -p:AndroidPackageFormat=apk -p:AndroidKeyStore=true -p:AndroidSigningKeyStore="$keystore" -p:AndroidSigningKeyAlias=rutacam -p:AndroidSigningKeyPass="$env:RUTACAM_KEY_PASS" -p:AndroidSigningStorePass="$env:RUTACAM_KEY_PASS"
} else {
    dotnet build $project -c Debug -f net10.0-android
}

if ($LASTEXITCODE -eq 0) {
    $apkDir = "$repoRoot\src\RutaCamGPS\bin\$Configuration\net10.0-android"
    $apk = Get-ChildItem -Path $apkDir -Filter "*.apk" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($apk) {
        $dest = "$repoRoot\dist"
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        $finalApk = "$dest\$($apk.Name)"
        Copy-Item -Path $apk.FullName -Destination $finalApk -Force
        Write-Host "`nAPK generado con éxito:" -ForegroundColor Green
        Write-Host "  $finalApk ($([Math]::Round($apk.Length / 1MB, 2)) MB)" -ForegroundColor Green
    }
}
