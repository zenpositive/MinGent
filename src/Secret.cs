using System;
using System.Security.Cryptography;
using System.Text;

namespace MinGent
{
    static class Secret
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("JustPad.apikey.v1");
        static readonly bool Win = System.IO.Path.DirectorySeparatorChar == '\\';

        public static string Protect(string plain)
        {
            if (plain == null) plain = "";
            if (Win)
            {
                try
                {
                    byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                    return "DPAPI:" + Convert.ToBase64String(enc);
                }
                catch { return null; }
            }
            return "B64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                if (stored.StartsWith("DPAPI:"))
                {
                    byte[] raw = Convert.FromBase64String(stored.Substring(6));
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.CurrentUser));
                }
                if (stored.StartsWith("B64:"))
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(4)));
            }
            catch { }
            return "";
        }
    }
}
