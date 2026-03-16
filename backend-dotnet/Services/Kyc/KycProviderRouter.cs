namespace Textzy.Api.Services.Kyc;

public class KycProviderRouter(IEnumerable<IKycProvider> providers)
{
    private readonly Dictionary<string, IKycProvider> _map =
        providers.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public IKycProvider Resolve(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) code = "digilocker";
        if (_map.TryGetValue(code.Trim(), out var p)) return p;
        throw new InvalidOperationException($"Unsupported KYC provider '{code}'.");
    }
}

