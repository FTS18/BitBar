# Require Administrator privileges
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Restarting as Administrator..." -ForegroundColor Yellow
    Start-Process powershell.exe "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$AppName = "BitBar"
$InstallDir = "C:\Program Files\$AppName"
$PublishDir = "$PSScriptRoot\windows\bin\Release\net10.0-windows\win-x64\publish"
$ExePath = "$InstallDir\$AppName.exe"

Write-Host "Building and Publishing $AppName..." -ForegroundColor Cyan
Set-Location "$PSScriptRoot\windows"
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false

if (-not (Test-Path "$PublishDir\$AppName.exe")) {
    Write-Host "Build failed. Could not find executable in $PublishDir." -ForegroundColor Red
    Pause
    exit
}

Write-Host "Stopping any running instances..." -ForegroundColor Cyan
Stop-Process -Name $AppName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1 # Wait for handles to release

Write-Host "Copying files to $InstallDir..." -ForegroundColor Cyan
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
}
Copy-Item "$PublishDir\*" -Destination $InstallDir -Recurse -Force

Write-Host "Creating Start Menu Shortcut..." -ForegroundColor Cyan
$StartMenuPath = [Environment]::GetFolderPath("Programs")
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$StartMenuPath\$AppName.lnk")
$Shortcut.TargetPath = $ExePath
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Save()

Write-Host "Registering Auto-Start on Boot..." -ForegroundColor Cyan
$RegKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $RegKey -Name $AppName -Value $ExePath

Write-Host "Starting $AppName..." -ForegroundColor Cyan
Start-Process $ExePath

Write-Host "=========================================" -ForegroundColor Green
Write-Host "Installation Complete! BitBar is now running." -ForegroundColor Green
Write-Host "It will automatically start when Windows boots." -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
Pause
