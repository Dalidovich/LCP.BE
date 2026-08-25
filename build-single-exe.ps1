[CmdletBinding()]
param(
    [string]$BackendDir = $PSScriptRoot,
    [string]$FrontendDir = "",
    [string]$LibraryRootPath = "",
    [string]$Password = "",
    [bool]$SmartVideoGrouping = $true,
    [double]$MaxSyncDeletionRatio = 0.5,
    [long]$ThumbnailCacheBytes = 67108864,
    [long]$PreviewCacheBytes = 536870912,
    [int]$FfmpegProbeTimeoutSeconds = 30,
    [int]$FfmpegConvertTimeoutSeconds = 300,
    [long]$MaxUploadBytes = 68719476736,
    [string[]]$CorsAllowedOrigins = @(),
    [int]$Port = 5107,
    [string]$ListenAddress = '0.0.0.0',
    [string]$OutputDir = "",
    [switch]$SkipFrontendBuild,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$msg) {
    Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

function New-PasswordHash([string]$plainPassword) {
    $saltBytes = New-Object byte[] 16
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($saltBytes) } finally { $rng.Dispose() }

    $pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
        @($plainPassword, $saltBytes, 100000, [System.Security.Cryptography.HashAlgorithmName]::SHA256))
    try { $hashBytes = $pbkdf2.GetBytes(32) } finally { $pbkdf2.Dispose() }

    return @{
        Hash = [Convert]::ToBase64String($hashBytes)
        Salt = [Convert]::ToBase64String($saltBytes)
    }
}

function Assert-SafeOutputDir([string]$path) {
    $full = [System.IO.Path]::GetFullPath($path)
    $protected = @(
        [System.IO.Path]::GetPathRoot($full)
        $env:USERPROFILE
        $env:SystemRoot
        $env:ProgramFiles
        ${env:ProgramFiles(x86)}
        $BackendDir
        $FrontendDir
    ) | Where-Object { $_ }

    foreach ($candidate in $protected) {
        if ($full.TrimEnd('\') -ieq [System.IO.Path]::GetFullPath($candidate).TrimEnd('\')) {
            throw "Refusing to use -OutputDir '$full': the script wipes that directory before copying. Pick a dedicated folder."
        }
    }
    return $full
}

$ApiProject = Join-Path $BackendDir 'LCP.API'
$PublishDir = Join-Path $ApiProject 'bin\Release\net9.0\win-x64\publish'

if (-not (Test-Path $ApiProject)) {
    throw "Backend project not found: $ApiProject"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK not found in PATH'
}
if (-not $LibraryRootPath) {
    throw 'LibraryRootPath is required, for example: -LibraryRootPath "D:\mycoll"'
}

$OutputDir = Assert-SafeOutputDir $OutputDir

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
$passwordHash = ''
$passwordSalt = ''
if ($Password) {
    $credentials = New-PasswordHash $Password
    $passwordHash = $credentials.Hash
    $passwordSalt = $credentials.Salt
}

$settings = [ordered]@{
    Logging         = [ordered]@{
        LogLevel = [ordered]@{
            Default                = 'Information'
            'Microsoft.AspNetCore' = 'Warning'
        }
    }
    AllowedHosts    = '*'
    Urls            = "http://$ListenAddress`:$Port"
    LibrarySettings = [ordered]@{
        LibraryRootPath             = $LibraryRootPath
        PasswordHash                = $passwordHash
        PasswordSalt                = $passwordSalt
        SmartVideoGrouping          = $SmartVideoGrouping
        MaxSyncDeletionRatio        = $MaxSyncDeletionRatio
        ThumbnailCacheBytes         = $ThumbnailCacheBytes
        PreviewCacheBytes           = $PreviewCacheBytes
        FfmpegProbeTimeoutSeconds   = $FfmpegProbeTimeoutSeconds
        FfmpegConvertTimeoutSeconds = $FfmpegConvertTimeoutSeconds
        MaxUploadBytes              = $MaxUploadBytes
    }
}

if ($CorsAllowedOrigins.Count -gt 0) {
    $settings.Insert(2, 'Cors', [ordered]@{ AllowedOrigins = @($CorsAllowedOrigins) })
}

$config = $settings | ConvertTo-Json -Depth 6
Set-Content -Path (Join-Path $PublishDir 'appsettings.json') -Value $config -Encoding UTF8
Remove-Item (Join-Path $PublishDir 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue

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
Write-Host "Library path : $LibraryRootPath" -ForegroundColor Green
if ($passwordHash) {
    Write-Host "Password gate: enabled (PBKDF2-SHA256, 100000 iterations)" -ForegroundColor Green
}
else {
    Write-Host "Password gate: disabled - every endpoint is open" -ForegroundColor Yellow
}
if (-not $passwordHash -and $ListenAddress -notlike '127.*' -and $ListenAddress -ne 'localhost') {
    Write-Warning 'Listening on the network WITHOUT a password - anyone on the LAN can reach every endpoint, including Import and Shutdown. Consider -Password "..."'
}

if ($Launch) {
    Start-Process -FilePath (Join-Path $OutputDir 'LCP.API.exe') -WorkingDirectory $OutputDir
}
