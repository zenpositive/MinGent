MinGent
=======
An AI agent for Windows that works with models on your own PC. Tell it what
you want done and it reads and changes your files, runs commands, searches
the web and uses its own built-in browser, showing every step as it works.
It has its own notepad for your notes and for the files it works on.

It runs models on your PC with its built-in engine, or Claude, Gemini,
OpenAI or OpenRouter models with your own API key. It controls only the
programs it starts itself, and a program you opened only if you allow that
one program. While it uses one, it moves your mouse pointer and types into
that window. Before each step it waits until you have left the mouse and
keyboard alone for a second (four seconds at most), but it's still best to
leave them alone until it is done.

No dependency tree: no installer, no packages, no plugins and no automatic
updates.
MinGent itself has no third-party code. You compile it yourself from the
source in the "src" folder with the C# compiler that is part of Windows, so
the program you run is the code you can read. The optional built-in engine
is llama.cpp's prebuilt server (with NVIDIA's CUDA runtime on NVIDIA cards),
downloaded when you set it up, and the built-in browser is Microsoft Edge
WebView2.

This is version 1.1.2 beta: it works, and it still has rough edges. Report
problems at https://github.com/zenpositive/MinGent/issues.

MinGent is free software under the MIT license and comes with no warranty.
Read USE AT YOUR OWN RISK at the end of this file before you let it work on
anything you care about.


BUILD IT (one time, takes a few seconds)
----------------------------------------
1. Extract the whole MinGent folder somewhere you like, e.g. C:\Tools\MinGent
2. Double-click  Build-MinGent.bat
3. MinGent.exe appears next to the .bat file. That one file is the whole app.

The exe is compiled on your own PC from the readable source code in the "src"
folder, using the C# compiler that is already part of Windows 10/11
(.NET Framework 4.8). Nothing is downloaded or installed.

If Windows says "Windows protected your PC" when you start the .bat (it does
that for files that came from the internet): click "More info" > "Run anyway",
or right-click the .bat > Properties > tick "Unblock" > OK.

Smart App Control (Windows 11) or a company's application control policy can
block new, unsigned programs - a freshly built MinGent.exe or the built-in
engine (llama-server.exe). Windows then says the app was blocked (sometimes as
"your organization's Device Guard policy"). There is no exception for single
programs, and MinGent does not try to get around it. If only the engine is
blocked, cloud models still work.

To rebuild after changing the code, close MinGent and run the .bat again.
"Build-MinGent.bat /quiet" builds without any questions (for scripts).


SUPPLY CHAIN
------------
Most AI tools pull in hundreds of packages from npm or PyPI and update
themselves. Each of those is a way in: a hijacked maintainer account or a
look-alike package runs its code on your PC, next to your files, keys and
sign-ins. MinGent has none of that.
- No packages. It uses only the .NET Framework that comes with Windows. All
  of it is in "src": about 80,000 lines of C# you can read.
- No prebuilt programs. A release is the source code and Build-MinGent.bat,
  which compiles it with csc.exe from Windows itself.
- No automatic updates, plugins, extensions or telemetry. A new version
  arrives only when you download and build it, or when you click "Check for
  updates" in Settings > General and then Install.
- "Check for updates" installs a release only if both release keys pinned
  in your version signed it: one locked in the TPM chip of the developer's
  PC, and a security key that has to be touched for each signature. One key
  alone can't sign a release. MinGent checks the download against the
  SHA-256 in the signature and builds it from source with the C# compiler
  in Windows. Then it closes, puts the new version in place of the old one
  in the same folder, and opens again, so shortcuts and pins keep working.
  If the new version fails to start, the old one is put back. A release
  without both signatures is never installed.
- The one program MinGent downloads, the built-in engine (llama.cpp's
  server), is pinned to one release and checked against SHA-256 checksums
  written in the source before it is unpacked. Models are checked against the
  SHA-256 checksum Hugging Face lists for each file, when it lists one.
- Newer llama.cpp releases are only offered if you switch that on (Settings >
  Models). They are checked against the checksums GitHub lists for them,
  which protects the download but can't vouch for the release itself.
- The agent asks before it installs software (pip, npm, winget and the
  rest), also in Auto mode, unless you switch that off in Settings > Safety.
  Full auto asks nothing. The check reads the command; it can't see what a
  script does once it runs.
To check a download, compare its SHA-256 with the one on the release page.
In PowerShell: Get-FileHash .\MinGent-1.1.2-beta.zip


YOUR OWN ICON
-------------
MinGent comes with its own icon (src\MinGent.ico, the little robot face). To
use a different one, put a picture next to Build-MinGent.bat, name it
icon.png  and build again. It is converted into every size Windows uses
(16 to 256 px).

- Best: a square PNG, 256 x 256 px or bigger, with a transparent background.
- If Explorer or a pinned taskbar button still shows the old icon after a
  rebuild, that's the Windows icon cache. Unpin and pin again, or sign out and
  back in.

The converter (src\tools\MakeIcon.cs) is compiled into a temporary
"build-temp" folder only when icon.png exists, and deleted afterwards. It is not
part of MinGent.exe.


THE WINDOW
----------
- Left: your chats, grouped by Today, Yesterday, Previous 7 days and so on,
  with a search box, New chat, Models (the model library) and Settings. The
  button at the top folds the list into a narrow rail; it does that by itself
  when the window gets too narrow.
- Your projects show above the chats (see Workspace). Click one to list only
  its chats; new chats then start in that project until you click it again.
- Each chat's "..." button has Rename, Archive and Delete. Archived chats
  leave the list but stay on your PC; "Archived chats" at the bottom of the
  list shows them, and opening one moves it back. "Delete all chats" in
  Settings > Privacy deletes archived chats too.
- Middle: the chat and the message box.
- Right, when you or the agent need them: Notes (a full text editor with tabs;
  each chat keeps its own) and the browser. The two buttons at the top right
  of the chat show or hide them; drag the gaps between the panes to resize
  them.
- The x at the top right of the chat closes the agent and its chat list, so
  MinGent works as a plain notepad. The face button above Notes, or
  Ctrl+Shift+A, brings it back; it also comes back by itself when it needs
  your OK. MinGent remembers which way you left it.


CHATS
-----
- Type what you want and press Enter - a question, "fix the crash when the
  input is empty", "make a small website in a folder called site on my Desktop
  and show it in the browser", "find the newest version of X and update
  package.json". Shift+Enter starts a new line.
