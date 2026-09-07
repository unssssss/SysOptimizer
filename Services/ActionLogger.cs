using System.IO;
using System.Text.Json;
using SysOptimizer.Models;

namespace SysOptimizer.Services;

/// <summary>
/// keeps a running record of every approved action that got executed, one json line per
/// run, appended so old entries never get overwritten. the point is so i can actually look
/// back later and answer "wait, what did i run last tuesday and did it work"
/// </summary>
public class ActionLogger
{
    public string LogPath { get; }

    public ActionLogger(string? logPath = null)
    {
        LogPath = logPath ?? Path.Combine(AppContext.BaseDirectory, "logs", "action-log.jsonl");
        var dir = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public async Task LogAsync(ApprovedAction action, ExecutionResult result)
    {
        var entry = new
        {
            timestamp = DateTime.Now.ToString("u"),
            id = action.Id,
            script = action.ExecutionScript,
            requires_admin = action.RequiresAdmin,
            requires_reboot = action.RequiresReboot,
            success = result.Success,
            output = result.Output,
            error = result.Error
        };

        string line = JsonSerializer.Serialize(entry);
        // AppendAllTextAsync opens/closes the file each call, which is a bit wasteful for a
        // batch of actions, but it means nothing gets lost if the app crashes mid-run
        await File.AppendAllTextAsync(LogPath, line + Environment.NewLine);
    }

    public async Task LogBatchAsync(IEnumerable<ApprovedAction> actions, IEnumerable<ExecutionResult> results)
    {
        var resultsById = results.ToDictionary(r => r.Id);
        foreach (var action in actions)
        {
            if (resultsById.TryGetValue(action.Id, out var result))
                await LogAsync(action, result);
        }
    }
}
