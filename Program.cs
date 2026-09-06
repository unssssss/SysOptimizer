using SysOptimizer.Models;
using SysOptimizer.Services;

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

        // step 2: run the actual scan (this is read-only, doesn't change anything on the machine)
        Console.WriteLine("Collecting system diagnostics...");
        string scanJson = collector.CollectScanDataAsJson();
        Console.WriteLine("Done.\n");

        // step 3: mode a, just ask the model to look at the scan and suggest stuff.
        // nothing gets executed here, it's purely "here's what i found, what do you think"
        Console.WriteLine("Sending scan to Gemini for analysis...");
        AnalysisResponse analysis;
        try
        {
            analysis = await client.RequestAnalysisAsync(scanJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Analysis failed: {ex.Message}");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"System health: {analysis.SystemHealth}");
        Console.WriteLine($"Summary: {analysis.Summary}");
        if (analysis.Limitations.Count > 0)
        {
            Console.WriteLine("Limitations:");
            foreach (var l in analysis.Limitations) Console.WriteLine($"  - {l}");
        }

        if (analysis.Recommendations.Count == 0)
        {
            Console.WriteLine("\nNo recommendations. Nothing to do.");
            return;
        }

        // step 4: show the recommendations and let the person pick which ones they trust.
        // this is the whole point of the app honestly, never auto-run anything
        Console.WriteLine($"\n{analysis.Recommendations.Count} recommendation(s):\n");
        foreach (var r in analysis.Recommendations)
        {
            Console.WriteLine($"[{r.Id}] ({r.Category}, risk: {r.RiskLevel}) {r.Title}");
            Console.WriteLine($"    {r.Description}");
            Console.WriteLine($"    Why: {r.WhyItHelps}");
            Console.WriteLine($"    Evidence: {r.Evidence}");
            Console.WriteLine($"    Admin required: {r.RequiresAdmin} | Reboot required: {r.RequiresReboot} | Reversible: {r.Reversible}");
            Console.WriteLine($"    Script: {r.ExecutionScript}");
            Console.WriteLine();
        }

        Console.WriteLine("Enter the IDs you approve (comma-separated), or press Enter to approve none:");
        string? input = Console.ReadLine();
        var approvedIds = (input ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => analysis.Recommendations.Any(r => r.Id == id))
            .ToList();

        if (approvedIds.Count == 0)
        {
            Console.WriteLine("No actions approved. Exiting.");
            return;
        }

        // step 5: mode b, ask again but this time only for the ids that got approved.
        // the model builds the "official" execution plan from just those
        Console.WriteLine("\nRequesting execution plan for approved items...");
        ExecutionPlanResponse plan;
        try
        {
            plan = await client.RequestExecutionPlanAsync(analysis, approvedIds);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to build execution plan: {ex.Message}");
            return;
        }

        if (plan.ApprovedActions.Count == 0)
        {
            Console.WriteLine("Execution plan came back empty. Nothing to run.");
            return;
        }

        // step 6: finally actually run the approved scripts
        Console.WriteLine($"\nRunning {plan.ApprovedActions.Count} approved action(s)...\n");
        var results = await engine.RunAsync(plan);

        foreach (var result in results)
        {
            Console.WriteLine($"[{result.Id}] {(result.Success ? "SUCCESS" : "FAILED/SKIPPED")}");
            if (!string.IsNullOrWhiteSpace(result.Output)) Console.WriteLine($"    Output: {result.Output.Trim()}");
            if (!string.IsNullOrWhiteSpace(result.Error)) Console.WriteLine($"    Error: {result.Error.Trim()}");
        }

        Console.WriteLine("\nDone.");
    }
}
