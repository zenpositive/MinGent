@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
set "CSC="
for %%D in (Framework64 Framework) do (
    if not defined CSC if exist "%WINDIR%\Microsoft.NET\%%D\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\%%D\v4.0.30319\csc.exe"
)
if not defined CSC (
    echo  [X] The C# compiler of the .NET Framework 4 was not found.
    exit /b 1
)
for %%I in ("%CSC%") do set "NET=%%~dpI"
if not defined LOCALAPPDATA (
    echo  [X] LOCALAPPDATA is not set, so there is no folder to build the signer in.
    exit /b 1
)
set "OUT=%LOCALAPPDATA%\MinGent signer"
if not exist "%OUT%\" mkdir "%OUT%"
if not exist "%OUT%\" echo  [X] Could not make the folder "%OUT%". & exit /b 1
if exist "%~dp0ReleaseSigner.exe" del /f /q "%~dp0ReleaseSigner.exe"
"%CSC%" /nologo /noconfig /target:exe /optimize+ /platform:anycpu /warn:4 /codepage:65001 /out:"%OUT%\ReleaseSigner.exe" /r:"%NET%System.dll" /r:"%NET%System.Core.dll" /r:"%NET%System.Security.dll" /r:"%NET%System.IO.Compression.dll" /r:"%NET%System.IO.Compression.FileSystem.dll" ReleaseSigner.cs ..\Release.cs
if errorlevel 1 (
    echo  [X] The signer did not build.
    exit /b 1
)
echo  Built %OUT%\ReleaseSigner.exe
"%WINDIR%\System32\certutil.exe" -hashfile "%OUT%\ReleaseSigner.exe" SHA256
