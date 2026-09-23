using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ApexAI.Core.Configuration;

public interface ISecretStore
{
    void Set(string key, string value);
    string? Get(string key);
    void Remove(string key);
}

public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _directory;

    public DpapiSecretStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexAI", "secrets");
        Directory.CreateDirectory(_directory);
    }

    public void Set(string key, string value)
    {
        ValidateKey(key);
        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, null, 0);
        File.WriteAllBytes(Path.Combine(_directory, key + ".bin"), protectedBytes);
        CryptographicOperations.ZeroMemory(bytes);
    }

    public string? Get(string key)
    {
        ValidateKey(key);
        var path = Path.Combine(_directory, key + ".bin");
        if (!File.Exists(path)) return null;
        var protectedBytes = File.ReadAllBytes(path);
        var bytes = ProtectedData.Unprotect(protectedBytes, null, 0);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Remove(string key)
    {
        ValidateKey(key);
        var path = Path.Combine(_directory, key + ".bin");
        if (File.Exists(path)) File.Delete(path);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Secret key must be a safe non-empty file name.", nameof(key));
    }

    private static class ProtectedData
    {
        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptProtectData(ref Blob data, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob result);
        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob result);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr handle);

        public static byte[] Protect(byte[] data, byte[]? entropy, int flags)
        {
            var input = new Blob(data);
            var output = new Blob();
            if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, flags, ref output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return output.ToArrayAndFree();
        }

        public static byte[] Unprotect(byte[] data, byte[]? entropy, int flags)
        {
            var input = new Blob(data);
            var output = new Blob();
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, flags, ref output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return output.ToArrayAndFree();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Blob
        {
            public int Length;
            public IntPtr Data;

            public Blob(byte[] data)
            {
                Length = data.Length;
                Data = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, Data, data.Length);
            }

            public byte[] ToArrayAndFree()
            {
                var result = new byte[Length];
                Marshal.Copy(Data, result, 0, Length);
                LocalFree(Data);
                return result;
            }
        }
    }
}
