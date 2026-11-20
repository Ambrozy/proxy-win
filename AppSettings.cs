using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record AppSettings
{
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "AppProxy.settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string[] Applications { get; init; } = [];
    public bool Https { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 1080;
    public string Username { get; init; } = "";
    public bool Ipv4Dns { get; init; }
    public string? EncryptedPassword { get; init; }
    public string? EncryptedPendingUri { get; init; }

    public static AppSettings? Read()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                ?? throw new JsonException("Файл настроек пуст.");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public void Write()
    {
        string temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static string? Protect(string value)
    {
        if (value.Length == 0)
            return null;

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        byte[] bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
