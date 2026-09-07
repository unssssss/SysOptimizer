using System.Diagnostics;
using System.Management;
using System.Text.Json;
using System.Threading;

namespace SysOptimizer.Services;

/// <summary>
/// for issues that only happen when you DO something (launch a game, plug in a device,
/// whatever) a one-time system snapshot won't catch it. this watches network adapter
/// status + the system/application event logs in real time, starting right before you
/// trigger the problem and stopping right after, so we hand the model a small focused
/// window instead of a random slice of "recent" logs that might not even cover the moment
/// </summary>
public class NetworkCaptureService
{
    private readonly List<object> _statusChanges = new();
    private readonly List<object> _eventLogEntries = new();
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private EventLog? _systemLog;
    private EventLog? _applicationLog;
    private DateTime _startTime;

    public void Start()
    {
        _startTime = DateTime.Now;
        _cts = new CancellationTokenSource();

        // fire-and-forget polling loop, checks adapter status once a second while active
        _ = Task.Run(() => PollNetworkStatusAsync(_cts.Token));

        // these fire in real time whenever windows writes a new entry, way better than
        // polling for this part since we don't want to miss anything between checks
        _systemLog = new EventLog("System") { EnableRaisingEvents = true };
        _systemLog.EntryWritten += OnEntryWritten;

        _applicationLog = new EventLog("Application") { EnableRaisingEvents = true };
        _applicationLog.EntryWritten += OnEntryWritten;
    }

    private void OnEntryWritten(object sender, EntryWrittenEventArgs e)
    {
        var entry = e.Entry;
        if (entry.TimeGenerated < _startTime) return; // ignore backlog, only care about stuff from now on

        lock (_lock)
        {
            _eventLogEntries.Add(new
            {
                time = entry.TimeGenerated.ToString("u"),
                log = (sender as EventLog)?.Log,
                source = entry.Source,
                type = entry.EntryType.ToString(),
                event_id = entry.InstanceId,
                message = Truncate(entry.Message, 400)
            });
        }
    }

    private async Task PollNetworkStatusAsync(CancellationToken token)
    {
        var lastKnownStatus = new Dictionary<string, object?>();

        while (!token.IsCancellationRequested)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, NetConnectionID, NetConnectionStatus FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE");

                foreach (ManagementObject mo in searcher.Get())
                {
                    string adapterKey = mo["NetConnectionID"]?.ToString() ?? mo["Name"]?.ToString() ?? "unknown adapter";
                    object? status = mo["NetConnectionStatus"];

                    if (!lastKnownStatus.TryGetValue(adapterKey, out var previous) || !Equals(previous, status))
                    {
                        lock (_lock)
                        {
                            _statusChanges.Add(new
                            {
                                time = DateTime.Now.ToString("u"),
                                adapter = adapterKey,
                                // 2 = connected, 7 = media disconnected — these two flipping
                                // back and forth is exactly the "wifi disappears" pattern
                                status_code = status
                            });
                        }
                        lastKnownStatus[adapterKey] = status;
                    }
                }
            }
            catch
            {
                // wmi hiccups happen, just skip this tick rather than crashing the capture
            }

            try { await Task.Delay(1000, token); }
            catch (TaskCanceledException) { break; }
        }
    }

    public object StopAndGetReport()
    {
        _cts?.Cancel();

        if (_systemLog != null)
        {
            _systemLog.EnableRaisingEvents = false;
            _systemLog.EntryWritten -= OnEntryWritten;
            _systemLog.Dispose();
        }
        if (_applicationLog != null)
        {
            _applicationLog.EnableRaisingEvents = false;
            _applicationLog.EntryWritten -= OnEntryWritten;
            _applicationLog.Dispose();
        }

        lock (_lock)
        {
            return new
            {
                capture_note = "this is a focused capture window, not a full system scan. " +
                                "it only contains network adapter status changes and new event log " +
                                "entries that happened between capture_started and capture_ended.",
                capture_started = _startTime.ToString("u"),
                capture_ended = DateTime.Now.ToString("u"),
                network_adapter_status_changes = _statusChanges,
                new_event_log_entries = _eventLogEntries
            };
        }
    }

    public string StopAndGetReportAsJson() =>
        JsonSerializer.Serialize(StopAndGetReport(), new JsonSerializerOptions { WriteIndented = true });

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "...");
}
