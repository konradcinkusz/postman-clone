using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace PostmanClone.EnvironmentsService.Secrets;

/// <summary>
/// Chooses the protector from <c>Secrets:EncryptionKey</c> (base64, 32 bytes). A missing, malformed or wrong-sized key
/// logs a warning that names the setting but never its value, and yields <see cref="UnavailableSecretProtector"/>.
/// </summary>
public static class SecretProtectorFactory
{
    public const string KeySetting = "Secrets:EncryptionKey";

    public static ISecretProtector Create(string? base64Key, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            logger.LogWarning("{Setting} is not set; secret variables are unavailable.", KeySetting);
            return new UnavailableSecretProtector();
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key.Trim());
        }
        catch (FormatException)
        {
            logger.LogWarning("{Setting} is not valid base64; secret variables are unavailable.", KeySetting);
            return new UnavailableSecretProtector();
        }

        if (key.Length != AesGcmSecretProtector.KeySizeInBytes)
        {
            logger.LogWarning("{Setting} must decode to {Bytes} bytes; secret variables are unavailable.",
                KeySetting, AesGcmSecretProtector.KeySizeInBytes);
            CryptographicOperations.ZeroMemory(key);
            return new UnavailableSecretProtector();
        }

        var protector = new AesGcmSecretProtector(key);
        CryptographicOperations.ZeroMemory(key);
        return protector;
    }
}
