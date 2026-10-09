# Entorno de compilación local de RutaCam GPS. Uso:  . .\tools\env.ps1
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:DOTNET_ROOT = Join-Path $tools 'dotnet10'
$env:JAVA_HOME = Join-Path $tools 'jdk17'
$env:ANDROID_HOME = Join-Path $tools 'android-sdk'
$env:AndroidSdkDirectory = $env:ANDROID_HOME
$env:JavaSdkDirectory = $env:JAVA_HOME
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:PATH = "$env:DOTNET_ROOT;$env:JAVA_HOME\bin;$env:PATH"
