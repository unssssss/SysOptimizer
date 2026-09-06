using System.Diagnostics;
using System.Management;
using System.Text.Json;

namespace SysOptimizer.Services;

/// <summary>
/// grabs a snapshot of system diagnostic data. this is the only class that actually
/// touches the real os, and it's all read-only queries, nothing gets changed here
/// </summary>
public class DiagnosticCollector
{
    public object CollectScanData()
    {
        var scan = new Dictionary<string, object?>
        {
            ["windows_version"] = GetWindowsVersion(),
            ["cpu"] = GetCpuInfo(),
            ["ram"] = GetRamInfo(),
            ["disks"] = GetDiskInfo(),
            ["top_processes"] = GetTopProcesses(15),
            ["startup_apps"] = GetStartupApps(),
            ["services"] = GetRunningServices(),
            ["recent_system_errors"] = GetRecentSystemErrors(20),
        };

        return scan;
    }

    public string CollectScanDataAsJson()
    {
        var scan = CollectScanData();
        return JsonSerializer.Serialize(scan, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object GetWindowsVersion()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in searcher.Get())
            {
                return new
                {
                    caption = mo["Caption"]?.ToString(),
                    version = mo["Version"]?.ToString(),
                    build = mo["BuildNumber"]?.ToString()
                };
            }
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
        return new { error = "unavailable" };
    }

    private static object GetCpuInfo()
    {
        try
        {
            string? name = null;
            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    name = mo["Name"]?.ToString();
                    break;
                }
            }

            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();
            Thread.Sleep(500); // perfcounter needs a first throwaway read before the real one is accurate, learned that the hard way
            float usage = cpuCounter.NextValue();

            return new { model = name, current_utilization_percent = Math.Round(usage, 1) };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static object GetRamInfo()
    {
        try
        {
            ulong totalKb = 0, freeKb = 0;
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in searcher.Get())
            {
                totalKb = Convert.ToUInt64(mo["TotalVisibleMemorySize"]);
                freeKb = Convert.ToUInt64(mo["FreePhysicalMemory"]);
            }

            double totalGb = totalKb / 1024.0 / 1024.0;
            double freeGb = freeKb / 1024.0 / 1024.0;
            double usedGb = totalGb - freeGb;

            return new
            {
                total_gb = Math.Round(totalGb, 1),
                used_gb = Math.Round(usedGb, 1),
                free_gb = Math.Round(freeGb, 1),
                used_percent = totalGb > 0 ? Math.Round(usedGb / totalGb * 100, 1) : 0
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static object GetDiskInfo()
    {
        var disks = new List<object>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                double totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;

                disks.Add(new
                {
                    drive = drive.Name,
                    format = drive.DriveFormat,
                    total_gb = Math.Round(totalGb, 1),
                    free_gb = Math.Round(freeGb, 1),
                    free_percent = totalGb > 0 ? Math.Round(freeGb / totalGb * 100, 1) : 0
                });
            }
        }
        catch (Exception ex)
        {
            disks.Add(new { error = ex.Message });
        }
        return disks;
    }

    private static object GetTopProcesses(int count)
    {
        var list = new List<object>();
        try
        {
            var processes = Process.GetProcesses()
                .Where(p =>
                {
                    try { return p.WorkingSet64 > 0; }
                    catch { return false; }
                })
                .OrderByDescending(p =>
                {
                    try { return p.WorkingSet64; }
                    catch { return 0L; }
                })
                .Take(count);

            foreach (var p in processes)
            {
                try
                {
                    list.Add(new
                    {
                        name = p.ProcessName,
                        pid = p.Id,
                        memory_mb = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1)
                    });
                }
                catch
                {
                    // process probably closed between when we listed it and when we tried to read it, just skip it
                }
            }
        }
        catch (Exception ex)
        {
            list.Add(new { error = ex.Message });
        }
        return list;
    }

    private static object GetStartupApps()
    {
        var list = new List<object>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Command, Location, User FROM Win32_StartupCommand");
            foreach (ManagementObject mo in searcher.Get())
            {
                list.Add(new
                {
                    name = mo["Name"]?.ToString(),
                    command = mo["Command"]?.ToString(),
                    location = mo["Location"]?.ToString(),
                    user = mo["User"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            list.Add(new { error = ex.Message });
        }
        return list;
    }

    private static object GetRunningServices()
    {
        var list = new List<object>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DisplayName, State, StartMode FROM Win32_Service WHERE State='Running'");
            foreach (ManagementObject mo in searcher.Get())
            {
                list.Add(new
                {
                    name = mo["Name"]?.ToString(),
                    display_name = mo["DisplayName"]?.ToString(),
                    state = mo["State"]?.ToString(),
                    start_mode = mo["StartMode"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            list.Add(new { error = ex.Message });
        }
        return list;
    }

    private static object GetRecentSystemErrors(int count)
    {
        var list = new List<object>();
        try
        {
            using var log = new EventLog("System");
            int total = log.Entries.Count;
            int start = Math.Max(0, total - count);

            for (int i = total - 1; i >= start; i--)
            {
                var entry = log.Entries[i];
                if (entry.EntryType == EventLogEntryType.Error || entry.EntryType == EventLogEntryType.Warning)
                {
                    list.Add(new
                    {
                        time = entry.TimeGenerated.ToString("u"),
                        source = entry.Source,
                        type = entry.EntryType.ToString(),
                        event_id = entry.InstanceId,
                        message = Truncate(entry.Message, 300)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            list.Add(new { error = ex.Message });
        }
        return list;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "...");
}
