# AGENTS.md

MinGent is a Windows desktop app: an AI agent with its own notepad and browser, in C# with no dependencies. These notes are for anyone, human or AI, who changes the code.

## Rules

- No dependencies. No NuGet, no npm, no downloaded DLLs, no code copied in from elsewhere. If a feature needs a library, write the part you need or leave the feature out. This is the point of the project.
- C# 5 on .NET Framework 4.8. `Build-MinGent.bat` compiles with the csc.exe that ships with Windows, which stops at C# 5: no `$"..."`, `?.`, `nameof`, expression-bodied members, `out var`, tuples or local functions. Write anonymous functions as `delegate (...) { ... }` like the rest of the code.
- A new `.cs` file must be added to `SOURCES` in `Build-MinGent.bat`, or the build will not see it.
- The code has no comments. Keep it that way and make names say what things do.
- Text the user sees is plain and short: sentence case, no exclamation marks, no emoji, and say what happens rather than how clever it is.
- Nothing reaches the internet without going through `Http.Create` or `Net.Check`, so offline mode and the connection list stay true.
- Start Windows programs by their full path (`Native.WindowsExe`), and refer to framework assemblies in `Build-MinGent.bat` by full path, so a file dropped next to MinGent or in the current folder can't stand in for them. Anything downloaded and run, like the llama.cpp engine, is checked against a SHA-256 first.
- Anything MinGent stores goes under `Program.DataDir`, so "Erase everything" can find it. Check `Program.Erased` before writing there.
- An older version installs the next one, so keep the hand-off the same in every version: a copy of the old MinGent.exe runs `--finish-update` with the same arguments, waits for the new version to write `healthy.txt` in the `Updates` folder (30 seconds after it opens, or when it closes normally) and puts the old version back if it closes before that. Before anything is replaced, the old version runs the new build with `--check-release <note>`, which must exit 0 when two of the keys the new build pins signed it and its version matches. Keep these arguments, the `Updates` folder, `result.txt`, `healthy.txt` and the single-instance lock name working, or older versions can't update to it.
- `src/ReleaseKeys.cs` pins the keys that must sign the next release. Keep its layout exactly, the `Pinned` list with one quoted key per line and nothing else, because the updater refuses any other form. Change it only to replace a key, in a release that two of the current keys sign, and never pin fewer than two keys.
- Agent actions that install software, start other programs, delete widely, touch credentials, publish, or click buy and account buttons must keep asking the user in Auto mode. See `CommandSafety` in `Shell.cs` and `PermissionCategory` in `AgentCore.cs`.

## Build

Double-click `Build-MinGent.bat`, or run `Build-MinGent.bat /quiet` in a script. It writes `MinGent.exe` next to the .bat and fails on any compiler error. Close MinGent before you build.

## Releasing

1. Commit, then make the release zip from the committed files: everything under a top folder `MinGent/`, named `MinGent-<version>.zip` (for example `MinGent-1.0.6-beta.zip`), with the same version in `src/Program.cs`.
2. On the developer's laptop, build the signer with `src\tools\Build-Signer.bat`. It builds into `%LOCALAPPDATA%\MinGent signer`, outside the repo, so nothing in the repo folder can load into it. Rebuild it only after reading what changed in `src/tools` and `src/Release.cs` since the last build, because it runs with the signing keys. Then run `"%LOCALAPPDATA%\MinGent signer\ReleaseSigner.exe" sign MinGent-<version>.zip tpm:MinGentRelease security-key` and touch the YubiKey when Windows asks. This writes `MinGent-<version>.zip.sig.txt` next to the zip.
3. Check it against the keys the previous release pins: `ReleaseSigner.exe verify MinGent-<version>.zip <previous release zip>`.
4. Upload the zip and the `.sig.txt` file to the GitHub release.

The TPM key is named `MinGentRelease`. It lives in that laptop's TPM, under its Windows user, and never leaves the chip. The security key's private key never leaves the YubiKey. There is no password or secret file; `src/ReleaseKeys.cs` holds only the public halves.

