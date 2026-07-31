[CmdletBinding()]
param(
    [string]$BackendDir = "",
    [string]$FrontendDir = "",
    [string]$LibraryRootPath = "",
    [string]$Password = "",
    [bool]$SmartVideoGrouping = $true,
    [int]$Port = 5107,
    [string]$ListenAddress = '0.0.0.0',
    [string]$OutputDir = (Join-Path $env:USERPROFILE ''),
    [switch]$SkipFrontendBuild,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$msg) {
    Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

$ApiProject = Join-Path $BackendDir 'LCP.API'
$PublishDir = Join-Path $ApiProject 'bin\Release\net9.0\win-x64\publish'

if (-not (Test-Path $ApiProject)) {
    throw "Backend project not found: $ApiProject"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK not found in PATH'
}

if (-not $SkipFrontendBuild) {
    Write-Step "Building frontend ($FrontendDir)"
    if (-not (Test-Path (Join-Path $FrontendDir 'package.json'))) {
        throw "Frontend project not found: $FrontendDir"
    }
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw 'npm not found in PATH'
    }
    Push-Location $FrontendDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'npm build failed' }
    }
    finally {
        Pop-Location
    }
}

$browserOut = Join-Path $FrontendDir 'dist\lcp-fe\browser'
if (-not (Test-Path $browserOut)) {
    throw "Frontend build output not found: $browserOut (run with -SkipFrontendBuild only if dist already exists)"
}

Write-Step "Copying frontend output to wwwroot"
$wwwroot = Join-Path $ApiProject 'wwwroot'
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item (Join-Path $browserOut '*') $wwwroot -Recurse -Force

$programCs = Join-Path $ApiProject 'Program.cs'
if (-not (Select-String -Path $programCs -Pattern 'UseStaticFiles' -Quiet)) {
    Write-Warning 'Program.cs does not contain UseStaticFiles - SPA hosting is not wired up. The exe will serve only the API.'
}

Write-Step "Publishing single-file exe"
& dotnet publish (Join-Path $ApiProject 'LCP.API.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -p:NoWarn=MSB3246
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Step "Writing appsettings.json"
$config = @{
    Logging = @{
        LogLevel = @{
            Default = 'Information'
            'Microsoft.AspNetCore' = 'Warning'
        }
    }
    AllowedHosts = '*'
    Urls = "http://$ListenAddress`:$Port"
    LibrarySettings = @{
        LibraryRootPath = $LibraryRootPath
        Password = $Password
        SmartVideoGrouping = $SmartVideoGrouping
    }
} | ConvertTo-Json -Depth 5
Set-Content -Path (Join-Path $PublishDir 'appsettings.json') -Value $config -Encoding UTF8

Write-Step "Copying to $OutputDir"
Get-Process -Name 'LCP.API' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$OutputDir*" } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir | Out-Null
Copy-Item (Join-Path $PublishDir '*') $OutputDir -Recurse -Force

Write-Step "Configuring Windows Firewall (TCP $Port)"
$ruleName = "LCP API TCP $Port"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    & netsh advfirewall firewall delete rule name=$ruleName 2>$null | Out-Null
    & netsh advfirewall firewall add rule name=$ruleName dir=in action=allow protocol=TCP localport=$Port
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Firewall rule added: $ruleName (inbound TCP $Port)" -ForegroundColor Green
    }
    else {
        Write-Warning "Failed to add firewall rule. Allow the prompt when the app first starts."
    }
}
else {
    Write-Warning 'Not running as Administrator - skipping automatic firewall rule.'
    Write-Warning "Run this script as Administrator once, or allow the Windows firewall prompt when the app first starts."
    Write-Warning "Manual: netsh advfirewall firewall add rule name=`"$ruleName`" dir=in action=allow protocol=TCP localport=$Port"
}

$lanIps = Get-NetIPConfiguration -ErrorAction SilentlyContinue |
    Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
    ForEach-Object { $_.IPv4Address.IPAddress } |
    Where-Object { $_ -ne '0.0.0.0' }

Write-Host "`nDone. Artifacts in: $OutputDir" -ForegroundColor Green
Write-Host "Local access : http://localhost:$Port" -ForegroundColor Green
if ($lanIps) {
    Write-Host "LAN access   :" -ForegroundColor Green
    foreach ($ip in $lanIps) {
        Write-Host ("                http://{0}:{1}" -f $ip, $Port) -ForegroundColor Green
    }
}
else {
    Write-Host "LAN access   : (no non-loopback IPv4 address detected)" -ForegroundColor Yellow
}
Write-Host "Library path : $LibraryRootPath | Password: $($(if ($Password) { 'set' } else { 'none' }))" -ForegroundColor Green
if ($Password -eq '' -and $ListenAddress -notlike '127.*' -and $ListenAddress -ne 'localhost') {
    Write-Warning 'Listening on the network WITHOUT a password - anyone on the LAN can access this server (including Shutdown). Consider -Password "..."'
}

if ($Launch) {
    Start-Process -FilePath (Join-Path $OutputDir 'LCP.API.exe') -WorkingDirectory $OutputDir
}
