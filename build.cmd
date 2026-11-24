@echo off
rem Builds bin\ShakeCursor.exe with the C# compiler that ships with every Windows (.NET Framework 4.x).
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin

set RES=
for %%f in (assets\cursors\breeze\*.cur assets\cursors\breeze\*.ani) do set RES=!RES! -resource:%%f,Breeze.%%~nxf

"%CSC%" -nologo -optimize+ -codepage:65001 -target:winexe -platform:anycpu -out:bin\ShakeCursor.exe ^
  -win32manifest:src\app.manifest -win32icon:assets\app.ico ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  !RES! src\*.cs
exit /b %ERRORLEVEL%
