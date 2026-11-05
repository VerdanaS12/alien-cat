using System.Security.Cryptography;
using System.Text;
using AlienCat.Core.Storage;

namespace AlienCat.App.Services;

/// <summary>Encrypts secrets for the current Windows user (DPAPI).</summary>
internal sealed class DpapiProtector : ISecretProtector
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AlienCat.NotionToken.v1");

    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedValue) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
}
