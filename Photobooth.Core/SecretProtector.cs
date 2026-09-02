using System.Security.Cryptography;
using System.Text;

namespace Photobooth.Core;

/// <summary>
/// DPAPI (current-user scope) protect/unprotect for secrets stored in the
/// database -- the SMTP password and Twilio auth token on SharingSettings.
/// Same scope/algorithm Photobooth.Data's BoothConfiguration.Protect already
/// uses for the DB connection string; duplicated here rather than referenced
/// from there since Photobooth.Core has no dependency on Photobooth.Data,
/// and both AdminWindow's save path (Photobooth.UI, which already
/// references Core) and the real delivery services below (also in Core)
/// need it. Current-user scope means the encrypted value only decrypts
/// under the same Windows account that encrypted it, on the same machine --
/// fine for a single dedicated booth-machine account, same assumption
/// BoothConfiguration already makes for the connection string.
/// </summary>
// DPAPI (ProtectedData) is Windows-only, same as System.Drawing.Common
// elsewhere in this project -- fine, since this whole solution already only
// runs on the Windows booth machine. Suppressed the same way
// BoothConfiguration.Protect/Unprotect already are, rather than
// [SupportedOSPlatform]-annotating the whole class.
#pragma warning disable CA1416
public static class SecretProtector
{
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return "";
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return "";
        }

        byte[] protectedBytes = Convert.FromBase64String(protectedText);
        byte[] plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
#pragma warning restore CA1416
