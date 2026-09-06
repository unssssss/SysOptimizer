using System.Diagnostics;
using System.Security.Principal;
using SysOptimizer.Models;

namespace SysOptimizer.Services;

/// <summary>
/// runs approved actions and only approved actions. this class doesn't decide
/// anything on its own, it just runs whatever scripts are inside an
/// executionplanresponse that was already built from the ids the user picked
/// </summary>
public class ExecutionEngine
{
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public async Task<List<ExecutionResult>> RunAsync(ExecutionPlanResponse plan)
    {
        var results = new List<ExecutionResult>();

        foreach (var action in plan.ApprovedActions)
        {
            if (action.RequiresAdmin && !IsRunningAsAdministrator())
            {
                results.Add(new ExecutionResult
                {
                    Id = action.Id,
                    Success = false,
                    Output = "",
                    Error = "Skipped: this action requires administrator privileges, " +
                            "but the application is not running elevated. Restart as admin to run it."
                });
                continue;
            }

            if (action.RequiresReboot)
            {
                Console.WriteLine($"[{action.Id}] This action requires a reboot afterward. Continue? (y/n)");
                var confirm = Console.ReadLine();
                if (!string.Equals(confirm?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new ExecutionResult
                    {
                        Id = action.Id,
                        Success = false,
                        Output = "",
                        Error = "Skipped by user at reboot confirmation step."
                    });
                    continue;
                }
            }

            var result = await RunSingleScriptAsync(action);
            results.Add(result);
        }

        return results;
    }

    private static async Task<ExecutionResult> RunSingleScriptAsync(ApprovedAction action)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                // -NoProfile / -NonInteractive so it runs the same way every time, no weird
                // profile scripts messing with it. also thought about using -EncodedCommand
                // (base64) here but the whole point of this project is avoiding obfuscated
                // commands, so just passing the script as plain text instead
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{EscapeForCommandArg(action.ExecutionScript)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new ExecutionResult
            {
                Id = action.Id,
                Success = process.ExitCode == 0,
                Output = stdout,
                Error = stderr
            };
        }
        catch (Exception ex)
        {
            return new ExecutionResult
            {
                Id = action.Id,
                Success = false,
                Output = "",
                Error = ex.Message
            };
        }
    }

    private static string EscapeForCommandArg(string script) =>
        script.Replace("\"", "\\\"");
}

public class ExecutionResult
{
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
}
