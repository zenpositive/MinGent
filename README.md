# MinGent

An AI agent for Windows that works with models on your own PC. It has no dependency tree: you compile it from the source in this repository with the C# compiler that is part of Windows.

- Works in your files, runs commands, searches the web and uses its own browser, and shows every step.
- Runs models on your PC with its built-in llama.cpp engine. Claude, OpenAI and Gemini work with your own key.
- Supply chain resistant: no packages, no installer, no updater, no plugins, no telemetry.
- Asks before it installs software, also in Auto mode, unless you switch that off.
- Offline mode, a list of the internet addresses it connects to, and one button that erases what it stored.

## Get it

1. Download `MinGent-1.0-beta.zip` from [Releases](https://github.com/zenpositive/MinGent/releases/latest) and compare its SHA-256 with the one listed there.
2. Extract it and double-click `Build-MinGent.bat`.
3. Start `MinGent.exe`.

Needs Windows 10 or 11. `README.txt` explains the rest. This is a beta: report problems in [Issues](https://github.com/zenpositive/MinGent/issues).

## License

MIT, with no warranty. An AI agent can delete files, run commands and act on websites. Use Ask mode for anything you can't undo.
