using System.Text.Json;

internal static class ProxyConfig
{
    // Escape characters that are special in the Go regular-expression engine.
    private static string ExactWindowsPath(string path) =>
        "(?i)^" + string.Concat(path.Select(c =>
            @"\.+*?()|[]{}^$".Contains(c) ? "\\" + c : c.ToString())) + "$";

    public static string Create(string app, string host, int port, bool https,
        string username, string password)
    {
        var upstream = new Dictionary<string, object>
        {
            ["type"] = https ? "http" : "socks",
            ["tag"] = "proxy",
            ["server"] = host,
            ["server_port"] = port,
            ["username"] = username,
            ["password"] = password
        };

        if (https)
            upstream["tls"] = new { enabled = true, server_name = host };
        else
            upstream["version"] = "5";

        var path = new[] { ExactWindowsPath(app) };
        var rules = new List<object>();
        if (https)
            rules.Add(new { process_path_regex = path, network = "udp", action = "reject" });
        rules.Add(new { process_path_regex = path, network = new[] { "tcp", "udp" }, outbound = "proxy" });

        return JsonSerializer.Serialize(new
        {
            log = new { level = "info", disabled = false },
            dns = new { servers = new[] { new { type = "local", tag = "system" } } },
            inbounds = new[]
            {
                new { type = "tun", tag = "tun", address = new[] { "172.19.0.1/30", "fdfe:dcba:9876::1/126" },
                    auto_route = true, stack = "mixed" }
            },
            outbounds = new object[] { upstream, new { type = "direct", tag = "direct" } },
            route = new { rules, final = "direct", auto_detect_interface = true, default_domain_resolver = "system" }
        });
    }
}
