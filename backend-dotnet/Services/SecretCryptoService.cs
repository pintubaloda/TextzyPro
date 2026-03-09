using System.Security.Cryptography;
using System.Text;

namespace Textzy.Api.Services;

public class SecretCryptoService(IConfiguration config, IHostEnvironment env)
{
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes(ResolveMasterKey(config, env)));

    public string Encrypt(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return string.Empty;
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var bytes = Encoding.UTF8.GetBytes(plain);
        var encrypted = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        return $"{Convert.ToBase64String(aes.IV)}:{Convert.ToBase64String(encrypted)}";
    }

    public string Decrypt(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return string.Empty;
        var parts = payload.Split(':', 2);
        if (parts.Length != 2) return string.Empty;
        var iv = Convert.FromBase64String(parts[0]);
        var cipher = Convert.FromBase64String(parts[1]);
        using var aes = Aes.Create();
        aes.Key = _key;
        using var decryptor = aes.CreateDecryptor(aes.Key, iv);
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static string ResolveMasterKey(IConfiguration config, IHostEnvironment env)
    {
        var configured = (config["Secrets:MasterKey"] ?? string.Empty).Trim();
        if (env.IsProduction())
        {
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("Secrets:MasterKey is required in Production.");
            if (configured.Length < 32)
                throw new InvalidOperationException("Secrets:MasterKey must be at least 32 characters in Production.");
            return configured;
        }

        return string.IsNullOrWhiteSpace(configured)
            ? "textzy-dev-master-key-change-in-prod"
            : configured;
    }
}