## Where things are

| File | What it holds |
| --- | --- |
| `Program.cs` | Start-up, data folder, single instance, Open with registration |
| `MainForm.cs` | Main window, settings file, Notes tabs and session saving, erase flow |
| `AiPanel.cs`, `ChatView.cs`, `Markdown.cs` | Chat pane, message box, how answers are drawn |
| `Attachments.cs` | Files and pictures attached to a message: cards, previews, pasting, and the list the agent gets |
| `AgentCore.cs` | Agent loop, system prompt, permission levels, protected paths |
| `AgentTools.cs` | The tools: files, commands, web search and fetch, browser tools |
| `AgentAssist.cs` | Repairs tool calls that local models get wrong |
| `AgentHelpers.cs` | Helper agents (explorer, reviewer) |
| `AgentExtras.cs` | Memory, desktop program control, secure file erase |
| `Shell.cs` | Running commands, `CommandSafety`, `TerminalRules` (ssh and read-only server commands) |
| `Terminal.cs` | The agent's own terminal windows: the command line it typed, the check before Enter, ssh detection, reading the terminal's text |
| `Llm.cs` | Model providers: OpenAI-compatible servers, Anthropic, Gemini |
| `Engine.cs` | Built-in llama.cpp engine: pinned release with SHA-256 checksums, opt-in newer releases |
| `ModelLibrary.cs`, `ModelLibraryDialog.cs`, `ModelCatalog.cs` | Local models, Hugging Face search, downloads |
| `Browser.cs`, `BrowserWindows.cs`, `WebView2Interop.cs` | Built-in browser on WebView2 |
| `BrowserScripts.cs` | JavaScript the browser tools run, as C# strings |
| `BrowserConsole.cs`, `ConsoleDrawer.cs` | The browser console: console messages from the DevTools protocol, the check on code the agent runs there, and the console under the page |
| `Net.cs`, `Eraser.cs` | Offline mode, connection list, erase everything |
| `Http.cs`, `Json.cs` | HTTP and JSON |
| `Skills.cs` | Skills: the how-tos in the Skills folder, their line in the system prompt, the template for a new one |
| `Schedule.cs` | Scheduled tasks: when a task is due and how they are saved. The runner is in `AiPanel.cs` |
| `Projects.cs` | Projects: the work folders MinGent remembers |
| `Charts.cs` | Charts in answers: reading a chart block and laying it out |
| `QuickEntry.cs` | The shortcut that brings MinGent up from any program |
| `YouTube.cs` | Video details and transcripts |
| `Video.cs` | Watching videos: moments, time labels and the sheet of pictures. The `watch_video` tool is in `AgentTools.cs` |
| `Audio.cs` | Sound files for the agent: reads WAV and AIFF, decodes other formats with Windows Media Foundation, measures loudness, spectrum, pitch and tempo, draws the waveform and spectrogram |
| `AudioDetail.cs`, `AudioFocus.cs` | Deeper sound analysis: hits, beats and bars, notes, sections, lining up two files, and the listening report for sound the agent made |
| `Documents.cs`, `Doc*.cs`, `Pdf*.cs`, `DocxWriter.cs`, `XlsxWriter.cs`, `PptxWriter.cs`, `Tex*.cs` | Reading and writing documents, formulas |
| `EditorView.cs`, `Document.cs`, `TabStrip.cs`, `FindBar.cs`, `Syntax.cs` | Notes editor |
| `SettingsDialog.cs`, `Sidebar.cs`, `Controls.cs`, `UiKit.cs`, `Theme.cs`, `AppHighlight.cs` | Settings, chat list, controls, theme |
| `Secret.cs` | API keys encrypted with Windows DPAPI |
| `Updater.cs`, `Release.cs`, `ReleaseKeys.cs` | Updates: the release signature note and two of the pinned keys, download and build in a staging folder, then a copy of the running version replaces the files in place after MinGent closes |
| `tools/ReleaseSigner.cs` | The developer's signing tool: a TPM key, and a security key through Windows WebAuthn |
