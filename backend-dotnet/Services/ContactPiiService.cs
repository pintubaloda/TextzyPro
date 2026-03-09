using System.Security.Cryptography;
using System.Text;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class ContactPiiService(SecretCryptoService crypto, IConfiguration config)
{
    public bool IsEnabled =>
        string.Equals(config["PiiEncryption:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

    public void Protect(Contact contact)
    {
        if (!IsEnabled) return;

        if (!string.IsNullOrWhiteSpace(contact.Name))
            contact.NameEncrypted = ProtectValue(contact.Name);
        if (!string.IsNullOrWhiteSpace(contact.Email))
            contact.EmailEncrypted = ProtectValue(contact.Email);
        if (!string.IsNullOrWhiteSpace(contact.Phone))
        {
            contact.PhoneEncrypted = ProtectValue(contact.Phone);
            contact.PhoneHash = Sha256(contact.Phone.Trim());
        }

        // Keep plaintext empty at rest when encryption is enabled.
        contact.Name = string.Empty;
        contact.Email = string.Empty;
        contact.Phone = string.Empty;
    }

    public string RevealName(Contact contact) => Reveal(contact.NameEncrypted, contact.Name);
    public string RevealEmail(Contact contact) => Reveal(contact.EmailEncrypted, contact.Email);
    public string RevealPhone(Contact contact) => Reveal(contact.PhoneEncrypted, contact.Phone);

    private string Reveal(string encrypted, string fallback)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(encrypted))
            return fallback ?? string.Empty;
        try
        {
            return encrypted.StartsWith("enc:", StringComparison.Ordinal)
                ? crypto.Decrypt(encrypted[4..])
                : crypto.Decrypt(encrypted);
        }
        catch
        {
            return fallback ?? string.Empty;
        }
    }

    private string ProtectValue(string input)
    {
        var enc = crypto.Encrypt(input.Trim());
        return $"enc:{enc}";
    }

    public string ComputePhoneHash(string input)
    {
        var value = (input ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(value) ? string.Empty : Sha256(value);
    }

    private static string Sha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
