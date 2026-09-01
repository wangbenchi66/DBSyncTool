using System.Security.Cryptography;
using System.Text;

namespace DBSync.Desktop.Services;

public sealed class AesGcmConnectionEncryption(string masterPassword) : IConnectionEncryption
{
    private static readonly byte[] Salt = "DBSyncTool.Connection"u8.ToArray();
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key = Rfc2898DeriveBytes.Pbkdf2(
        masterPassword,
        Salt,
        100_000,
        HashAlgorithmName.SHA256,
        KeySize);

    public byte[] Protect(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);

        var result = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, result, nonce.Length + tag.Length, cipher.Length);
        return result;
    }

    public byte[] Unprotect(byte[] data)
    {
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("连接配置数据无效。");

        var nonce = data[..NonceSize];
        var tag = data[NonceSize..(NonceSize + TagSize)];
        var cipher = data[(NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
