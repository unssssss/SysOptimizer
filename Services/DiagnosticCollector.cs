using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

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
            ["gpu"] = GetGpuInfo(),
            ["disks"] = GetDiskInfo(),
            ["top_processes"] = GetTopProcesses(15),
            ["startup_apps"] = GetStartupApps(),
            ["services"] = GetRunningServices(),
            ["network_adapters"] = GetNetworkAdapters(),
            ["installed_applications"] = GetInstalledApplications(60),
            ["temp_and_cache"] = GetTempAndCacheSizes(),
            ["power_plan"] = GetPowerPlan(),
            ["windows_update"] = GetWindowsUpdateStatus(),
            ["driver_issues"] = GetDriverIssues(),
            ["recent_system_errors"] = GetRecentEventLogEntries("System", 20),
            ["recent_application_errors"] = GetRecentEventLogEntries("Application", 20),
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

    private static object GetRecentEventLogEntries(string logName, int count)
    {
        var list = new List<object>();
        try
        {
            using var log = new EventLog(logName);
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

    private static object GetGpuInfo()
    {
        var gpus = new List<object>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, DriverDate FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
            {
                long? ramBytes = mo["AdapterRAM"] != null ? Convert.ToInt64(mo["AdapterRAM"]) : null;
                gpus.Add(new
                {
                    name = mo["Name"]?.ToString(),
                    // note: AdapterRAM is a 32-bit field in wmi and reports wrong/negative values
                    // on a lot of modern gpus with 4gb+ vram, so treat this as "best effort"
                    vram_gb_best_effort = ramBytes is > 0 ? Math.Round(ramBytes.Value / 1024.0 / 1024.0 / 1024.0, 1) : (double?)null,
                    driver_version = mo["DriverVersion"]?.ToString(),
                    driver_date = mo["DriverDate"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            gpus.Add(new { error = ex.Message });
        }
        return gpus;
    }

    /// <summary>
    /// this is the piece that matters for stuff like "wifi drops when i open a game" —
    /// grabs every network adapter, whether it's actually connected, and the power
    /// management setting that's the #1 cause of that exact symptom (windows turning off
    /// the adapter to save power mid-game). also flags drivers with known conflict history.
    /// </summary>
    private static object GetNetworkAdapters()
    {
        var adapters = new List<object>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NetConnectionID, NetConnectionStatus, MACAddress, Manufacturer, PNPDeviceID " +
                "FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE");

            foreach (ManagementObject mo in searcher.Get())
            {
                string? pnpId = mo["PNPDeviceID"]?.ToString();
                string? name = mo["Name"]?.ToString();

                adapters.Add(new
                {
                    name,
                    connection_name = mo["NetConnectionID"]?.ToString(),
                    manufacturer = mo["Manufacturer"]?.ToString(),
                    // 2 = connected, 7 = media disconnected, others = various transitional states
                    connection_status_code = mo["NetConnectionStatus"],
                    power_management_allows_sleep = GetAdapterPowerSavingEnabled(pnpId),
                    known_conflict_prone = IsKnownConflictProneAdapter(name)
                });
            }
        }
        catch (Exception ex)
        {
            adapters.Add(new { error = ex.Message });
        }
        return adapters;
    }

    /// <summary>
    /// checks the "allow the computer to turn off this device to save power" checkbox
    /// via the registry, since wmi doesn't expose this directly. this is the setting
    /// that's usually behind "wifi disappears when a game/gpu load starts."
    /// </summary>
    private static bool? GetAdapterPowerSavingEnabled(string? pnpDeviceId)
    {
        if (string.IsNullOrEmpty(pnpDeviceId)) return null;
        try
        {
            // device power settings live under this enum key, keyed by instance id.
            // this is a best-effort lookup — not every driver stores it in the same spot,
            // so a null here just means "couldn't tell," not "setting is off."
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\{pnpDeviceId}\Device Parameters");
            var value = key?.GetValue("EnhancedPowerManagementEnabled") ?? key?.GetValue("SelectiveSuspendEnabled");
            if (value == null) return null;
            return Convert.ToInt32(value) != 0;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsKnownConflictProneAdapter(string? adapterName)
    {
        if (string.IsNullOrEmpty(adapterName)) return false;
        // not an exhaustive list, just the ones that come up a lot in "game launches,
        // wifi drops" reports — mostly realtek wifi chips and killer networking cards
        // interacting badly with anti-cheat drivers or aggressive power saving.
        string[] flaggedKeywords = { "realtek", "killer", "rtl8" };
        return flaggedKeywords.Any(k => adapterName.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static object GetInstalledApplications(int maxCount)
    {
        var apps = new List<object>();
        try
        {
            string[] uninstallKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var keyPath in uninstallKeys)
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(keyPath);
                if (baseKey == null) continue;

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    using var subKey = baseKey.OpenSubKey(subKeyName);
                    string? displayName = subKey?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue; // skip entries with no real name, usually system components

                    apps.Add(new
                    {
                        name = displayName,
                        version = subKey?.GetValue("DisplayVersion") as string,
                        publisher = subKey?.GetValue("Publisher") as string,
                        install_date = subKey?.GetValue("InstallDate") as string
                    });

                    if (apps.Count >= maxCount) break;
                }
                if (apps.Count >= maxCount) break;
            }
        }
        catch (Exception ex)
        {
            apps.Add(new { error = ex.Message });
        }
        return apps;
    }

    private static object GetTempAndCacheSizes()
    {
        try
        {
            long userTempBytes = GetDirectorySizeSafe(Path.GetTempPath());
            long windowsTempBytes = GetDirectorySizeSafe(@"C:\Windows\Temp");

            long chromeCacheBytes = GetDirectorySizeSafe(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Google\Chrome\User Data\Default\Cache"));
            long edgeCacheBytes = GetDirectorySizeSafe(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Edge\User Data\Default\Cache"));

            return new
            {
                user_temp_mb = Math.Round(userTempBytes / 1024.0 / 1024.0, 1),
                windows_temp_mb = Math.Round(windowsTempBytes / 1024.0 / 1024.0, 1),
                chrome_cache_mb = Math.Round(chromeCacheBytes / 1024.0 / 1024.0, 1),
                edge_cache_mb = Math.Round(edgeCacheBytes / 1024.0 / 1024.0, 1),
                note = "browser cache sizes are only included for browsers that are actually installed; 0 usually just means not found"
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static long GetDirectorySizeSafe(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            // EnumerateFiles with a try/catch per-file would be more thorough but way slower;
            // this is a "good enough for a scan" estimate, not a byte-perfect count
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* file in use / permission denied, just skip it */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static object GetPowerPlan()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/getactivescheme",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            string output = process!.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            return new { active_scheme_raw = output.Trim() };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static object GetWindowsUpdateStatus()
    {
        try
        {
            DateTime? lastInstalled = null;
            using var searcher = new ManagementObjectSearcher("SELECT InstalledOn FROM Win32_QuickFixEngineering");
            foreach (ManagementObject mo in searcher.Get())
            {
                if (DateTime.TryParse(mo["InstalledOn"]?.ToString(), out var installedOn))
                {
                    if (lastInstalled == null || installedOn > lastInstalled) lastInstalled = installedOn;
                }
            }

            // this key existing usually means windows is waiting on a reboot to finish applying an update
            bool pendingReboot = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") != null;

            return new
            {
                most_recent_update_installed = lastInstalled?.ToString("yyyy-MM-dd"),
                pending_reboot_for_update = pendingReboot
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static object GetDriverIssues()
    {
        var issues = new List<object>();
        try
        {
            // ConfigManagerErrorCode != 0 means device manager is showing some kind of
            // warning/error icon on that device — 0 means "working properly"
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode != 0");
            foreach (ManagementObject mo in searcher.Get())
            {
                issues.Add(new
                {
                    device_name = mo["Name"]?.ToString(),
                    error_code = mo["ConfigManagerErrorCode"]
                });
            }
        }
        catch (Exception ex)
        {
            issues.Add(new { error = ex.Message });
        }
        return issues;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "...");
}
