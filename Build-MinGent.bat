@echo off
setlocal EnableExtensions DisableDelayedExpansion
title MinGent - build
cd /d "%~dp0"

set "QUIET="
if /i "%~1"=="/quiet" set "QUIET=1"

echo.
echo  ==============================================================
echo    MinGent builder
echo    Compiles the source code in the "src" folder into MinGent.exe
echo    using the C# compiler that is already part of Windows
echo    (.NET Framework 4). Nothing is downloaded or installed.
echo  ==============================================================
echo.

set "CSC="
for %%D in (Framework64 Framework) do (
    if not defined CSC if exist "%WINDIR%\Microsoft.NET\%%D\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\%%D\v4.0.30319\csc.exe"
)
if not defined CSC (
    echo  [X] The C# compiler of the .NET Framework 4 was not found.
    echo      It is built into Windows 10 and Windows 11. On older Windows,
    echo      install ".NET Framework 4.8" from Microsoft and run this again.
    goto :failed
)
for %%I in ("%CSC%") do set "NET=%%~dpI"
set "SYS=%WINDIR%\System32"

set "SOURCES=Program.cs Theme.cs Syntax.cs Document.cs EditorView.cs TabStrip.cs FindBar.cs MainForm.cs UiKit.cs PixelIcons.cs Json.cs Net.cs Eraser.cs Http.cs Secret.cs Llm.cs ModelCatalog.cs ModelLibrary.cs Engine.cs Shell.cs AgentCore.cs AgentTools.cs AgentAssist.cs AgentExtras.cs Uia.cs AgentHelpers.cs Skills.cs Charts.cs Schedule.cs Projects.cs Video.cs QuickEntry.cs YouTube.cs Audio.cs AudioDetail.cs AudioFocus.cs Release.cs ReleaseKeys.cs Updater.cs Markdown.cs TexParse.cs TexLayout.cs Documents.cs PdfText.cs DocBuild.cs DocExport.cs DocxWriter.cs XlsxWriter.cs PptxWriter.cs PdfFont.cs PdfLayout.cs PdfWriter.cs AppHighlight.cs ChatView.cs AiPanel.cs Attachments.cs Terminal.cs Browser.cs BrowserWindows.cs BrowserScripts.cs BrowserConsole.cs ConsoleDrawer.cs WebView2Interop.cs GeminiWeb.cs GeminiHelper.cs SettingsDialog.cs ModelLibraryDialog.cs Controls.cs Sidebar.cs"
set "FILES="
for %%F in (%SOURCES% MinGent.manifest MinGent.ico helper\manifest.json helper\background.js helper\content.js helper\icon48.png helper\icon128.png) do (
    if not exist "src\%%F" (
        echo  [X] Missing file: src\%%F
        echo      Keep this .bat file next to the "src" folder - extract the whole folder first.
        goto :failed
    )
)
for %%S in (%SOURCES%) do call set "FILES=%%FILES%% src\%%S"

if not exist "MinGent.exe" goto :notrunning
:checkrunning
"%SYS%\tasklist.exe" /FI "IMAGENAME eq MinGent.exe" /NH 2>nul | "%SYS%\find.exe" /I "MinGent.exe" >nul
if not errorlevel 1 (
    if defined QUIET (
        echo  [X] MinGent is running. Close it and build again.
        goto :failed
    )
    echo  [!] MinGent is running. Close it, then press any key to continue...
    pause >nul
    goto :checkrunning
)

:notrunning
set "ICONARGS=/win32icon:src\MinGent.ico /resource:src\MinGent.ico,MinGent.ico"
set "HELPERARGS=/resource:src\helper\manifest.json,helper.manifest.json /resource:src\helper\background.js,helper.background.js /resource:src\helper\content.js,helper.content.js /resource:src\helper\icon48.png,helper.icon48.png /resource:src\helper\icon128.png,helper.icon128.png"
echo  Compiler: %CSC%
echo  Building MinGent.exe ...
echo.

"%CSC%" /nologo /noconfig /target:winexe /optimize+ /platform:anycpu /warn:4 /codepage:65001 /out:MinGent.exe /win32manifest:src\MinGent.manifest %ICONARGS% %HELPERARGS% /r:"%NET%System.dll" /r:"%NET%System.Core.dll" /r:"%NET%System.Drawing.dll" /r:"%NET%System.Windows.Forms.dll" /r:"%NET%System.Security.dll" /r:"%NET%System.IO.Compression.dll" /r:"%NET%System.IO.Compression.FileSystem.dll" /r:"%NET%System.Xml.dll" /r:"%NET%Accessibility.dll" %FILES%
set "RESULT=%errorlevel%"
if not "%RESULT%"=="0" goto :failed
if not exist "MinGent.exe" goto :failed

echo.
echo  ==============================================================
echo    Done. MinGent.exe was created here:
echo    "%~dp0MinGent.exe"
echo.
echo    README.txt explains how to pick a model and get started.
echo  ==============================================================
echo.
if defined QUIET goto :end
"%SYS%\choice.exe" /C YN /N /M "  Start MinGent now? [Y/N] "
if errorlevel 2 goto :end
start "" "%~dp0MinGent.exe"
goto :end

:failed
echo.
echo  Build FAILED - see the messages above.
echo.
if not defined QUIET pause
endlocal
exit /b 1

:end
endlocal
exit /b 0
