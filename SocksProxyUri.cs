internal sealed record SocksProxyUri(string Host, int Port, string Username, string Password)
{
    public static SocksProxyUri? Parse(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("socks5", StringComparison.OrdinalIgnoreCase) ||
            uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6) ||
            uri.Port is < 1 or > 65535 || uri.AbsolutePath != "/" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return null;

        var credentials = uri.UserInfo.Split(':', 2);
        if (uri.UserInfo.Length != 0 && (credentials.Length != 2 || credentials[0].Length == 0))
            return null;

        return new SocksProxyUri(uri.IdnHost.Trim('[', ']'), uri.Port,
            Uri.UnescapeDataString(credentials[0]),
            credentials.Length == 2 ? Uri.UnescapeDataString(credentials[1]) : "");
    }
}
