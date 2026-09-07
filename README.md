# sysoptimizer

a little windows diagnostic/optimization app i built while learning how to hook a c#
app up to an ai api. the idea: scan the pc, send the scan to gemini, let it suggest
fixes, and only run the stuff i actually say yes to. nothing runs automatically, ever —
that felt like the important rule to not break while building this.

## what you need

- windows 10/11
- [.net 8 sdk](https://dotnet.microsoft.com/download)
- a free gemini api key from [google ai studio](https://aistudio.google.com/apikey) —
  no credit card needed, which is why i went with this instead of the paid apis

## setup

click the green "code" button on github and download the zip and extract all.

1. get a free key at https://aistudio.google.com/apikey (sign in with google, click
   "create api key"). if it says "no cloud projects available," you might need to make
   a project first at console.cloud.google.com/projectcreate, then go back and try again.

2. set it as an environment variable (powershell):
   ```powershell
   setx GEMINI_API_KEY "your-key-here"
   ```
   close and reopen your terminal after this — setx only applies to new windows, learned
   that one the hard way.

3. build it:
   ```powershell
   dotnet build
   ```

4. run it:
   ```powershell
   dotnet run
   ```
   if a recommendation needs admin rights, you have to run your terminal as
   administrator first or that specific one just gets skipped (on purpose, not a bug).

## free tier heads up

google's free tier changes model names more often than i expected — i started this on
gemini-2.5-flash and it got retired within a day of me finishing. if you get a 404
saying a model "is no longer available," it usually tells you what to switch to right in
the error message. you can override without touching the code:
```powershell
setx GEMINI_MODEL "whatever-model-they-say-to-use"
```

## two modes

when you run it, it asks which one you want:

**1) full system scan** — a general health check. good for "is my pc slow for a dumb
reason" type questions.

**2) reproduce-and-capture** — for stuff that only happens when you *do* something.
i added this after trying to figure out why my wifi kept dropping whenever i opened a
game. a regular scan is a snapshot, so it has no idea what happened five minutes ago
when the problem actually occurred — this mode instead watches network adapter status
and the event logs live, starting right before you trigger the issue and stopping right
after, so gemini gets a small focused window instead of a random slice of "recent" stuff
that might not even cover the moment it happened.

## how it actually works

| step | file | what it does |
|---|---|---|
| 1 | `DiagnosticCollector.cs` | read-only scan — cpu, ram, gpu, disks, top processes, startup apps, services, network adapters (+ power-saving settings + known conflict-prone drivers), installed applications, temp/cache folder sizes, active power plan, windows update status, device manager driver errors, and recent system + application event log errors. all read-only, nothing gets touched. |
| 1b | `NetworkCaptureService.cs` | (reproduce-and-capture mode only) watches network adapter status every second and listens for new event log entries in real time, between when you say "start" and "stop." |
| 2 | `GeminiApiClient.cs` (`RequestAnalysisAsync`) | sends the scan (or capture) + the system prompt to gemini, gets back a list of recommendations. still nothing runs at this point. |
| 3 | `UI/ApprovalWindow.xaml` | an actual gui window now instead of typing ids into the console — shows every recommendation with its risk level, evidence, and exact script, with a checkbox next to each one. |
| 4 | `GeminiApiClient.cs` (`RequestExecutionPlanAsync`) | sends only the checked ids back, gets an execution plan with just those. the client also filters the response itself afterward, so even a mistake on the model's end can't sneak an unapproved action through. |
| 5 | `ExecutionEngine.cs` | actually runs the approved scripts through powershell. skips anything needing admin if i'm not running elevated, and double-checks before anything that needs a reboot. |
| 6 | `ActionLogger.cs` | appends every run to `logs/action-log.jsonl` — what ran, when, and whether it worked. |

## file layout

```
SysOptimizer/
├── SysOptimizer.csproj
├── Program.cs                     # ties everything together, mode selection
├── SystemPrompt.txt               # the rules i give the model (safe to edit)
├── Models/
│   └── Schemas.cs                 # the json shapes the model has to respond in
├── Services/
│   ├── DiagnosticCollector.cs     # the full read-only scan
│   ├── NetworkCaptureService.cs   # live capture for reproduce-and-capture mode
│   ├── GeminiApiClient.cs         # talks to google's api
│   ├── ExecutionEngine.cs         # runs only what got approved
│   └── ActionLogger.cs            # writes logs/action-log.jsonl
└── UI/
    ├── ApprovalWindow.xaml(.cs)   # the review/approve window
    ├── RecommendationViewModel.cs # checkbox-bindable wrapper around a recommendation
    └── GuiApproval.cs             # runs the wpf window from the console app's sta thread
```

## things i learned / stuff i'd still want to add

- **nothing auto-runs.** analysis never executes anything on its own, you have to
  check specific boxes in the approval window for anything to happen.
- **double approval check.** even if gemini's execution-plan response somehow included
  something i didn't check, the client code strips it out before it reaches the part
  that actually runs commands.
- **no sketchy commands.** the system prompt tells the model not to use base64 or
  encoded commands, no downloading and running random stuff, nothing like that.
- **admin stuff is gated.** if a script needs elevation and the terminal isn't running
  as admin, it just skips that one instead of trying and failing weirdly or popping a
  uac prompt mid-run.
- **wpf from a console app was a whole learning curve.** wpf windows need to run on an
  sta thread, and my `Main` is `async`, which doesn't guarantee that. ended up just
  spinning up a dedicated thread for the approval window (see `GuiApproval.cs`) instead
  of converting the whole thing into a "real" wpf app — simpler than i expected once i
  found the right approach, way more confusing before that.
- **there's still no undo button.** the action log records what ran, but nothing
  actually reverts anything yet — that's still on the list.
- **the temp/cache size scan is an estimate, not exact.** i skip files that error out
  (in use, permission denied) instead of retrying, so the numbers can be a little low.
- **gpu vram numbers can be wrong on some cards.** wmi reports that field as a 32-bit
  number, which overflows/reports garbage on some gpus with 4gb+ vram. flagged this in
  the code as "best effort."

## next steps i'm thinking about

- add an actual "undo" button that replays the reversible actions backward using the
  action log instead of just recording that they happened
- a proper main window instead of a console + popup combo — right now it's still a
  console app that happens to open a wpf window partway through
- system file integrity checks (sfc/dism) — didn't add this since running sfc
  automatically felt too invasive for something that just scans by default, would want
  it to be an explicit opt-in step
- hardware temperature readings — windows doesn't expose this through a standard api
  without vendor-specific tools, so this one's on hold for now
