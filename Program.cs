using System.IO;
using SysOptimizer.Models;
using SysOptimizer.Services;
using SysOptimizer.UI;

namespace SysOptimizer;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("=== SysOptimizer ===");
        Console.WriteLine("windows diagnostic & optimization assistant (powered by Gemini)\n");

        // step 1: grab the api key from env vars, this is how we avoid hardcoding secrets
        string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("ERROR: Set the GEMINI_API_KEY environment variable before running.");
            Console.WriteLine("  Get a free key at https://aistudio.google.com/apikey");
            Console.WriteLine("  setx GEMINI_API_KEY \"your-key-here\"   (then restart the terminal)");
            return;
        }
        // letting people override the model name in case google renames/retires the default one
        // (they do this more often than i expected while building this lol)
        string? model = Environment.GetEnvironmentVariable("GEMINI_MODEL");

        string promptPath = Path.Combine(AppContext.BaseDirectory, "SystemPrompt.txt");
        if (!File.Exists(promptPath))
        {
            Console.WriteLine($"ERROR: SystemPrompt.txt not found at {promptPath}");
            return;
        }
        string systemPrompt = await File.ReadAllTextAsync(promptPath);

        var client = new GeminiApiClient(apiKey, systemPrompt, model);
        var collector = new DiagnosticCollector();
        var engine = new ExecutionEngine();
        var actionLogger = new ActionLogger();

        // step 2: pick a mode. full scan covers general health, capture mode is for
        // "this only happens when i do X" type problems that a snapshot can't catch
        Console.WriteLine("What do you want to do?");
        Console.WriteLine("  1) full system scan (general health check)");
        Console.WriteLine("  2) reproduce-and-capture (for intermittent stuff, like wifi dropping when a game launches)");
        Console.Write("Pick 1 or 2: ");
        string? modeChoice = Console.ReadLine();

        AnalysisResponse analysis;

        if (modeChoice?.Trim() == "2")
        {
            analysis = await RunReproduceAndCaptureAsync(client);
        }
        else
        {
            analysis = await RunFullScanAsync(client, collector);
        }

        if (analysis.Recommendations.Count == 0)
        {
            Console.WriteLine("\nno recommendations. nothing to do.");
            return;
        }

        // step 3: show the recommendations in a real window and let the person check off
        // whichever ones they actually trust. nothing runs until they hit approve here —
        // this is still the core safety rule of the whole app, gui or not
        Console.WriteLine($"\n{analysis.Recommendations.Count} recommendation(s) — opening the review window...");
        var approvedIds = GuiApproval.ShowApprovalDialog(analysis.Recommendations);

        if (approvedIds.Count == 0)
        {
            Console.WriteLine("no actions approved. exiting.");
            return;
        }

        Console.WriteLine($"approved: {string.Join(", ", approvedIds)}");

        // step 4: mode b, ask again but this time only for the ids that got approved.
        // the model builds the "official" execution plan from just those
        Console.WriteLine("\nrequesting execution plan for approved items...");
        ExecutionPlanResponse plan;
        try
        {
            plan = await client.RequestExecutionPlanAsync(analysis, approvedIds);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"failed to build execution plan: {ex.Message}");
            return;
        }

        if (plan.ApprovedActions.Count == 0)
        {
            Console.WriteLine("execution plan came back empty. nothing to run.");
            return;
        }

        // step 5: finally actually run the approved scripts
        Console.WriteLine($"\nrunning {plan.ApprovedActions.Count} approved action(s)...\n");
        var results = await engine.RunAsync(plan);

        foreach (var result in results)
        {
            Console.WriteLine($"[{result.Id}] {(result.Success ? "SUCCESS" : "FAILED/SKIPPED")}");
            if (!string.IsNullOrWhiteSpace(result.Output)) Console.WriteLine($"    output: {result.Output.Trim()}");
            if (!string.IsNullOrWhiteSpace(result.Error)) Console.WriteLine($"    error: {result.Error.Trim()}");
        }

        // step 6: write everything that just ran to a log file, so there's an actual
        // record later of what happened and when (useful for reverting reversible stuff)
        await actionLogger.LogBatchAsync(plan.ApprovedActions, results);
        Console.WriteLine($"\nlogged this run to {actionLogger.LogPath}");

        Console.WriteLine("\ndone.");
    }

    private static async Task<AnalysisResponse> RunFullScanAsync(GeminiApiClient client, DiagnosticCollector collector)
    {
        Console.WriteLine("\ncollecting system diagnostics...");
        string scanJson = collector.CollectScanDataAsJson();
        Console.WriteLine("done.\n");

        Console.WriteLine("sending scan to gemini for analysis...");
        AnalysisResponse analysis;
        try
        {
            analysis = await client.RequestAnalysisAsync(scanJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"analysis failed: {ex.Message}");
            Environment.Exit(1);
            return null!; // unreachable, just keeps the compiler happy about the return path
        }

        PrintAnalysisSummary(analysis);
        return analysis;
    }

    private static async Task<AnalysisResponse> RunReproduceAndCaptureAsync(GeminiApiClient client)
    {
        Console.WriteLine();
        Console.WriteLine("reproduce-and-capture mode.");
        Console.WriteLine("this watches your network adapters + event logs in real time instead of");
        Console.WriteLine("taking a one-time snapshot, since intermittent stuff like this usually");
        Console.WriteLine("doesn't show up in a regular scan.");
        Console.WriteLine();
        Console.WriteLine("when you're ready: press ENTER to start capturing, then go trigger the");
        Console.WriteLine("problem (e.g. launch valorant and wait for the wifi to drop). once it");
        Console.WriteLine("happens, come back here and press ENTER again to stop.");
        Console.ReadLine();

        var capture = new NetworkCaptureService();
        capture.Start();
        Console.WriteLine("\ncapturing... go do the thing that breaks it. press ENTER when it happens.");
        Console.ReadLine();

        string captureJson = capture.StopAndGetReportAsJson();
        Console.WriteLine("capture stopped. sending to gemini for analysis...");

        string contextNote =
            "This is a focused capture from a reproduce-and-capture session, NOT a full system " +
            "scan. The user deliberately triggered a problem (their network connection dropping " +
            "when launching a specific application) and this JSON contains only network adapter " +
            "status changes and new event log entries recorded during that window. Analyze this " +
            "for the root cause of the network drop.";

        AnalysisResponse analysis;
        try
        {
            analysis = await client.RequestAnalysisAsync(captureJson, contextNote);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"analysis failed: {ex.Message}");
            Environment.Exit(1);
            return null!;
        }

        PrintAnalysisSummary(analysis);
        return analysis;
    }

    private static void PrintAnalysisSummary(AnalysisResponse analysis)
    {
        Console.WriteLine();
        Console.WriteLine($"system health: {analysis.SystemHealth}");
        Console.WriteLine($"summary: {analysis.Summary}");
        if (analysis.Limitations.Count > 0)
        {
            Console.WriteLine("limitations:");
            foreach (var l in analysis.Limitations) Console.WriteLine($"  - {l}");
        }
    }
}
