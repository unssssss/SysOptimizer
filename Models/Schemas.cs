using System.Text.Json.Serialization;

namespace SysOptimizer.Models;

// ---------- mode a: analysis response ----------

public class AnalysisResponse
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "analysis";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("system_health")]
    public string SystemHealth { get; set; } = "";

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = new();

    [JsonPropertyName("recommendations")]
    public List<Recommendation> Recommendations { get; set; } = new();
}

public class Recommendation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("why_it_helps")]
    public string WhyItHelps { get; set; } = "";

    [JsonPropertyName("risk_level")]
    public string RiskLevel { get; set; } = "";

    [JsonPropertyName("automation_safe")]
    public bool AutomationSafe { get; set; }

    [JsonPropertyName("reversible")]
    public bool Reversible { get; set; }

    [JsonPropertyName("requires_admin")]
    public bool RequiresAdmin { get; set; }

    [JsonPropertyName("requires_reboot")]
    public bool RequiresReboot { get; set; }

    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = "";

    [JsonPropertyName("execution_script")]
    public string ExecutionScript { get; set; } = "";
}

// ---------- mode b: execution plan response ----------

public class ExecutionPlanResponse
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "execution_plan";

    [JsonPropertyName("approved_actions")]
    public List<ApprovedAction> ApprovedActions { get; set; } = new();
}

public class ApprovedAction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("execution_script")]
    public string ExecutionScript { get; set; } = "";

    [JsonPropertyName("requires_admin")]
    public bool RequiresAdmin { get; set; }

    [JsonPropertyName("requires_reboot")]
    public bool RequiresReboot { get; set; }
}