- While it works, one line under your message shows its face and what it is
  doing right now ("Reading parser.py", "Waiting for your OK"), with the
  step of the plan it is on next to it. While the model thinks, the line
  says what the thought is about instead of just that it is thinking, and
  what the agent last said to you shows under the line.
  Click the line to open every step: its thoughts, the files it read, the
  exact lines it changed, the commands it ran and their output. When it is
  done the line sums it up ("Read 2 files, edited parser.py, ran 1 command
  - 1m 14s"). The window title shows the current step too, or that the
  agent is waiting for you, so you can see it in the taskbar.
- Charts: when numbers are easier to see than to read, the agent can draw
  a bar or line chart right in its answer ("chart the monthly totals from
  sales.csv"). Copy on the chart gives you its data. MinGent draws the
  chart itself; nothing is downloaded for it.
- You can keep typing while it works. Messages you send then wait at the
  bottom of the chat and the agent reads them at its next step, so you can add
  details or change direction without stopping it. When it reads one, the
  steps it took before fold away above your message, and what it says back
  shows under it in the open. Until it reads one, a waiting message has a
  pencil to edit it and a bin to remove it (Up in the empty message box edits
  the last one). While you edit, the agent keeps working but doesn't read that
  message, or the ones after it, until you save. If it finishes in the
  meantime, the message goes out when you save. Stop puts queued messages back
  in the message box.
- When the agent asks you something, the question card lists its suggested
  answers; the last row of the card takes an answer of your own. When it
  offers a plan, type there what to change.
- Select any text in the chat with the mouse and copy it (Ctrl+C), or use the
  Copy button under an answer or a code block. Right-click for "Copy the whole
  conversation". Error messages and notices can be selected and copied too,
  and error dialogs have a Copy button.
- Bigger jobs get a plan, shown as a checklist above the message box. While
  the agent works, the step it is on shows next to its status; click the
  checklist to fold it away or open it again.
- Plan first: turn on the Plan chip under the message box (or press
  Shift+Tab) and the agent only reads and researches, then shows you its plan
  as a card. "Start" goes ahead, "Start in Auto" goes ahead without asking
  about ordinary steps, and "Keep planning" lets you change the plan. Nothing
  is changed before you approve it.
- Attach files, folders and pictures with +, by dropping them anywhere on the
  chat, or with Ctrl+V (a screenshot, a picture copied from a browser, files
  copied in Explorer). They show as cards above the message box, pictures as
  small previews; the x on a card removes it. Dropped on Notes, text files
  open there and anything else (a PDF, a picture, a folder) is attached to the
  chat. In the chat, click a picture to see it larger, a text file to open it
  in Notes, a document to open it, and anything else to show it in its folder.
  The agent gets the full path of each one, and models that can see pictures
  get the pictures too.
- Documents: attach a PDF, Word, Excel, PowerPoint, OpenDocument, RTF or EPUB
  file with + (or name it in your message) and the agent reads its text -
  PDFs page by page, spreadsheets as tables, slides with their speaker notes.
  Scanned PDFs that are only pictures can't be read.
- Sound files: attach or name a WAV, MP3, M4A, FLAC, WMA or other sound file,
  or a video, and the agent measures its sound: loudness, peaks and clipping,
  silences and background noise, stereo, frequency balance, the strongest
  tones, pitch, tempo and a rough key. Models that can see images also get a
  waveform and a spectrogram. It can't make out words or name a song. MinGent
  reads WAV and AIFF itself and other formats with the decoders built into
  Windows. When it needs more, the agent asks for one thing at a time: the
  beat and bar times, the hits (kick, snare, hi-hat), the notes of a melody,
  the sections of a song, or where a second file lines up with the first
  (to the sample when they share audio, and whether they drift apart). For
  a sound it made in code, it can listen to it: how it starts and ends,
  clicks between notes, pitch and tuning, tone and aliasing, clipping and
  DC offset. Long lists go to a .csv file in the workspace instead of the
  chat.
- It also makes Word, Excel, PowerPoint and PDF files: ask for "a Word report",
  "slides about ..." or "a spreadsheet of ..." and it writes the file, with
  headings, tables, pictures, formulas and links. Slides split at --- lines or
  headings, and what doesn't fit continues on the next slide. Excel files get
  real numbers and dates; formulas that could reach the internet or other
  files stay plain text. An existing document is never changed in place - the
  agent writes a complete new version. Open the result with the Open link in
  the chat. The agent uses this instead of installing Python libraries or
  other programs; only when you want something it can't do (custom fonts,
  colors or an exact layout) does it ask before using another tool, and it
  uses a tool you name.
- Scripts: the agent uses what is already on your PC. Python only if it is
  installed; otherwise PowerShell for small jobs and C#, compiled with the
  compiler that comes with Windows, for heavy work such as generating sound.
  It doesn't install a language just for one task.
- Math: formulas the model writes in LaTeX are typeset - $...$ or \(...\)
  inside a sentence, $$...$$ or \[...\] on their own lines. Selecting a formula
  copies it as plain text with real symbols; "Copy" under the answer copies
  the LaTeX.
- If a reply stops because of an error, "Continue" picks up where it stopped.
- A chat's "..." button, or a right-click, renames, archives or deletes it
  (F2 and Delete work too). Deleting really deletes: the chat file is
  overwritten before it is removed, and what the agent remembered in that
  chat is forgotten too. It can't be undone, so MinGent asks first. Older
  saves of a chat are overwritten as well each time it is saved.
- The ring under the message box shows how full the model's context window is
  (amber at 70%, red at 90%). Long chats are summarized automatically when
  they get close to the limit; click the ring and "Compact now", or type
  /compact, to do it sooner. Your latest message is always kept as it is.
  The summary is written from the whole chat; your messages, your answers to
  the agent's questions, the plan you approved and what helpers found come
  first when it doesn't all fit.
  The 200 most recent chats are kept; archived chats are kept until you delete
  them.
- Commands: type / in the message box and a list of them pops up - /compact,
  /new (or /clear) for a new chat, /temp for a temporary chat, /plan to switch
  plan first on or off, /skill to write a new skill, /schedule to set up a
  task that runs by itself, /project to choose the folder the chat works in.
  Your skills are in the same list (see Workspace). Pick one with the arrow
  keys and Enter or Tab, or click it. Settings > General lists the commands
  too.
- Temporary chat: the dashed bubble at the top right of a new chat. It is not
  saved to your chats and memory stays off. It's gone as soon as you leave it
  (a new chat, another chat from the list, or closing MinGent), and the
  built-in browser's history is cleared then (after a crash, the next time
  MinGent starts); sign-ins stay. Click the bubble and "Keep this chat" to
  save it after all.


KEYBOARD
--------
  Ctrl+Shift+N         new chat
  Ctrl+Shift+A         jump to the message box (opens the agent if closed)
  Ctrl+Alt+Space       from any program: bring MinGent to the front with the
                       message box ready (off until you choose it, see below)
  Enter / Shift+Enter  send / new line
  Shift+Tab            plan first on / off (in the message box)
  Esc                  stop the agent (in the chat or the message box)
  /                    in the message box: list the commands and skills
  Ctrl+Shift+E         show / hide Notes
  Ctrl+Shift+B         show / hide the browser
  Ctrl+,               settings
  Ctrl +  -  0         zoom the chat or Notes in / out / back (whichever has
                       the keyboard focus; Ctrl + mouse wheel works too)
  Ctrl+C / Ctrl+A      in the chat: copy the selection / select everything

  Notes:
  Ctrl+N or Ctrl+T     new tab              Ctrl+F           find
  Ctrl+O               open                 Ctrl+H           replace
  Ctrl+S               save                 F3 / Shift+F3    next / previous match
  Ctrl+Shift+S         save as              Ctrl+G           go to line
  Ctrl+W or Ctrl+F4    close tab            Alt+Z            word wrap on/off
  Ctrl+Tab             next tab             Ctrl+Z / Ctrl+Y  undo / redo
  Ctrl+Shift+Tab       previous tab         Alt+Left/Right   back / forward

  Browser:  Ctrl+L, F6 or Alt+D go to the address bar. Ctrl+Shift+J shows
            or hides the console, F12 opens the developer tools.
  Models:   Up / Down choose, Enter opens, uses or downloads the selected
            model; the menu key or Shift+F10 lists all you can do with it.

  While a web page in the browser has the keyboard focus, Ctrl+Shift+N, A, E
  and B, Ctrl+, and Ctrl+N, T, O, S, Shift+S and Ctrl+Tab still work; other
  keys go to the page. In Notes, Ctrl+C / Ctrl+X with nothing selected copies
  / cuts the whole line, and double-click selects a word, triple-click a line.

  From any program: Settings > General > Keyboard shortcuts has "Bring up
  MinGent from any program". Choose Ctrl+Alt+Space, Alt+Shift+Space or
  Ctrl+Alt+Enter there, and that shortcut brings MinGent to the front with
  the message box ready, whatever program you are in. It is off until you
  choose one and works while MinGent is running, also when it is minimized.
  Windows gives a shortcut to one program at a time: when another program
  already has the one you chose, MinGent tells you so when you save.


