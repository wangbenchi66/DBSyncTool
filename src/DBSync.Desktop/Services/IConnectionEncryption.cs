namespace DBSync.Desktop.Services;

public interface IConnectionEncryption
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}
