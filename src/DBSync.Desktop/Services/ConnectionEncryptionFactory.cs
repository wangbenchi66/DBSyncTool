using System.Runtime.InteropServices;

namespace DBSync.Desktop.Services;

public sealed class ConnectionEncryptionFactory : IConnectionEncryption
{
    private readonly IConnectionEncryption _inner;

    public ConnectionEncryptionFactory()
    {
        _inner = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProtectedDataConnectionEncryption()
            : new AesGcmConnectionEncryption(
                Environment.GetEnvironmentVariable("DBSYNC_MASTER_PASSWORD")
                ?? throw new InvalidOperationException("非 Windows 平台需要设置 DBSYNC_MASTER_PASSWORD。"));
    }

    public byte[] Protect(byte[] data) => _inner.Protect(data);

    public byte[] Unprotect(byte[] data) => _inner.Unprotect(data);
}