MODELS
------
The model menu is at the bottom right of the message box; each time you open
it, MinGent checks again which models are there and updates the list while
it is open. If you have not picked one, MinGent uses the first that works: a
model in the built-in engine, then LM Studio, then Claude, then OpenAI, then
Gemini, then OpenRouter. When a provider has more models than fit on the
screen, the menu shows the first few (and the one you use) plus "All N
models..." for the full list.

- Built-in engine: runs GGUF models on this PC with llama.cpp 0.7.0. Open
  Models in the sidebar and click "Set up the engine" once - MinGent picks the
  right build for your graphics card (NVIDIA CUDA, Vulkan or CPU; the CPU
  build for NVIDIA cards with less than about 3.5 GB of memory, where the
  processor is faster). It also finds GGUF models LM Studio already
  downloaded, so you do not need two copies. The "Engine" button at the top of
  the model library switches to another build or removes the engine. With
  two or more graphics cards, the engine spreads a model over all of them by
  itself (the CUDA builds use the NVIDIA cards, Vulkan every card except
  graphics built into the processor), and the model library adds up their
  memory when it says what fits.
- Newer llama.cpp releases: off by default. With Settings > Models > "Offer
  newer llama.cpp releases" on, MinGent asks GitHub for llama.cpp's latest
  stable release when it starts and when you open Models, and offers it there
  (with the CUDA runtime that comes with it). Nothing is installed until you
  click Update, and the Engine button goes back to the tested release.
- Downloading models: Models > Download first suggests models that are popular
  right now and sized for this PC (for a 32 GB graphics card, roughly 13B to
  38B parameters), one copy of each from a trusted publisher. Type in the
  search box to search Hugging Face as you type; models that suit this PC come
  first and say "Suits this PC". Pick one and MinGent shows a single Download
  button for the size that fits best: it reads the start of the model file to
  work out how much memory a long chat needs, picks the highest quality that
  still leaves room for one, and says how many tokens of chat fit. "Other
  sizes" lists the rest, each marked "Fits your GPU", "Partly in RAM, slower"
  or "Too big for this PC" ("Fits in memory" or "Tight on memory" on a PC
  that runs models from RAM). Results carry a badge: "Official" (a well-known
  model maker publishing its own model), "Trusted publisher" (well-known
  publishers and quantizers such as lmstudio-community, unsloth, bartowski),
  or "Few downloads". A name like "Qwen3.8-4B-..." from an unknown publisher
  is not from Qwen - check the publisher before the name. Icons next to the
  name show what a model can do - a hammer for tool use, an eye for vision,
  a bulb for thinking, and a red warning when its chat template has no tool
  calls (the agent needs those); hover an icon to see what it means. The
  same icons show under On this PC. Each model shows when it was published
  and last updated, a short description from its model card, its longest
  context, its license, and for every size how much memory it needs.
  Several models can download at once, and downloads keep going when you
  close the library; they show at the top of "On this PC". They resume if
  interrupted and are checked against the SHA-256 checksum Hugging Face
  lists for the file, when it lists one; a file that does not match is
  deleted. Settings > Models > Model folder picks where new models are
  saved.
- Context length: "Auto" (the default) measures the free graphics memory when
  the model loads and picks the longest context that fits, up to the most the
  model supports. Hover the ring under the message box to see how much of it a
  chat uses. Settings > Models also has fixed sizes, and "Compact" context
  memory (the default) fits about twice as much as full precision.
- Claude, OpenAI, Gemini or OpenRouter: paste your API key in Settings >
  Models. MinGent checks it right away and says whether it works, and a key
  pasted in another provider's row is not sent anywhere. The link next to
  each key opens the page where you make one (Gemini keys come from
  aistudio.google.com, and free keys work, with daily limits). Change and
  Remove replace or delete a saved key. OpenRouter gives one key for models
  from many makers; MinGent lists those that can use tools, most popular
  first, with their prices, and the model menu's "All N models..." list has a
  search box.
- Other model servers: if you already run one, Settings > Models > Other
  model servers takes any OpenAI-compatible address (Ollama, vLLM, another
  llama.cpp...). LM Studio is found by itself once its server runs (Developer
  tab), and then shows up in the model menu: MinGent asks LM Studio to load
  the model you pick, and "Use the model loaded in LM Studio" follows whatever
  you load there.
- Thinking: models that can think show a "Thinking" chip under the message box
  to pick how much (off is fastest). A new level counts from your next
  message, also in the middle of a chat. Local models that always think, such
  as DeepSeek R1 and QwQ, have no off: low is their fastest. The model's
  thinking can be shown in the chat.

Tips for local models
- Load models with a context of at least 32K tokens (64K-128K is better for
  bigger jobs). A 4K context is too small for agent work. The built-in engine's
  "Auto" context picks the longest that fits, but can go as low as 16K on a
  small graphics card.
- Models trained for tool use work best (Qwen 3, Gemma 3, Llama 3.x, Mistral,
  GLM...). The model menu marks models LM Studio says are not trained for tools.
- If a thinking model keeps running out of room before it answers, pick a
  lower level on the Thinking chip, or load the model with a bigger context
  (the room for its answer grows with the context).


HOW IT WORKS
------------
It works like a coding agent: it looks at your files, makes a plan for bigger
jobs, edits, runs tests or builds, checks the result the way a person would
(runs the script, opens the page, clicks through the program it built) and
fixes what went wrong.

