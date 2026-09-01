using System.Security.Cryptography;
using System.Runtime.Versioning;
using WBC66.Autofac.Core;

namespace DBSync.Desktop.Services;

[SupportedOSPlatform("windows")]
public sealed class ProtectedDataConnectionEncryption : IConnectionEncryption, IDependency
{
    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}
