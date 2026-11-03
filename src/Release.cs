using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    static class ReleaseKey
    {
        public const string FidoRp = "mingent:release";
        public const int Needed = 2;
        const string B64 = "[A-Za-z0-9+/]+={0,2}";
        static readonly Regex KeyText = new Regex("(?<![A-Za-z0-9+/=:])(?:(?:RSA|FIDO):" + B64 + ":" + B64 + "|EC:" + B64 + ")", RegexOptions.CultureInvariant);

        public static string Format(RSAParameters p)
        {
            return "RSA:" + Convert.ToBase64String(Strip(p.Modulus)) + ":" + Convert.ToBase64String(Strip(p.Exponent));
        }

        public static string FormatEc(byte[] x, byte[] y)
        {
            return "EC:" + Convert.ToBase64String(Point(x, y));
        }

        public static string FormatFido(byte[] credentialId, byte[] x, byte[] y)
        {
            return "FIDO:" + Convert.ToBase64String(credentialId) + ":" + Convert.ToBase64String(Point(x, y));
        }

        static byte[] Point(byte[] x, byte[] y)
        {
            if (x == null || y == null || x.Length != 32 || y.Length != 32) throw new ArgumentException("a P-256 point has two 32-byte halves");
            byte[] p = new byte[64];
            Array.Copy(x, 0, p, 0, 32);
            Array.Copy(y, 0, p, 32, 32);
            return p;
        }

        static byte[] Strip(byte[] b)
        {
            int i = 0;
            while (i < b.Length - 1 && b[i] == 0) i++;
            byte[] r = new byte[b.Length - i];
            Array.Copy(b, i, r, 0, r.Length);
            return r;
        }

        static byte[] FromBase64(string s)
        {
            try { return Convert.FromBase64String(s); }
            catch (FormatException) { return null; }
        }

        public static bool Parse(string key, out RSAParameters p)
        {
            p = new RSAParameters();
            string[] parts = (key ?? "").Trim().Split(':');
            if (parts.Length != 3 || parts[0] != "RSA") return false;
            p.Modulus = FromBase64(parts[1]);
            p.Exponent = FromBase64(parts[2]);
            return p.Modulus != null && p.Exponent != null && p.Modulus.Length >= 256 && p.Modulus.Length <= 1024 && p.Modulus[0] != 0 && p.Exponent.Length >= 1 && p.Exponent.Length <= 4;
        }

        public static bool ParseEc(string key, out byte[] point)
        {
            point = null;
            string[] parts = (key ?? "").Trim().Split(':');
            if (parts.Length != 2 || parts[0] != "EC") return false;
            point = FromBase64(parts[1]);
            return point != null && point.Length == 64;
        }

        public static bool ParseFido(string key, out byte[] credentialId, out byte[] point)
        {
            credentialId = null;
            point = null;
            string[] parts = (key ?? "").Trim().Split(':');
            if (parts.Length != 3 || parts[0] != "FIDO") return false;
            credentialId = FromBase64(parts[1]);
            point = FromBase64(parts[2]);
            return credentialId != null && credentialId.Length >= 16 && credentialId.Length <= 1023 && point != null && point.Length == 64;
        }

        public static string Kind(string key)
        {
            RSAParameters r;
            byte[] a, b;
            if (Parse(key, out r)) return "RSA";
            if (ParseEc(key, out a)) return "EC";
            if (ParseFido(key, out a, out b)) return "FIDO";
            return null;
        }

        public static string Material(string key)
        {
            RSAParameters r;
            byte[] a, b;
            if (Parse(key, out r)) return Hex(r.Modulus);
            if (ParseEc(key, out a)) return Hex(a);
            if (ParseFido(key, out a, out b)) return Hex(b);
            return null;
        }

        public static string Fingerprint(string key)
        {
            return Hex(Sha256(Encoding.UTF8.GetBytes((key ?? "").Trim())));
        }

        public static string Short(string fingerprint)
        {
            string f = fingerprint ?? "";
            return f.Length <= 16 ? f : f.Substring(0, 16);
        }

        public static string Hex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static byte[] Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        public static string Base64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static string FidoClientData(string body)
        {
            return "{\"type\":\"webauthn.get\",\"challenge\":\"" + Base64Url(Sha256(Encoding.UTF8.GetBytes(body ?? ""))) + "\",\"origin\":\"" + FidoRp + "\"}";
        }

        public static bool Verify(string key, string body, byte[] authenticator, byte[] signature)
        {
            return Verify(key, body, authenticator, signature, PlatformEcdsa);
        }

        public static bool Verify(string key, string body, byte[] authenticator, byte[] signature, Func<byte[], byte[], byte[], bool> ecdsa)
        {
            if (body == null || signature == null || signature.Length == 0) return false;
            RSAParameters p;
            byte[] point, credentialId;
            if (Parse(key, out p)) return authenticator == null && VerifyRsa(p, Encoding.UTF8.GetBytes(body), signature);
            if (ParseEc(key, out point)) return authenticator == null && signature.Length == 64 && ecdsa(point, Encoding.UTF8.GetBytes(body), signature);
            if (!ParseFido(key, out credentialId, out point) || authenticator == null || authenticator.Length < 37 || authenticator.Length > 1024) return false;
            byte[] rp = Sha256(Encoding.UTF8.GetBytes(FidoRp));
            for (int i = 0; i < 32; i++) if (authenticator[i] != rp[i]) return false;
            if ((authenticator[32] & 0x01) == 0) return false;
            byte[] rs = RawSignature(signature);
            if (rs == null) return false;
            byte[] client = Sha256(Encoding.UTF8.GetBytes(FidoClientData(body)));
            byte[] data = new byte[authenticator.Length + client.Length];
            Array.Copy(authenticator, 0, data, 0, authenticator.Length);
            Array.Copy(client, 0, data, authenticator.Length, client.Length);
            return ecdsa(point, data, rs);
        }

        static bool VerifyRsa(RSAParameters p, byte[] data, byte[] signature)
        {
            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(p);
                    return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (Exception) { return false; }
        }

        public static bool PlatformEcdsa(byte[] point, byte[] data, byte[] rs)
        {
            if (point == null || point.Length != 64 || rs == null || rs.Length != 64) return false;
            try
            {
                ECParameters p = new ECParameters();
                p.Curve = ECCurve.NamedCurves.nistP256;
                ECPoint q = new ECPoint();
                q.X = Slice(point, 0, 32);
                q.Y = Slice(point, 32, 32);
                p.Q = q;
                using (ECDsa ec = ECDsa.Create(p)) return ec.VerifyData(data, rs, HashAlgorithmName.SHA256);
            }
            catch (Exception) { return false; }
        }

        public static byte[] RawSignature(byte[] der)
        {
            if (der == null || der.Length < 8 || der.Length > 72 || der[0] != 0x30 || der[1] != der.Length - 2) return null;
            int at = 2;
            byte[] r = DerInteger(der, ref at), s = DerInteger(der, ref at);
            if (r == null || s == null || at != der.Length) return null;
            byte[] rs = new byte[64];
            Array.Copy(r, 0, rs, 32 - r.Length, r.Length);
            Array.Copy(s, 0, rs, 64 - s.Length, s.Length);
            return rs;
        }

        static byte[] DerInteger(byte[] der, ref int at)
        {
            if (at + 2 > der.Length || der[at] != 0x02) return null;
            int len = der[at + 1];
            at += 2;
            if (len < 1 || len > 33 || at + len > der.Length) return null;
            int start = at;
            at += len;
            if ((der[start] & 0x80) != 0) return null;
            if (len > 1 && der[start] == 0 && (der[start + 1] & 0x80) == 0) return null;
            while (len > 0 && der[start] == 0) { start++; len--; }
            if (len == 0 || len > 32) return null;
            return Slice(der, start, len);
        }

        static byte[] Slice(byte[] b, int from, int n)
        {
            byte[] r = new byte[n];
            Array.Copy(b, from, r, 0, n);
            return r;
        }

        public static string FidoKeyFrom(byte[] auth, out string error)
        {
            error = "the security key sent a new key MinGent can't read";
            if (auth == null || auth.Length < 55) return null;
            byte[] rp = Sha256(Encoding.UTF8.GetBytes(FidoRp));
            for (int i = 0; i < 32; i++) if (auth[i] != rp[i]) { error = "the security key answered for another site"; return null; }
            if ((auth[32] & 0x01) == 0) { error = "the security key was not touched"; return null; }
            if ((auth[32] & 0x40) == 0) { error = "the security key sent no new key"; return null; }
            int at = 37 + 16;
            int idLength = (auth[at] << 8) | auth[at + 1];
            at += 2;
            if (idLength < 16 || idLength > 1023 || at + idLength > auth.Length) return null;
            byte[] id = Slice(auth, at, idLength);
            at += idLength;
            int major;
            long count;
            if (!Cbor(auth, ref at, out major, out count) || major != 5 || count > 16) return null;
            long kty = 0, alg = 0, crv = 0;
            byte[] x = null, y = null;
            for (long i = 0; i < count; i++)
            {
                int mk, mv;
                long k, v;
                if (!Cbor(auth, ref at, out mk, out k) || (mk != 0 && mk != 1)) return null;
                if (mk == 1) k = -1 - k;
                if (!Cbor(auth, ref at, out mv, out v)) return null;
                if (mv == 0 || mv == 1)
                {
                    long value = mv == 1 ? -1 - v : v;
                    if (k == 1) kty = value;
                    else if (k == 3) alg = value;
                    else if (k == -1) crv = value;
                }
                else if (mv == 2)
                {
                    if (v > 1024 || at + v > auth.Length) return null;
                    byte[] bytes = Slice(auth, at, (int)v);
                    at += (int)v;
                    if (k == -2) x = bytes;
                    else if (k == -3) y = bytes;
                }
                else return null;
            }
            if (kty != 2 || alg != -7 || crv != 1 || x == null || y == null || x.Length != 32 || y.Length != 32) { error = "the security key made a kind of key MinGent doesn't use"; return null; }
            error = null;
            return FormatFido(id, x, y);
        }

        static bool Cbor(byte[] b, ref int at, out int major, out long value)
        {
            major = -1;
            value = 0;
            if (at >= b.Length) return false;
            int head = b[at++];
            major = head >> 5;
            int info = head & 31;
            if (info < 24) { value = info; return true; }
            if (info > 26) return false;
            int n = info == 24 ? 1 : info == 25 ? 2 : 4;
            if (at + n > b.Length) return false;
            for (int i = 0; i < n; i++) value = (value << 8) | b[at++];
            return true;
        }

        public static string KeysSource(IEnumerable<string> keys)
        {
            StringBuilder sb = new StringBuilder("namespace MinGent\n{\n    static class ReleaseKeys\n    {\n        public static readonly string[] Pinned =\n        {\n");
            foreach (string k in keys) sb.Append("            \"").Append(k).Append("\",\n");
            sb.Append("        };\n    }\n}\n");
            return sb.ToString();
        }

        public static string Pinned(string source, List<string> keys)
        {
            string text = (source ?? "").Replace("\r\n", "\n");
            if (text.Length > 0 && text[0] == '\ufeff') text = text.Substring(1);
            List<string> found = InText(text), material = new List<string>();
            if (text.Trim() != KeysSource(found).Trim()) return "the new version's release key file isn't in the form MinGent expects";
            foreach (string k in found)
            {
                string m = Material(k);
                if (m == null) return "the new version pins a release key MinGent can't use";
                if (material.Contains(m)) return "the new version pins the same key twice";
                material.Add(m);
                keys.Add(k);
            }
            if (keys.Count < Needed) return "the new version pins fewer than two release keys, so it could not check the updates after it";
            return null;
        }

        public static List<string> InText(string text)
        {
            List<string> keys = new List<string>();
            foreach (Match m in KeyText.Matches(text ?? "")) keys.Add(m.Value);
            return keys;
        }

        public static string FileHash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return Hex(sha.ComputeHash(fs));
        }
    }

    class ReleaseSignature
    {
        public string Key = "";
        public byte[] Authenticator, Signature;
    }

    class ReleaseNote
    {
        public const string Head = "MinGent release signature";
        public const int MaxSignatures = 8;
        public string Version = "", File = "", Sha256 = "", Body = "";
        public long Bytes = -1;
        public List<string> Keys = new List<string>();
        public List<ReleaseSignature> Signatures = new List<ReleaseSignature>();

        public static string Compose(string version, string file, long bytes, string sha256, IEnumerable<string> keyFingerprints)
        {
            List<string> fps = new List<string>();
            foreach (string f in keyFingerprints) fps.Add((f ?? "").Trim().ToLowerInvariant());
            return Head + "\nversion: " + version + "\nfile: " + file + "\nbytes: " + bytes.ToString(CultureInfo.InvariantCulture) +
                   "\nsha256: " + (sha256 ?? "").ToLowerInvariant() + "\nkeys: " + string.Join(" ", fps.ToArray()) + "\n";
        }

        public string Text()
        {
            StringBuilder sb = new StringBuilder(Body);
            foreach (ReleaseSignature s in Signatures)
            {
                sb.Append("\nsigned-with: ").Append(s.Key).Append('\n');
                if (s.Authenticator != null) sb.Append("authenticator: ").Append(Convert.ToBase64String(s.Authenticator)).Append('\n');
                sb.Append("signature: ").Append(Convert.ToBase64String(s.Signature)).Append('\n');
            }
            return sb.ToString();
        }

        public void Put(ReleaseSignature s)
        {
            Signatures.RemoveAll(delegate (ReleaseSignature o) { return o.Key == s.Key; });
            Signatures.Add(s);
        }

        public static ReleaseNote Parse(string text, out string error)
        {
            error = null;
            string t = (text ?? "").Replace("\r\n", "\n");
            if (t.Length > 0 && t[0] == '\ufeff') t = t.Substring(1);
            if (!t.StartsWith(Head + "\n", StringComparison.Ordinal)) { error = "it is not a MinGent release signature"; return null; }
            string[] parts = t.TrimEnd('\n', ' ', '\t').Split(new string[] { "\n\n" }, StringSplitOptions.None);
            ReleaseNote n = new ReleaseNote();
            n.Body = parts[0] + "\n";
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string[] lines = parts[0].Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                int c = line.IndexOf(": ", StringComparison.Ordinal);
                if (c <= 0) { error = "it has a line MinGent can't read"; return null; }
                string k = line.Substring(0, c), v = line.Substring(c + 2).Trim();
                if (!seen.Add(k)) { error = "it gives " + k + " twice"; return null; }
                switch (k)
                {
                    case "version": n.Version = v; break;
                    case "file": n.File = v; break;
                    case "sha256": n.Sha256 = v.ToLowerInvariant(); break;
                    case "keys":
                        foreach (string fp in v.ToLowerInvariant().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (!Regex.IsMatch(fp, "^[0-9a-f]{64}$") || n.Keys.Contains(fp)) { error = "its list of keys is damaged"; return null; }
                            n.Keys.Add(fp);
                        }
                        break;
                    case "bytes":
                        long b;
                        if (!long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out b)) { error = "its size is not a number"; return null; }
                        n.Bytes = b;
                        break;
                }
            }
            if (n.Version.Length == 0 || n.File.Length == 0 || n.Bytes < 0 || !Regex.IsMatch(n.Sha256, "^[0-9a-f]{64}$") || n.Keys.Count < ReleaseKey.Needed)
            {
                error = "it is missing the version, file, size, checksum or keys";
                return null;
            }
            for (int p = 1; p < parts.Length; p++)
            {
                if (parts[p].Trim().Length == 0) continue;
                if (n.Signatures.Count >= MaxSignatures) { error = "it has too many signatures"; return null; }
                ReleaseSignature s = Block(parts[p], out error);
                if (s == null) return null;
                n.Signatures.Add(s);
            }
            return n;
        }

        static ReleaseSignature Block(string block, out string error)
        {
            error = null;
            ReleaseSignature s = new ReleaseSignature();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in block.Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                int c = line.IndexOf(": ", StringComparison.Ordinal);
                if (c <= 0) { error = "it has a line MinGent can't read"; return null; }
                string k = line.Substring(0, c), v = line.Substring(c + 2).Trim();
                if (!seen.Add(k)) { error = "a signature gives " + k + " twice"; return null; }
                try
                {
                    if (k == "signed-with") s.Key = v.ToLowerInvariant();
                    else if (k == "authenticator") s.Authenticator = Convert.FromBase64String(v);
                    else if (k == "signature") s.Signature = Convert.FromBase64String(v);
                }
                catch (FormatException) { error = "a signature is damaged"; return null; }
            }
            if (!Regex.IsMatch(s.Key, "^[0-9a-f]{64}$") || s.Signature == null || s.Signature.Length == 0 || s.Signature.Length > 1024 || (s.Authenticator != null && s.Authenticator.Length > 1024))
            {
                error = "a signature is incomplete";
                return null;
            }
            return s;
        }

        public List<string> SignedBy(IEnumerable<string> trusted)
        {
            return SignedBy(trusted, ReleaseKey.PlatformEcdsa);
        }

        public List<string> SignedBy(IEnumerable<string> trusted, Func<byte[], byte[], byte[], bool> ecdsa)
        {
            List<string> ok = new List<string>();
            List<string> material = new List<string>();
            foreach (string key in trusted)
            {
                if (string.IsNullOrEmpty(key) || ok.Contains(key)) continue;
                string fp = ReleaseKey.Fingerprint(key), m = ReleaseKey.Material(key);
                if (m == null || material.Contains(m)) continue;
                foreach (ReleaseSignature s in Signatures)
                    if (s.Key == fp && ReleaseKey.Verify(key, Body, s.Authenticator, s.Signature, ecdsa))
                    {
                        ok.Add(key);
                        material.Add(m);
                        break;
                    }
            }
            return ok;
        }

        public string KeysMatch(string releaseKeysSource)
        {
            List<string> keys = new List<string>();
            string error = ReleaseKey.Pinned(releaseKeysSource, keys);
            if (error != null) return error;
            List<string> found = new List<string>();
            foreach (string k in keys) found.Add(ReleaseKey.Fingerprint(k));
            if (found.Count != Keys.Count) return "the new version doesn't pin the release keys its signature names";
            foreach (string fp in found) if (!Keys.Contains(fp)) return "the new version doesn't pin the release keys its signature names";
            foreach (string fp in Keys) if (!found.Contains(fp)) return "the new version doesn't pin the release keys its signature names";
            return null;
        }
    }

    static class ReleaseVersion
    {
        public static readonly Regex ZipName = new Regex(@"^MinGent-([0-9]{1,6}\.[0-9]{1,6}\.[0-9]{1,6}(?:-[A-Za-z][A-Za-z0-9]{0,19})?)\.zip\z", RegexOptions.CultureInvariant);
        static readonly Regex Parts = new Regex(@"^[vV]?([0-9]{1,6})\.([0-9]{1,6})(?:\.([0-9]{1,6}))?(?:[- ]([A-Za-z][A-Za-z0-9]{0,19}))?$", RegexOptions.CultureInvariant);

        public static bool Exact(string v)
        {
            Match m = ZipName.Match("MinGent-" + (v ?? "") + ".zip");
            return m.Success && m.Groups[1].Value == v;
        }

        public static int[] Numbers(string v)
        {
            Match m = Parts.Match((v ?? "").Trim());
            if (!m.Success) return null;
            return new int[] { int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0 };
        }

        static string Suffix(string v)
        {
            Match m = Parts.Match((v ?? "").Trim());
            return m.Success ? m.Groups[4].Value.ToLowerInvariant() : "";
        }

        public static int Compare(string a, string b)
        {
            int[] x = Numbers(a), y = Numbers(b);
            if (x == null || y == null) return x == null ? (y == null ? 0 : -1) : 1;
            for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            string sa = Suffix(a), sb = Suffix(b);
            if (sa.Length == 0 || sb.Length == 0) return (sa.Length == 0).CompareTo(sb.Length == 0);
            MatchCollection pa = Regex.Matches(sa, @"[0-9]+|[^0-9]+"), pb = Regex.Matches(sb, @"[0-9]+|[^0-9]+");
            for (int i = 0; i < Math.Min(pa.Count, pb.Count); i++)
            {
                long na, nb;
                int c = long.TryParse(pa[i].Value, NumberStyles.None, CultureInfo.InvariantCulture, out na) && long.TryParse(pb[i].Value, NumberStyles.None, CultureInfo.InvariantCulture, out nb)
                    ? na.CompareTo(nb) : string.CompareOrdinal(pa[i].Value, pb[i].Value);
                if (c != 0) return c;
            }
            return pa.Count.CompareTo(pb.Count);
        }

        public static string Display(string v)
        {
            string s = (v ?? "").Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            return s.Replace('-', ' ');
        }
    }

    static class ReleaseZip
    {
        public const long MaxUnpacked = 256L * 1024 * 1024;

        public static string Extract(string zipPath, string dest)
        {
            char sep = Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(dest).TrimEnd(sep) + sep;
            try
            {
                using (ZipArchive za = ZipFile.OpenRead(zipPath))
                {
                    string top = null;
                    foreach (ZipArchiveEntry e in za.Entries)
                    {
                        string n = e.FullName.Replace('\\', '/');
                        int slash = n.IndexOf('/');
                        string first = slash < 0 ? "" : n.Substring(0, slash);
                        if (top == null) top = first;
                        else if (top != first) top = "";
                    }
                    long total = 0;
                    int files = 0;
                    foreach (ZipArchiveEntry e in za.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        string n = e.FullName.Replace('\\', '/');
                        if (!string.IsNullOrEmpty(top)) n = n.Substring(top.Length + 1);
                        string check = "/" + n + "/";
                        if (n.Length == 0 || n.StartsWith("/", StringComparison.Ordinal) || n.IndexOf(':') >= 0 || check.Contains("/../") || check.Contains("/./") || check.Contains("//"))
                            return "the archive holds an unsafe path (" + e.FullName + ")";
                        string full = Path.GetFullPath(Path.Combine(dest, n.Replace('/', sep)));
                        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "the archive holds an unsafe path (" + e.FullName + ")";
                        total += e.Length;
                        if (total > MaxUnpacked || ++files > 5000) return "the archive unpacks to an unexpected size";
                        Directory.CreateDirectory(Path.GetDirectoryName(full));
                        e.ExtractToFile(full, false);
                    }
                    if (files == 0) return "the archive is empty";
                }
            }
            catch (Exception ex) { return "the archive could not be unpacked: " + ex.Message; }
            return null;
        }
    }
}
