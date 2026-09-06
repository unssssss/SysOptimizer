# sysoptimizer

a little windows diagnostic/optimization console app i built while learning how to hook
a c# app up to an ai api. the idea: scan the pc, send the scan to gemini, let it suggest
fixes, and only run the stuff i actually say yes to. nothing runs automatically, ever
that felt like the important rule to not break while building this.

## what you need

- windows 10/11
- [.net 8 sdk](https://dotnet.microsoft.com/download)
- a free gemini api key from [google ai studio](https://aistudio.google.com/apikey) —
  no credit card needed, which is why i went with this instead of the paid apis

## setup

1. get a free key at https://aistudio.google.com/apikey (sign in with google, click
   "create api key"). if it says "no cloud projects available," you might need to make
   a project first at console.cloud.google.com/projectcreate, then go back and try again.

2. set it as an environment variable (powershell):

before running the terminal codes, make sure to copy the file path to sysoptimizer folder
in the terminal, type in cd file/path/here

```powershell
setx GEMINI_API_KEY "your-key-here"
```

close and reopen your terminal after this, setx only applies to new windows, learned
that one the hard way too.

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
gemini-2.5-flash and it got retired within like a day of me finishing. if you get a 404
saying a model "is no longer available," it usually tells you what to switch to right in
the error message. you can override without touching the code:

```powershell
setx GEMINI_MODEL "whatever-model-they-say-to-use"
```

## how it actually works

i split it into steps so i could reason about each piece separately:

| step | file                                               | what it does                                                                                                                                                                                                       |
| ---- | -------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 1    | `DiagnosticCollector.cs`                           | read-only scan — cpu, ram, disk, top processes, startup apps, running services, recent errors from the system event log. doesn't touch or change anything.                                                         |
| 2    | `GeminiApiClient.cs` (`RequestAnalysisAsync`)      | sends the scan + the "rules" (the system prompt) to gemini, gets back a list of recommendations. still nothing runs at this point.                                                                                 |
| 3    | `Program.cs`                                       | prints each recommendation with its risk level, why it helps, and the exact script, then asks which ones i approve by id.                                                                                          |
| 4    | `GeminiApiClient.cs` (`RequestExecutionPlanAsync`) | sends only the approved ids back, gets an execution plan with just those. i also filter the response myself afterward just in case, so even a mistake on the model's end can't sneak an unapproved action through. |
| 5    | `ExecutionEngine.cs`                               | actually runs the approved scripts through powershell. skips anything needing admin if i'm not running elevated, and double-checks before anything that needs a reboot.                                            |

## file layout

```
SysOptimizer/
├── SysOptimizer.csproj
├── Program.cs                    # ties everything together + the approval prompt
├── SystemPrompt.txt              # the rules i give the model (safe to edit)
├── Models/
│   └── Schemas.cs                # the json shapes the model has to respond in
└── Services/
    ├── DiagnosticCollector.cs    # the read-only scan
    ├── GeminiApiClient.cs        # talks to google's api
    └── ExecutionEngine.cs        # runs only what got approved
```

## things i learned / stuff i'd still wanna add

- **nothing auto-runs.** analysis never executes anything on its own, you have to type
  specific ids for anything to happen.
- **double approval check.** even if gemini's execution-plan response somehow included
  something i didn't approve, the client code strips it out before it reaches the part
  that actually runs commands. didn't want to just trust the model blindly.
- **no sketchy commands.** the system prompt tells the model not to use base64 or
  encoded commands, no downloading and running random stuff, nothing like that.
- **admin stuff is gated.** if a script needs elevation and the terminal isn't running
  as admin, it just skips that one instead of trying and failing weirdly or popping a
  uac prompt mid-run.
- **this is still a rough scaffold, not a finished thing.** stuff i'd want to add before
  trusting it on a machine i actually care about: a log of every script that ran (so i
  could undo stuff later), a dry-run mode, more scan categories (installed apps, temp
  file sizes, power plan, driver/update status — all mentioned in the system prompt but
  not collected yet), and probably a real gui instead of typing ids into a console.

## next steps i'm thinking about

- swap the console approval step for an actual gui once i'm more comfortable with wpf
- add a log file that records what ran and when, so reversible stuff can actually be reverted
- expand the scanner to cover the categories from the system prompt i haven't gotten to yet
