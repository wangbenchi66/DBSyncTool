using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DBSync.Core.Snapshot;

/// <summary>
/// .dbsync 文件头和 AES-256 加解密工具。
///</summary>
internal static class SnapshotFileFormat
{
    private static readonly byte[] Magic = "DBSYNC1"u8.ToArray();
    private const int SaltLength = 16;
    private const int IvLength = 16;
    private const int KeyLength = 32;
    private const int Iterations = 100_000;

    /// <summary>
    /// 写入明文文件头。
    /// </summary>
    /// <param name="stream">输出流</param>
    /// <param name="passwordHint">密码提示</param>
    /// <returns>加密参数</returns>
    internal static async Task<SnapshotEncryptionHeader> WriteHeaderAsync(Stream stream, string? passwordHint)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var iv = RandomNumberGenerator.GetBytes(IvLength);
        var hintBytes = string.IsNullOrEmpty(passwordHint) ? [] : Encoding.UTF8.GetBytes(passwordHint);
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, hintBytes.Length);

        await stream.WriteAsync(Magic);
        await stream.WriteAsync(salt);
        await stream.WriteAsync(iv);
        await stream.WriteAsync(lengthBytes);
        await stream.WriteAsync(hintBytes);

        return new SnapshotEncryptionHeader(salt, iv, passwordHint);
    }

    /// <summary>
    /// 读取明文文件头。
    /// </summary>
    /// <param name="stream">输入流</param>
    /// <returns>加密参数</returns>
    internal static async Task<SnapshotEncryptionHeader> ReadHeaderAsync(Stream stream)
    {
        var magic = await ReadExactlyAsync(stream, Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidOperationException("不是有效的 .dbsync 文件。");

        var salt = await ReadExactlyAsync(stream, SaltLength);
        var iv = await ReadExactlyAsync(stream, IvLength);
        var lengthBytes = await ReadExactlyAsync(stream, 4);
        var hintLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (hintLength < 0)
            throw new InvalidOperationException("不是有效的 .dbsync 文件。");

        var hintBytes = await ReadExactlyAsync(stream, hintLength);
        var passwordHint = hintBytes.Length == 0 ? null : Encoding.UTF8.GetString(hintBytes);
        return new SnapshotEncryptionHeader(salt, iv, passwordHint);
    }

    /// <summary>
    /// 创建 AES 加密流。
    /// </summary>
    /// <param name="outputStream">输出流</param>
    /// <param name="password">密码</param>
    /// <param name="header">加密参数</param>
    /// <returns>加密流</returns>
    internal static CryptoStream CreateEncryptStream(Stream outputStream, string password, SnapshotEncryptionHeader header)
    {
        var aes = CreateAes(password, header);
        return new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
    }

    /// <summary>
    /// 创建 AES 解密流。
    /// </summary>
    /// <param name="inputStream">输入流</param>
    /// <param name="password">密码</param>
    /// <param name="header">加密参数</param>
    /// <returns>解密流</returns>
    internal static CryptoStream CreateDecryptStream(Stream inputStream, string password, SnapshotEncryptionHeader header)
    {
        var aes = CreateAes(password, header);
        return new CryptoStream(inputStream, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
    }

    /// <summary>
    /// 读取指定数量的字节。
    /// </summary>
    /// <param name="stream">输入流</param>
    /// <param name="count">字节数</param>
    /// <returns>读取到的字节</returns>
    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer);
        return buffer;
    }

    /// <summary>
    /// 根据密码和文件头创建 AES 实例。
    /// </summary>
    /// <param name="password">密码</param>
    /// <param name="header">加密参数</param>
    /// <returns>AES 实例</returns>
    private static Aes CreateAes(string password, SnapshotEncryptionHeader header)
    {
        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = header.Iv;
        aes.Key = Rfc2898DeriveBytes.Pbkdf2(password, header.Salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        return aes;
    }
}

/// <summary>
/// .dbsync 文件加密头。
///</summary>
/// <param name="Salt">PBKDF2 盐值</param>
/// <param name="Iv">AES IV</param>
/// <param name="PasswordHint">明文密码提示</param>
internal sealed record SnapshotEncryptionHeader(byte[] Salt, byte[] Iv, string? PasswordHint);
