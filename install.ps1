# Shake Cursor for Windows - one-line installer
#
#   irm https://raw.githubusercontent.com/ksuvar/shakecursor-windows/main/install.ps1 | iex
#
# Downloads the latest ShakeCursor.exe from GitHub Releases and opens its installer.
# Silent install with the defaults (KDE Breeze pointers, start with Windows):
#
#   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/ksuvar/shakecursor-windows/main/install.ps1))) -Silent
#
# Add -KeepCursors to keep your current mouse pointers instead of switching to Breeze.

param([switch]$Silent, [switch]$KeepCursors)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$url = 'https://github.com/ksuvar/shakecursor-windows/releases/latest/download/ShakeCursor.exe'
$dir = Join-Path $env:TEMP ('ShakeCursor-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$exe = Join-Path $dir 'ShakeCursor.exe'

Write-Host 'Downloading Shake Cursor...'
Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing
Unblock-File $exe

$arguments = @()
if ($Silent) { $arguments += '--install', '--silent' }
if ($KeepCursors) { $arguments += '--keep-cursors' }

if ($arguments.Count) { $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -Wait }
else { $p = Start-Process -FilePath $exe -PassThru -Wait }

Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
if ($p.ExitCode -eq 0) { Write-Host 'Done. Shake your mouse to try it - the icon is in the taskbar notification area (^).' }
