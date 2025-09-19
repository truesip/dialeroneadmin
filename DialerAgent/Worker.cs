using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DialerAgent;

public class AgentConfig
{
    public string HubUrl { get; set; } = "https://localhost:5001";
    public string? AgentId { get; set; }
    public string? Token { get; set; }
    public DialerCfg Dialer { get; set; } = new();
}
public class DialerCfg { public string ExePath { get; set; } = @"C:\\Users\\Public\\DialerOne\\DialerOne.exe"; }

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _cfgRoot;
    private HubConnection? _conn;
    private AgentConfig _cfg = new();

    public Worker(IConfiguration cfg, ILogger<Worker> logger)
    { _cfgRoot = cfg; _logger = logger; _cfg = cfg.GetSection("Agent").Get<AgentConfig>() ?? new AgentConfig(); }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _conn = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(_cfg.HubUrl.TrimEnd('/')), "/hubs/agent"))
            .WithAutomaticReconnect()
            .Build();

        _conn.On<object>("Command", async cmdObj =>
        {
            var type = cmdObj?.GetType().GetProperty("Type")?.GetValue(cmdObj)?.ToString()?.ToLowerInvariant();
            var payload = cmdObj?.GetType().GetProperty("Payload")?.GetValue(cmdObj) as System.Collections.IDictionary;
            var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (payload != null)
                foreach (var k in payload.Keys) if (k != null) p[k.ToString()!] = payload[k]?.ToString() ?? string.Empty;
            _logger.LogInformation("Command: {type}", type);
            switch (type)
            {
                case "enable": EnableDialer(); break;
                case "disable": DisableDialer(); break;
                case "updatesip": UpdateSip(p); RestartDialer(); break;
                case "restart": RestartDialer(); break;
            }
            await Task.CompletedTask;
        });

        await _conn.StartAsync(stoppingToken);
        await _conn.SendAsync("Register", BuildHello(), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_conn.State == HubConnectionState.Connected)
                    await _conn.SendAsync("Heartbeat", BuildHeartbeat(), stoppingToken);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Heartbeat error"); }
            await Task.Delay(5000, stoppingToken);
        }
    }

    private object BuildHello() => new
    {
        AgentId = _cfg.AgentId ?? Environment.MachineName,
        Token = _cfg.Token,
        MachineName = Environment.MachineName,
        IPs = GetIPs(),
        MACs = GetMACs(),
        Version = "1.0.0"
    };

    private object BuildHeartbeat() => new
    {
        AgentId = _cfg.AgentId ?? Environment.MachineName,
        Status = IsDialerRunning() ? "running" : (IsDisabled() ? "disabled" : "stopped"),
        UserName = Environment.UserName
    };

    private static string[] GetIPs() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(a => a.Address.ToString()).Distinct().ToArray();

    private static string[] GetMACs() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .Select(n => string.Join("-", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")))).Distinct().ToArray();

    private static bool IsDialerRunning() => Process.GetProcessesByName("DialerOne").Any();
    private void RestartDialer() { StopDialer(); StartDialer(_cfg.Dialer.ExePath); }
    private static void StartDialer(string exe) { try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false }); } catch { } }
    private static void StopDialer() { foreach (var p in Process.GetProcessesByName("DialerOne")) { try { p.Kill(); } catch { } } }

    private static string DisabledFlag => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DialerOne", "disabled.flag");
    private static bool IsDisabled() => File.Exists(DisabledFlag);
    private static void DisableDialer() { try { Directory.CreateDirectory(Path.GetDirectoryName(DisabledFlag)!); File.WriteAllText(DisabledFlag, DateTime.UtcNow.ToString("o")); } catch { } StopDialer(); }
    private void EnableDialer() { try { if (File.Exists(DisabledFlag)) File.Delete(DisabledFlag); } catch { } if (!IsDialerRunning()) StartDialer(_cfg.Dialer.ExePath); }

    private static string ManagedSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DialerOne", "managed_settings.txt");
    private static void UpdateSip(Dictionary<string,string> p)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ManagedSettings)!);
            var lines = File.Exists(ManagedSettings) ? File.ReadAllLines(ManagedSettings).ToList() : new List<string>();
            void Upsert(string key)
            {
                if (!p.TryGetValue(key, out var v)) return; var idx = lines.FindIndex(l => l.StartsWith(key+"=", StringComparison.OrdinalIgnoreCase)); var line = key+"="+(v??""); if (idx>=0) lines[idx]=line; else lines.Add(line);
            }
            foreach (var k in new[]{"domain","username","password","port","callerid","fromname"}) Upsert(k);
            File.WriteAllLines(ManagedSettings, lines);
        }
        catch { }
    }
}