Made for local models
- MinGent does a lot of the work around the model, so smaller models get much
  further: it understands tool calls written in other agents' styles and
  repairs broken or misnamed arguments, gives the model precise hints when
  something fails (the closest matching lines when an edit misses, "did you
  mean" for wrong paths, the PowerShell version of a bash command), refuses
  placeholder code such as "// ... rest unchanged", checks JSON / XML / bracket
  balance after every write, and nudges the model when it stops early, forgets
  its checklist, or says "done" without having tested anything.
- When a step keeps failing the same way, MinGent tells the model what is going
  on and what else it can try (another route, or asking you), instead of
  letting it repeat itself. A repeated step is only stopped when it keeps
  giving the same result: another screenshot of a page that changed, or
  another key press, always goes through. Runs that only update the
  checklist or repeat an approved plan, and research in plan mode that never
  ends, are stopped after a few rounds, and the agent says where it is.
- Web search happens in the built-in browser, so you see it: Google first,
  then Bing, then DuckDuckGo when Google has nothing. When Google answers with
  a CAPTCHA, MinGent asks you to solve it in the browser pane and carries on
  with Google once you're through; "Skip Google" searches with Bing instead
  and doesn't ask again for 30 minutes. Google hides each result's address
  behind its own link, so MinGent looks up the real address and the agent gets
  normal links it can read and cite. Reading a page from its address stays a
  direct download, without the browser. Searches use the built-in browser's
  own sign-ins: if you signed in to Google there, they show up in that
  account's search history like your own. When the browser is off, searches go
  to DuckDuckGo, DuckDuckGo Lite and Bing directly. Results that have nothing
  to do with the query are left out (search engines sometimes answer automated
  searches with unrelated pages).
- Its memory of the conversation is kept in the shape the model's cache
  expects, so long sessions don't reprocess the whole chat at every step.
- It recovers from the ways local servers fail: a reply that runs out of room
  while still thinking is asked again with more room or told to think more
  briefly, a reply the server could not parse is retried, a tool call cut off
  by the output limit does not break the chat, models whose templates need
  strict user / assistant turns (Mistral Small 3.2, Ministral 3) work after
  tool results, and tool output too dense for the context is cut to fit using
  the server's own token count.

Helpers
- For bigger jobs the agent can hand a focused task to a helper: an "explorer"
  that researches code or the web, a "reviewer" that checks finished work and
  runs it, or a general helper. Each helper starts with a fresh, empty context
  and only its short report comes back, so the agent's own context stays small
  - which matters a lot for local models.
- Reviewers and general helpers check web pages in the built-in browser. A
  reviewer only opens pages on this PC or the local network there, so it
  can't change anything on websites you are signed in to. A helper can't see
  other programs' windows, so MinGent doesn't let it open Chrome, Edge, File
  Explorer or other programs' windows, and dev servers it starts don't open a
  browser either. When a helper has used the browser, the agent reads the page
  again before it clicks or types there.
- After the agent changes a lot of code, a reviewer checks the work before the
  final answer (Settings > Agent > Tools to switch this off). When the agent
  says it is done, the chat notes that the review comes next, so what the
  agent wrote before it is not the final answer yet. The final answer comes
  after the review and covers what the review found.
- Each step a helper takes shows as a row under its card, while it works and
  later. If you send a message while a helper works, the helper keeps going
  in the background and the agent answers you; the report is added when the
  helper is done. Stop ends background helpers too.
- With a local model, helpers run one after another on the same loaded model,
  so they need no extra graphics memory, only time. With Claude, OpenAI,
  Gemini or OpenRouter, explorers can run at the same time; reviewers and
  general helpers run one at a time, since they share the browser and the
  work folder.
- With Claude, OpenAI, Gemini or OpenRouter, the agent can also start an
  explorer in the background and keep working while it researches. Its report
  shows up in the chat when it is done, and the agent waits for it before it
  gives its final answer.

Tools
- Every tool the agent has is part of MinGent's own C# code: there are no
  plugins, no MCP servers and nothing that is downloaded while it runs.
- With Claude, OpenAI, Gemini or OpenRouter, the file reads, file searches and
  page reads the agent asks for in one step run at the same time, up to six at
  once. A read that comes after a change in the same step waits for that
  change. With a local model they run one after another.
- Each tool's description takes room in the model's context, which matters
  with a small local model. Settings > Agent > Tools switches whole groups on
  or off and shows what each one costs: web search (about 200 tokens), the
  browser (about 1,600), desktop programs (about 1,700) and helpers (about
  250). Files, commands, sound files and planning are always on (about
  2,950). The browser and desktop-program tools also switch off by
  themselves when the model's context is under 16K and 24K tokens.
- With web search and pages off, the agent still opens addresses on this PC
  and your local network, such as a development server.

Memory
- The agent remembers lasting facts about you and this PC between chats (your
  hardware, installed tools, where projects live, how you like things done).
  Settings > Memory shows everything it saved; edit or delete any line, or
  switch memory off. It refuses to save facts that look like passwords or
  keys. Temporary chats don't use memory at all.

Scheduled tasks
- Settings > Scheduled (or /schedule in the message box) sets up tasks that
  MinGent starts by itself: every day, Monday to Friday, every week, every
  month or once, at a time you choose. You write what to do the way you
  would ask in the chat.
- A task runs while MinGent is open. Each run is a new chat named after the
  task. It starts when the agent is not busy and you are not in the middle
  of something in MinGent: no half-written message, no question waiting,
  and MinGent either in the background or untouched for a minute. A run
  that was missed while MinGent was closed is made up for once, the next
  time it is open.
- A run works like a task you start yourself. In Ask mode it waits for your
  OK, and the window title and the taskbar button show that it is waiting.
- A task works in the default folder or in the project you pick for it. When
  that folder can't be used (a drive that is not plugged in, a folder that
  was moved), the run is skipped and a chat with the task's name says so.
- Only you can add or change scheduled tasks: the agent has no tool for it,
  so nothing it reads can set one up. They are saved on this PC only.

Testing programs it builds
- It can start a desktop program it wrote, read its window (buttons, fields,
  menus), click and type in it like a person, and take screenshots (with models
  that can see images), also a close-up of one part of the window when
  something is too small to read. It presses any key or shortcut (matched
  to your keyboard layout), holds keys down (up to 10 seconds at a time),
  also several at once
  (Shift+W, W+D), right-, middle-, double- and triple-clicks, clicks with Ctrl
  or Shift held, presses and holds, scrolls, hovers and drags, and moves the
  mouse by a set amount, the way 3D games turn the view. A pointer in your
  accent color shows where it points and clicks. This moves your real mouse
  pointer and sends real key presses to that window, so before each step it
  waits until you have left the mouse and keyboard alone for a second (four
  seconds at most), and it stops typing as soon as another window comes to
  the front. It is told never to fake input with PowerShell SendKeys and the
  like, which would go to whatever window has focus. Keys that act on Windows
  itself (the Windows key, Alt+Tab, Ctrl+Esc, Ctrl+Shift+Esc, PrintScreen) are
  refused, and pasting your clipboard into a program asks first, unless the
  clipboard still holds what the agent itself just copied.
- It only ever controls programs started by its own commands while MinGent
  runs, also in an earlier chat - never browsers, mail, chat, password
  managers, remote-access tools or system tools - and it asks before using a
  program that is not part of the project in the workspace. Programs that
  Windows opens for one of its commands (Store apps such as Calculator, or a
  program opened through a file type) count as its own when they start while
  that command runs; because such a program can't be fully told apart from
  one you opened at the same moment, MinGent asks before the agent looks at
  it, also in Auto mode, unless you switched off "Starting and using other
  programs" in Settings > Safety. Programs that were already open stay off
  limits, unless you switch on "Programs you opened yourself" (below).
