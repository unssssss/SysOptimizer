using System.Text;
using System.Text.Json;
using SysOptimizer.Models;

namespace SysOptimizer.Services;

public class GeminiApiClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly HttpClient _http;
    private readonly string _systemPrompt;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiApiClient(string apiKey, string systemPrompt, string? model = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("GEMINI_API_KEY is not set. Set it as an environment variable.");

        _apiKey = apiKey;
        _systemPrompt = systemPrompt;
        // this model works on the free tier as of when i wrote this. if it 404s later,
        // check https://ai.google.dev/gemini-api/docs/models for whatever's current
        // and set GEMINI_MODEL to override without touching the code
        _model = model ?? "gemini-3.6-flash";

        _http = new HttpClient();
    }

    /// <summary>
    /// mode a: send the scan data over and get back a list of recommendations.
    /// nothing runs yet at this point, it's just the model's opinion on the scan
    /// </summary>
    public async Task<AnalysisResponse> RequestAnalysisAsync(string scanDataJson)
    {
        string userMessage =
            "Here is the current system scan data (JSON). Analyze it per your instructions and " +
            "return ONLY the analysis JSON object.\n\n" + scanDataJson;

        string rawJson = await SendMessageAsync(userMessage);
        return DeserializeStrict<AnalysisResponse>(rawJson);
    }

    /// <summary>
    /// mode b: send back just the ids the user actually approved (plus the matching
    /// recommendation objects so the model still has the exact scripts to work with),
    /// and get an execution plan containing only those actions
    /// </summary>
    public async Task<ExecutionPlanResponse> RequestExecutionPlanAsync(
        AnalysisResponse originalAnalysis, IEnumerable<string> approvedIds)
    {
        var approvedIdList = approvedIds.ToList();

        var payload = new
        {
            approved_recommendation_ids = approvedIdList,
            original_recommendations = originalAnalysis.Recommendations
                .Where(r => approvedIdList.Contains(r.Id))
                .ToList()
        };

        string userMessage =
            "The host application confirms the user has explicitly approved the following " +
            "recommendation IDs, and supplies the matching original recommendation objects below. " +
            "Return ONLY the execution_plan JSON object containing exactly these approved actions " +
            "and no others.\n\n" + JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        string rawJson = await SendMessageAsync(userMessage);
        var plan = DeserializeStrict<ExecutionPlanResponse>(rawJson);

        // just being extra careful here: even if the model somehow includes something
        // that wasn't approved, we filter it out ourselves before it ever leaves this method.
        // trust but verify i guess
        plan.ApprovedActions = plan.ApprovedActions
            .Where(a => approvedIdList.Contains(a.Id))
            .ToList();

        return plan;
    }

    private async Task<string> SendMessageAsync(string userMessage)
    {
        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = _systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userMessage } }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        string bodyJson = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        string url = $"{BaseUrl}/{_model}:generateContent?key={_apiKey}";
        using var response = await _http.PostAsync(url, content);
        string responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini API error {(int)response.StatusCode}: {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);

        // sometimes gemini blocks a response (safety filters, hit max tokens with nothing
        // generated yet, etc) and just gives back an empty candidates list. catching that
        // here so we get a real error message instead of a confusing null ref later
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            string? blockReason = doc.RootElement.TryGetProperty("promptFeedback", out var feedback)
                && feedback.TryGetProperty("blockReason", out var reason)
                ? reason.GetString()
                : null;
            throw new InvalidOperationException(
                $"Gemini returned no candidates. Block reason: {blockReason ?? "unknown"}. Raw: {responseBody}");
        }

        var firstCandidate = candidates[0];
        string text = firstCandidate
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return StripCodeFences(text.Trim());
    }

    private static string StripCodeFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
        {
            int firstNewline = s.IndexOf('\n');
            if (firstNewline >= 0) s = s[(firstNewline + 1)..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        return s.Trim();
    }

    private static T DeserializeStrict<T>(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (result == null)
                throw new InvalidOperationException("Model returned null after deserialization.");
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse model response as {typeof(T).Name}. Raw response:\n{json}", ex);
        }
    }
}
