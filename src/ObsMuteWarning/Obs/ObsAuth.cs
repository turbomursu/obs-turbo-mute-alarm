using System.Security.Cryptography;
using System.Text;

namespace ObsMuteWarning.Obs;

internal static class ObsAuth
{
    /// <summary>
    /// obs-websocket v5 authentication string:
    /// base64(sha256(base64(sha256(password + salt)) + challenge)).
    /// </summary>
    public static string Compute(string password, string salt, string challenge)
    {
        string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }
}