- Programs you opened yourself (Settings > Agent, off by default): the agent
  can list the programs you have open, by name only, and ask to use one.
  MinGent asks you for each program, once in each chat, even in Full auto,
  and the agent sees its windows only after you allow it. Browsers, File
  Explorer, terminals and code editors can only be looked at, because a
  click there can run a script or a command. In any other program you
  allow, the agent can do whatever the program can do, macros and scripts
  included; the question says so. Mail, chat, AI chat apps, password
  managers, money and remote-access programs, Windows' own settings and
  security tools, and MinGent itself are never offered; MinGent knows them
  by their program names, so check the name in the question. When a chat
  that has read web content goes on to click or type in a program you
  allowed earlier, MinGent asks once more. The agent always says which of
  your windows it means, and MinGent doesn't rearrange them. Switching this
  off ends what you allowed in open chats.
- It can open its own terminal window (Windows Terminal, or a console where
  that is missing) and type commands there while you watch, for example to
  manage a server over ssh. Before Enter, and before any other key or click
  that could run the line, MinGent checks the command the way it checks the
  agent's other commands and asks before risky ones. On a server reached with
  ssh, commands that only look around (ls, cat, logs, status) run without
  asking in Auto mode and anything else asks, unless you switch off "Changes
  on servers over ssh" in Settings > Safety; reading password, key or token
  files there asks like it does on this PC. After keys MinGent can't follow
  (history, Tab completion, clicks), the next Enter or special key asks, also
  in Auto mode, and a line you refuse is cleared so it can't run by accident.
  The agent can't paste into a terminal. Terminal windows that were already
  open stay off limits. Windows Terminal runs all its windows in one
  program, so a window you open in the moment before the agent's appears
  could be taken for the agent's; MinGent asks before the agent types in each
  terminal window. A Windows Terminal window the agent opened stays open
  after MinGent closes, with whatever runs in it, so close it yourself.
- When the agent starts working in a program window that covers MinGent,
  MinGent takes one half of the screen and the program the other, and gets its
  old size back when the program closes (unless you moved it meanwhile). For a
  program the agent started itself, this happens before MinGent asks you
  anything about it, so the question isn't hidden behind the program. For a
  program Windows opened for one of its commands, MinGent asks first, comes to
  the front to do so, and moves the windows after you allow it.
  Settings > Agent > "Put MinGent and the program side by side" turns this
  off.
- While it uses a program, that window has an outline in your accent color
  and a small "MinGent" tag. Clicks go straight through the outline, it
  shows in your screenshots and screen recordings but never in the agent's,
  and it fades when the agent is done. When the program opens a Save or
  Open dialog, the agent works in the dialog, and it asks you before typing
  in it (the same Settings > Safety row decides this in Auto mode).


ASK, AUTO OR FULL AUTO
----------------------
- The "Auto" switch under the message box decides how much it asks; the
  arrow next to it has all three levels.
  Ask (switch off): it asks before running a command, before using the
  internet, and before changing files outside the workspace. Each request is
  a card in the chat with Allow / Allow for this chat / Deny.
  Auto (switch on): it works on its own and asks only before risky things
  (see below).
  Full auto (red): it asks nothing at all. It warns you the first time and
  goes back to Auto when you close MinGent. With a local model it also
  points out that smaller models are easier for a web page to trick, so a
  frontier model such as Claude, GPT or Gemini is the safer choice.
- What Auto still asks about is listed in Settings > Safety, one switch per
  kind, all on until you switch one off:
    Installing software and packages
    Starting and using other programs
    Deleting widely or outside the work folder
    System changes, admin rights and hidden code
    Passwords, keys, the clipboard and MinGent's own data
    Uploading, publishing and rewriting git history
    Changes on servers over ssh
    Payments, passwords and account actions
    Downloading programs and archives in the browser
    Running code in the browser console
- These rows also cover commands that hide what they do or reach outside the
  work folder: deletes of files picked by another command, a loop or a script
  outside the work folder, commands that delete, empty, replace or move
  files outside it (except in the temp folder), emptying the whole work
  folder, throwing away changes git hasn't saved, links or junctions to
  other folders, robocopy /MIR, emptying the Recycle Bin, generated or
  encoded code, and anything that touches MinGent's own settings, chats or
  browser data. Switching a row off stops those questions too.
- Sending one of your saved API keys to a website or over the internet asks
  first in Ask and Auto, and is refused in Full auto. The check looks for the
  key as written, so a command or script can get past it.
- These checks read the command; they catch common mistakes and tricks, but
  they cannot see every possible command or what a script does once it runs.
  Auto is for work you trust and Full auto for work you can afford to lose;
  use Ask when in doubt.
- Prompt-injection guard (on by default): web pages can contain text written to
  trick an AI. Once the agent has read web content in a chat, Auto mode asks
  again before commands, before writes outside the workspace, before
  changes to your skills (the agent follows them in later chats, so in any
  mode but Full auto MinGent asks), before
  everything in the list above (also rows you switched off), and before
  opening addresses or searches that carry a lot of data, something that
  looks like a password or key, or data hidden in a long site name. Page
  text is passed to the model marked as untrusted data, with a note not to
  follow it, and that includes page text that shows up in error messages. A
  model can still be fooled.
- In every mode, also Full auto, the agent's own file tools never write into
  Windows, Program Files, the Startup folder, shell profiles or MinGent's own
  data folder, also not through a link or junction that points there.
  Commands are checked by the rows above instead: in Full auto they run
  without asking, and only Windows' own permissions protect Windows and
  Program Files. So don't start MinGent as administrator.
- Sandbox (Settings > Safety, experimental, off by default): the agent's
  commands run with low rights. They can still read files and use the
  internet, but they can change files only in the work folder and in
  MinGent's own temp and cache folders (in AppData\LocalLow\MinGent), and
  they can't change other programs, your settings in the registry, or
  MinGent's own data, nor read MinGent's settings, keys, chats or notes
  backups. They can read the built-in browser's data, which MinGent leaves
  alone so the browser keeps working.
- To let commands work in a folder, MinGent allows low-rights programs to
  change files there, except its .git folder, files linked from elsewhere
  and links to other folders. It never does that for a whole drive, your
  user folder, Desktop, Documents, Downloads, OneDrive, a Windows or program
  folder, or the folder MinGent runs from; commands then can't change files
  there at all. The change stays when you turn the sandbox off; it only
  lets low-rights programs write there.
- A command runs outside the sandbox only after you allow that command, or
  in Full auto: when the agent asks for it, when MinGent asks about the
  command anyway (installs, opening programs), or when the sandbox can't
  start. A row you switched off in "What Auto still asks about" keeps such
  commands in the sandbox instead of letting them out without asking.
- The sandbox limits what a command can change, not what you run later: a
  script or program a sandboxed command changed in the work folder does
  whatever it now says when you or another program run it. If a sandboxed
  command replaces the work folder's .git folder, MinGent tells you, because
  git run outside the sandbox would then use its hooks and settings.

Undo
- After each reply that changed files with the agent's file tools, "N files
  changed - Undo" takes back what those tools did: restores the old file
  contents, brings back the files and folders it deleted, and moves files it
  created to the Recycle Bin. Changes made by commands it ran (a build, a
  script, Remove-Item) cannot be undone. Edits to open Notes tabs are also
  normal undo steps (Ctrl+Z).
