using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AiChatbot.Data;

public interface IDatabaseSettingsRepository
{
    // null = nothing saved yet (configuration decides)
    DatabaseSettings? Get();
    void Save(DatabaseSettings settings);
}

// For hosts that do not persist settings (tests, or Reports configured only through configuration)
public sealed class NullDatabaseSettingsRepository : IDatabaseSettingsRepository
{
    public DatabaseSettings? Get() => null;
    public void Save(DatabaseSettings settings) => throw new NotSupportedException("Database settings are not persisted here");
}

// database-settings.json next to the app, gitignored. The password is encrypted with Data Protection
// (DPAPI-backed on Windows); everything else is plain text. A missing, corrupt or undecryptable file reads
// as "nothing saved" (or an empty password), never as a crash.
public sealed class JsonDatabaseSettingsRepository(string filePath, IDataProtector protector) : IDatabaseSettingsRepository
{
    private const string Prefix = "enc:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _lock = new();

    public DatabaseSettings? Get()
    {
        lock (_lock)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                var s = JsonSerializer.Deserialize<DatabaseSettings>(File.ReadAllText(filePath), Json);
                if (s is null || !DbMode.IsValid(s.Mode) || !DbAuth.IsValid(s.Auth)) return null;
                return s with { Password = Decrypt(s.Password) };
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    // Atomic write: temp file then move, so a crash never leaves a torn file
    public void Save(DatabaseSettings settings)
    {
        lock (_lock)
        {
            var stored = settings with { Password = settings.Password.Length == 0 ? "" : Prefix + protector.Protect(settings.Password) };
            var temp = filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(stored, Json) + "\n");
            File.Move(temp, filePath, overwrite: true);
        }
    }

    private string Decrypt(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return ""; // never accept a plaintext password from disk
        try { return protector.Unprotect(stored[Prefix.Length..]); }
        catch (CryptographicException) { return ""; } // keys lost or machine changed: the user must re-enter it
    }
}
