# Instala toolchain local (sin admin) para compilar RutaCam GPS (.NET 10 MAUI Android)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnetDir = Join-Path $root 'dotnet10'
$jdkDir = Join-Path $root 'jdk17'
$sdkDir = Join-Path $root 'android-sdk'

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

if (-not (Test-Path "$dotnetDir\dotnet.exe")) {
    Write-Host '== .NET 10 SDK =='
    & "$root\dotnet-install.ps1" -Channel 10.0 -InstallDir $dotnetDir -NoPath
}
$env:DOTNET_ROOT = $dotnetDir
$env:PATH = "$dotnetDir;$env:PATH"

if (-not (Test-Path "$jdkDir\bin\java.exe")) {
    Write-Host '== JDK 17 (Microsoft OpenJDK) =='
    $zip = Join-Path $root 'jdk17.zip'
    Invoke-WebRequest 'https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.zip' -OutFile $zip
    $tmp = Join-Path $root 'jdk17_tmp'
    Expand-Archive $zip $tmp -Force
    Move-Item (Get-ChildItem $tmp -Directory | Select-Object -First 1).FullName $jdkDir
    Remove-Item $tmp -Recurse -Force; Remove-Item $zip
}
$env:JAVA_HOME = $jdkDir

Write-Host '== Workload maui-android =='
& "$dotnetDir\dotnet.exe" workload install maui-android --skip-sign-check

Write-Host '== Listo =='
& "$dotnetDir\dotnet.exe" --info | Select-Object -First 12