- The agent is told to delete with its own delete tool, which moves files to
  the Recycle Bin. Undo brings them back too, except files over 32 MB and
  folders over 2,000 files or 64 MB, which come back from the Recycle Bin.
  Files deleted by a command (Remove-Item, del, a script) are gone for good.
  The delete tool never deletes a whole drive, your Windows folders or the
  workspace itself, and outside the workspace it asks first, also in Auto
  mode, unless you switch off "Deleting widely or outside the work folder".
  In Auto, a command that deletes inside the workspace, or a single file in
  the temp folder, does not ask.
- By default files are saved after the agent edits them, so tests and builds
  see the new code (Settings > Agent to switch this off).

Workspace
- When the open Notes tab is a saved file, the agent works in that file's
  folder, unless that folder is a whole personal folder such as Documents,
  Desktop or Downloads. Otherwise new files go to Documents\MinGent Files
  (changeable in Settings > Agent). You never have to create a folder first.
- Unsaved Notes tabs can be read and edited too: it refers to them by their
  tab name.
- Projects: the folder button at the top of a chat shows where that chat
  works. Click it and "Choose a folder..." (or type /project) to make the
  chat work in a folder of your choice. MinGent remembers each folder you
  choose as a project, named after the folder, and lists your projects in
  that menu and above your chats. A chat stays in its project when you open
  it again, and "No project" goes back to the open file's folder or the
  default folder. A whole personal folder such as Documents, Desktop or a
  drive can't be a project, because inside its work folder the agent changes
  files without asking each time; choose a folder inside it.
- Project notes: ask it to "remember" something about a project and it writes
  it to MINGENT.md in the project folder; it reads that file at the start of
  every request.
- Project instructions: when the project has an AGENTS.md (or a CLAUDE.md),
  the file many coding agents read for how to build, test and write code in
  a project, the agent is told where it is and reads it before it changes
  the project's code. That costs one line of the model's context until
  the file is needed. MinGent looks in the work folder and, inside a Git
  repository, in the folders above it up to the repository's top folder;
  the nearest file counts. The file can't change the agent's safety rules
  or allow anything on your behalf, and what you say in the chat comes
  first.
- Skills: a skill is a how-to for a job that comes up again, written once
  as a Markdown file ("how we write release notes", "how to make the weekly
  report"). Type / in the message box and your skills show up next to the
  commands; pick one and add what this run is about. /skill starts a new
  one in Notes, and you can ask the agent to save what it just did as a
  skill. Skills live in the Skills folder inside the default folder
  (Settings > Agent). The agent only gets one line per skill, its name and
  when to use it, and reads the file when a task matches or when your
  message starts with /name. A skill folder made for Claude (a folder with
  SKILL.md in it) works too: put it in the Skills folder.
- Selected text in Notes is sent with your message (the chip above the message
  box shows what; click its x to leave it out).


THE BROWSER
-----------
- A real Chromium-based browser (Microsoft Edge WebView2, part of Windows 11 and
  current Windows 10) in a pane next to the chat. The agent can open pages, read
  them, click, type, scroll and take screenshots (with models that can see
  images); you watch it happen and can take over any time.
- To websites it looks like ordinary Microsoft Edge: MinGent adds nothing to
  the pages and sets no automation flags. The agent reads and clicks from a
  separate, private script context that pages cannot see or tamper with. The
  console is the exception: see below.
- It has its own profile, separate from your normal browsers: sign in there to
  the sites you want the agent to use, and the sign-ins stay. Every chat
  starts on a fresh page, so a new chat never carries on in the last chat's
  tab or its back / forward history. The agent closes the pane when it no
  longer needs it, or when you ask it to (the same goes for the notes pane);
  a pane you are using stays open unless you ask. Settings > Privacy >
  "Wipe browser data" signs you out of every site and erases the profile's
  history, cache and site data.
- When a site answers with a bot check or blocks automated access, the agent
  is told what happened and what else it can try, and "verify you are human"
  checks get a few seconds to finish by themselves. CAPTCHAs are left to you:
  the agent stops and asks you to complete them. (Settings > Agent has an
  experimental switch that lets the agent try them itself, the way a person
  does: from what it sees, with clicks and typing. Its screenshots then
  zoom in on the CAPTCHA, or add a zoomed picture of one that sits
  inside the page. It picks several picture tiles and Verify in one step,
  types the characters of a text CAPTCHA, and sees the CAPTCHA again after
  every click in it. After five answers that fail it hands the CAPTCHA to you;
  hCaptcha, whose puzzles have pages, gets ten, and Arkose, with its
  rounds, fifteen.)
- Give the agent a YouTube link and it reads the video's title, channel,
  date, description and transcript. Long pages and transcripts come in parts.
- Watching videos: a model that can see images can also look at a video. The
  agent opens it in the browser (the video's page, such as a YouTube page, or
  a video file on your PC: MP4, WebM or MOV) and gets pictures of it: nine
  moments spread over the video on one sheet, each with its time, or up to
  four moments it picks as full pictures. It can do the same for a part of
  the video to look closer. The video is paused while it looks. What is said
  in a YouTube video it reads from the transcript, and the sound of a video
  file it measures (see Sound files). It can't look at a live stream, at a
  video that sits inside a frame from another site (it opens the video's own
  page instead), or at protected films from streaming services.
- The agent's pointer, in your accent color, shows where it points, clicks,
  drags and scrolls in the browser. It shows in your screenshots and screen
  recordings, but not in the agent's own screenshots.
- "Sign in with..." and payment pop-ups open in their own small window with the
  address shown at the top. Links that open a new tab open in the same pane,
  the same way as a normal click. Messages, confirmations and "leave this page?"
  questions from pages appear as MinGent dialogs.
- A local .html preview can only load files from its own folder.
- Giving a file to a page: the agent can put a file from your PC into a
  page's file field, the way you would choose it in the page's file picker,
  for example to attach it to a form. MinGent asks you first and names the
  file and the site; Auto asks too, unless you switch off "Uploading,
  publishing and rewriting git history" in Settings > Safety, and it always
  asks once the chat has read web content. Files in sensitive places ask
  the way reading them does. The agent does not click file fields, because
  that opens a window only you can use. On pages that only make their file
  field at the moment you click their button, choose the file yourself.
- When the agent clicks by position in a screenshot, a click on a payment,
  account or verification button (also inside payment and CAPTCHA boxes from
  other sites) asks you first.
- Camera, microphone, location and notification requests: you get the normal
  Edge prompt when you are browsing; while the agent is in control they are
  refused automatically, just for that moment - the site can still ask you
  later. Downloads you start go to your Downloads folder as usual; downloads the
  agent starts (also ones that begin a moment after its click) go to the
  workspace's downloads folder. Ask mode asks before every download; Auto asks
  before programs, scripts and archives unless you switch off "Downloading
  programs and archives in the browser"; Full auto doesn't ask.
- Google does not allow signing in to Google accounts from apps' built-in
  browsers (this applies to every app, not only MinGent). When a page sends
  you there, MinGent shows "Open in your browser" to continue in your normal
  browser.
- The console: "Console" in the browser's "..." menu (or Ctrl+Shift+J) opens
  it under the page. It shows the page's errors, warnings and logs, the code
  the agent ran there and what came back, and has a line where you can run
  JavaScript yourself. "Developer tools" in the same menu (or F12) opens
  Edge's full developer tools in their own window.
- The agent can read the console and run JavaScript in the page to look into
  a problem, for example an error on a page it built. After each browser step
  it is told about new errors on pages from this PC (files, localhost).
  Websites can notice when their console is read, and some turn such
  browsers away, so MinGent reads a website's console only once you or the
  agent ask for it, and only for that site.
- Running code in the console asks first in Ask mode, and in Auto mode while
  "Running code in the browser console" in Settings > Safety is on (switching
  it off shows a warning). On a website the question is red: code there can
  do anything you can do on that site while signed in. Full auto doesn't ask.
- The question says in plain words what the code does. MinGent reads the
  code itself and names what matters: reading or changing cookies and data
  the site stored in the browser, reading form fields or saved sign-ins,
  sending requests (and to which other site), sending messages to other
  windows or frames, changing the page, calling the page's own functions
  (by name) and leaving code running in the page. When the agent says what
  the code is for, that shows first and MinGent's own reading under it, so
  a description can't hide what the code does. When MinGent is unsure, it
  names more rather than less. It is still a reading, not a guarantee, and
  the code itself is shown below it.
- MinGent refuses console code that does what the other browser tools do -
  clicking, typing, key presses, scrolling, submitting forms, opening pages
  or windows - so those keep going through the checks for buy, password and
  account buttons. It also refuses code that hides what it runs (eval, code
  in strings, encoded text, calls through computed names), and on websites
  network requests, reading cookies, storage, saved sign-ins or form fields,
  code that reaches into a CAPTCHA, and any code while a CAPTCHA is showing:
  CAPTCHAs are only ever solved by looking at them and clicking or typing.
  Like the command checks, this reads the code: it catches the common
  cases, not every possible trick.


NOTES
-----
Notes is a complete text editor - handy for pasting answers from other AI
chats, or for looking at the files the agent works on.

- Tabs: open as many files as you like. Drag tabs to reorder them, middle-click
  to close, double-click the empty tab bar for a new tab.
- Each chat has its own notes: the tabs you open or the agent creates in a chat
  only show in that chat, and only that chat's agent can see them. Switching
  chats switches the tabs; a new chat starts with an empty note. A file open in
  two chats is the same tab in both, so changes never split. A temporary
  chat's notes close when you leave it or close MinGent (MinGent asks first if
  one has unsaved changes), and deleting a chat closes its notes. Until then
  their unsaved text is backed up like any other, so a crash doesn't lose it.
