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
- Agent actions that install software, start other programs, delete widely, touch credentials, publish, or click buy and account buttons must keep asking the user in Auto mode. See `CommandSafety` in `Shell.cs` and `PermissionCategory` in `AgentCore.cs`.

## Build

Double-click `Build-MinGent.bat`, or run `Build-MinGent.bat /quiet` in a script. It writes `MinGent.exe` next to the .bat and fails on any compiler error. Close MinGent before you build.

## Where things are

| File | What it holds |
| --- | --- |
| `Program.cs` | Start-up, data folder, single instance, Open with registration |
| `MainForm.cs` | Main window, settings file, Notes tabs and session saving, erase flow |
| `AiPanel.cs`, `ChatView.cs`, `Markdown.cs` | Chat pane, message box, how answers are drawn |
| `AgentCore.cs` | Agent loop, system prompt, permission levels, protected paths |
| `AgentTools.cs` | The tools: files, commands, web search and fetch, browser tools |
| `AgentAssist.cs` | Repairs tool calls that local models get wrong |
| `AgentHelpers.cs` | Helper agents (explorer, reviewer) |
| `AgentExtras.cs` | Memory, desktop program control, secure file erase |
| `Shell.cs` | Running commands, `CommandSafety` |
| `Llm.cs` | Model providers: OpenAI-compatible servers, Anthropic, Gemini |
| `Engine.cs` | Built-in llama.cpp engine: pinned release with SHA-256 checksums, opt-in newer releases |
| `ModelLibrary.cs`, `ModelLibraryDialog.cs`, `ModelCatalog.cs` | Local models, Hugging Face search, downloads |
| `Browser.cs`, `BrowserWindows.cs`, `WebView2Interop.cs` | Built-in browser on WebView2 |
| `BrowserScripts.cs` | JavaScript the browser tools run, as C# strings |
| `Net.cs`, `Eraser.cs` | Offline mode, connection list, erase everything |
| `Http.cs`, `Json.cs` | HTTP and JSON |
| `YouTube.cs` | Video details and transcripts |
| `Documents.cs`, `Doc*.cs`, `Pdf*.cs`, `DocxWriter.cs`, `XlsxWriter.cs`, `PptxWriter.cs`, `Tex*.cs` | Reading and writing documents, formulas |
| `EditorView.cs`, `Document.cs`, `TabStrip.cs`, `FindBar.cs`, `Syntax.cs` | Notes editor |
| `SettingsDialog.cs`, `Sidebar.cs`, `Controls.cs`, `UiKit.cs`, `Theme.cs`, `AppHighlight.cs` | Settings, chat list, controls, theme |
| `Secret.cs` | API keys encrypted with Windows DPAPI |