- The agent's files don't open in Notes by themselves: Notes stays the way
  you left it, and the chat shows each change and links to the file.
  Settings > Agent > "Show the files the agent writes" opens the files it
  creates or edits in tabs while it works, if you like to watch them.
- It remembers everything when you close it, like Windows 11 Notepad: all tabs,
  unsaved changes and untitled notes are back the next time you start it. Open
  tabs are also backed up every few seconds and written straight to the disk,
  so even a crash or power cut does not lose your text. If the list of open
  tabs is ever damaged, the backed-up text comes back as "Recovered" tabs.
- When a file changes on disk while you have unsaved edits, MinGent asks before
  reloading (Cancel is the default), and Ctrl+Z after a reload brings your
  edits back.
- Syntax highlighting for about 45 languages, chosen by file extension. Click
  the language name in the status bar to change it.
- Highlights the current line, matching brackets, every occurrence of the
  selected word, all search matches, and edited lines (orange bar = unsaved,
  green bar = saved).
- Find / replace (match case, whole word, "3 of 17" counter), go to line, word
  wrap, zoom, font choice - all in the "..." menu above the tabs.
- Keeps each file's encoding (UTF-8, UTF-8 with BOM, UTF-16, ANSI) and line
  endings (CRLF / LF), shown in the status bar and changeable with a click.
- Notices when another program changes an open file and reloads it (it asks
  first if you have unsaved changes in that tab).
- Files can open straight into Notes from Explorer: see OPEN FILES WITH
  MINGENT below.
- "Preview in the browser" (in the "..." menu) shows the HTML file you are
  editing.


LOOKS
-----
Dark with a green accent, and on Windows 11 a title bar and rounded menus that
match. Settings > General > Appearance switches to Light, or to System to
follow Windows; Accent color > Windows uses the accent color you picked in
Windows. Windows high-contrast mode always takes over while it is on, and
"Animation effects" off in Windows turns off MinGent's small animations.

Settings > General > Reset puts every setting back to how MinGent comes. Your
chats, memory, API keys, instructions, models and offline mode stay.


OPEN FILES WITH MINGENT (optional)
----------------------------------
Settings > General > Files > "Show MinGent in Open with" > Add puts MinGent in
Explorer's "Open with" menu for text and code files (.txt, .md, .json, .log,
.py, .js, .html, .ps1 and more). Files you open that way appear in Notes, also
when MinGent is already running.

To open them with a double-click, press "Open Windows Settings" next to it and
pick MinGent for the file types you want: Windows lets only you, not apps,
change default apps. (Without Add you can still do it by hand: right-click a
file > Open with > Choose another app > More apps > "Look for another app on
this PC" > MinGent.exe > Always.)

Add writes a few per-user entries under HKEY_CURRENT_USER (Software\Classes,
Software\RegisteredApplications and Software\MinGent); Remove takes them out
again. If you move MinGent.exe, the entries follow it the next time it starts.


WHERE YOUR DATA IS
------------------
  %LOCALAPPDATA%\MinGent\settings.ini   settings; API keys encrypted with Windows
                                        DPAPI: other Windows accounts and copies
                                        of the file on other PCs can't read
                                        them, programs you run can
  %LOCALAPPDATA%\MinGent\session.txt    the list of open Notes tabs
  %LOCALAPPDATA%\MinGent\backup\        text of tabs with unsaved changes
  %LOCALAPPDATA%\MinGent\chats\         your chats (the 200 most recent;
                                        temporary chats are never written;
                                        archived chats are in chats\archive)
  %LOCALAPPDATA%\MinGent\memory.md      what the agent remembers about you
                                        (also shown in Settings > Memory)
  %LOCALAPPDATA%\MinGent\scheduled.json your scheduled tasks
  %LOCALAPPDATA%\MinGent\projects.json  the folders you chose as projects
  %LOCALAPPDATA%\MinGent\Browser\       the built-in browser's profile
  %LOCALAPPDATA%\MinGent\engine\        the built-in engine, if you set it up
  %LOCALAPPDATA%\MinGent\models\        models downloaded in the model library
                                        (or the folder you picked in
                                        Settings > Models > Model folder)
  %LOCALAPPDATA%\MinGent\connections.log only with "Keep a connection log file"
  %LOCALAPPDATA%\MinGent\error.log       only with "Keep an error log"
  %TEMP%\MinGent\                        the agent's PowerShell commands while
                                        they run; erased when each one ends
  Documents\MinGent Files\               what the agent saves when no other
                                        folder is chosen (your files), with
                                        your skills in its Skills folder
Nothing goes into the registry, except the Open with entries if you add them.

Uninstall: Settings > Privacy > "Erase everything" removes all of the above
and the Open with entries; you choose whether the engine, the models and
Documents\MinGent Files go too. Then delete the folder with Build-MinGent.bat
and MinGent.exe.


PRIVACY
-------
- MinGent sends nothing about you anywhere: no account, no telemetry, no
  crash reports, and no update checks except when you click "Check for
  updates" or switch on newer llama.cpp releases. With a local model your
  chats stay on your PC; only the web searches and pages the agent uses go
  out.
- Settings > Privacy > Offline mode: MinGent makes no internet connections -
  no cloud models, web search, web pages, update checks or downloads. Local
  models and pages on this PC or your local network keep working. The
  built-in browser restarts when you switch it, and while offline it only
  reaches this PC and your local network. The agent may not run commands
  that need the internet (downloads, package installs, git clone, fetch, pull
  and push, ssh, web addresses, tools like ping or nslookup aimed at the
  internet, file shares outside your network). These checks read the
  command, and offline mode is not a firewall: a program or script that goes
  online by itself still can.
- Settings > Privacy > Connections lists every internet address MinGent
  connected to since it started, and why, including where a download or page
  ended up after a redirect. For the built-in browser these are the pages it
  opened, not every file a page loads from other sites, and commands the
  agent runs make their own connections. The list is kept in memory; "Keep a
  connection log file" also writes it to connections.log, temporary chats
  included.
- No log files by default. "Keep an error log" writes error details to
  error.log, for a bug report.
- Settings > Privacy > "Erase everything" overwrites MinGent's chats, memory,
  API keys, browser data, logs, notes backups, settings and temporary files
  with random data, deletes them, removes its Open with entries and closes
  MinGent, which starts like new the next time. Files still in use are
  overwritten and deleted right after MinGent closes, and links to other
  folders are removed without following them. It asks about two more things:
  what the agent saved in Documents\MinGent Files (erased unless you switch it
  off) and the engine and models (kept unless you switch them on; they are
  downloads, not your data). Files in your other folders stay.
- A chat that used Claude, OpenAI, Gemini or OpenRouter was also sent to
  that provider (through OpenRouter, also to the model's maker), which keeps
  its own copy under its own rules; deleting the chat here can't reach that.
  The same goes for a temporary chat.
- The built-in browser keeps its own history and sign-ins like any browser;
  deleting a chat does not sign you out of sites ("Wipe browser data" does).
- Like Edge, the built-in browser checks sites and downloads with Microsoft
  Defender SmartScreen, which can send the addresses it checks to Microsoft.
  "SmartScreen for Microsoft Edge" in Windows Security turns that off.
- SSDs move data around internally, so no program can promise a drive never
  kept an older copy of a file. Windows device encryption (BitLocker) protects
  that too.
- Windows itself keeps a few traces of any program you run, such as recent
  files in its Open and Save dialogs.


SECURITY / ATTACK SURFACE
-------------------------
- Uses only the .NET Framework that is part of Windows. MinGent itself has no
  third-party code: every line is in the "src" folder for you to read. No
  automatic updates, telemetry, plugins, macros or scripting engine, and it
  never needs administrator rights. The optional engine is llama.cpp's own
  build and the browser is Microsoft's WebView2.
- MinGent itself connects only to: the model you chose (the built-in engine
  or LM Studio on 127.0.0.1, or api.anthropic.com / api.openai.com /
  generativelanguage.googleapis.com / openrouter.ai / your custom server),
  the providers you saved an API key for (to check the key and list their
  models), Google, Bing or DuckDuckGo when the agent searches (and Google
  once per result, to look up its real address), the pages and YouTube
  videos it reads, the sites you or it open in the browser, huggingface.co
  and its download servers when you search or download models, github.com
  and its download server when you set up, switch or update the engine or
  install a MinGent update, and api.github.com only with "Offer newer
  llama.cpp releases" on or when you click "Check for updates". Notes itself
  makes no network connections. Settings > Privacy > Connections shows each
  one.
- The built-in engine is llama.cpp's server, pinned to one release and checked
  against SHA-256 checksums built into MinGent before it is unpacked (with
  protection against malicious archive paths). A newer release you choose to
  install is checked against the checksums GitHub lists for it. It listens
  only on 127.0.0.1, on a port it picks itself (MinGent connects only after
  the engine reports it), with a random key that changes every start, runs
  without its web UI, with network downloads disabled, and is stopped when
  MinGent closes.
- Commands the agent runs with run_command get no keyboard input and have
  MinGent's own rights, so start MinGent normally, not as administrator (it
  warns you if you do). Stop ends the command that is running. Commands
  started in the background, such as a local web server, keep running until
  the agent stops them or you close MinGent, which ends them and everything
  they started, except a Windows Terminal window (see above).
- The browser pane runs in its own profile. It cannot open local files, except
  an HTML page being previewed and the files in that page's folder. MinGent
  injects no scripts into web pages; the agent's page reading runs in an
  isolated script context through the browser's own debugging interface, which
  is not exposed on any network port.
- Pages, model cards and documents built to keep MinGent busy (with thousands
  of unclosed tags or comments, for example) can't stall it: turning them into
  text stops after a few seconds. A page or document then comes through as
  plain text without its layout, and a model card shows without its
  description.
- Desktop program control is limited to processes the agent started inside its
  own job objects, plus programs Windows started for one of its program-opening
  commands while that command ran (it asks before using those, also in Auto
  mode), and is refused outright for browsers, mail and chat apps,
  password managers, remote-access tools, system tools, and consoles and
  terminals it did not open itself. Terminal windows it opened are found by
  their window, not their process, because Windows Terminal runs all its
  windows in one process.
- Other direct Windows API calls: title bar colors, rounded menu
  corners, the "Animation effects" setting, checking which characters a font
  has (so math symbols never show up as boxes), telling Explorer that file types
  changed (only after you press Add or Remove), the IME window position,
  placeholder text, flashing the taskbar button when the agent needs you,
  sending deleted files to the Recycle Bin, and handing file names to an
  already open MinGent window (a local window message between programs of the
  same user).


REQUIREMENTS
------------
Windows 10 or Windows 11 (they include .NET Framework 4.8).
For the browser: Microsoft Edge WebView2 Runtime (included in Windows 11 and
current Windows 10; free download from Microsoft otherwise).
For the agent: the built-in engine (a graphics card with enough memory for the
model helps a lot), an Anthropic / OpenAI / Gemini / OpenRouter API key, or a
model server you already run, such as LM Studio or Ollama.


USE AT YOUR OWN RISK
--------------------
MinGent is free software under the MIT license (see LICENSE). It is provided
"as is", without warranty of any kind, and the authors are not liable for any
damage or loss that comes from using it.

An AI agent makes mistakes. It can delete or change the wrong files, run
commands that do harm, send things to websites and act on sites where you are
signed in, and a web page can try to trick it. You decide what it may do, and
you are responsible for what it does on your PC and your accounts. Keep
backups, use Ask mode for anything you can't undo, check its work, and follow
the terms of the model providers and websites you use with it.
