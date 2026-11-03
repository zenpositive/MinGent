using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MinGent;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

static class ReleaseSigner
{
    const string Tpm = "Microsoft Platform Crypto Provider", RecoveryHead = "MinGent recovery key", NoteSuffix = ".sig.txt";
    const int Iterations = 600000, MinPinned = 2;

    class ZipRelease
    {
        public string Name = "", Version = "", Sha256 = "";
        public long Bytes;
        public List<string> Pinned = new List<string>();
        public List<string> Fingerprints = new List<string>();
    }

    static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 3 && args[0] == "key" && args[1] == "tpm") return NewTpmKey(args[2], args.Length >= 4 ? args[3] : null);
            if (args.Length >= 2 && args[0] == "key" && args[1] == "security-key") return NewSecurityKey(args.Length >= 3 ? args[2] : null);
            if (args.Length >= 3 && args[0] == "key" && args[1] == "recovery") return NewRecoveryKey(args[2]);
            if (args.Length >= 3 && args[0] == "key" && args[1] == "show") return Show(args[2], args.Length >= 4 ? args[3] : null);
            if (args.Length >= 3 && args[0] == "key" && args[1] == "delete") return Delete(args[2]);
            if (args.Length >= 3 && args[0] == "sign") return Sign(args[1], Rest(args, 2));
            if (args.Length >= 3 && args[0] == "verify") return Verify(args[1], args[2], args.Length >= 4 ? args[3] : null);
            if (args.Length >= 3 && args[0] == "practice") return Practice(args[1], args[2]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed: " + ex.Message);
            return 1;
        }
        Console.WriteLine("MinGent release signer");
        Console.WriteLine();
        Console.WriteLine("MinGent installs an update once two of the keys its own version pins have signed it.");
        Console.WriteLine();
        Console.WriteLine("  key tpm <name> [file]      Make a key inside this PC's TPM. Signing with it needs no PIN.");
        Console.WriteLine("  key security-key [file]    Register a security key such as a YubiKey. Windows asks you to touch it.");
        Console.WriteLine("                             With a file, the key is also written there, for src\\ReleaseKeys.cs.");
        Console.WriteLine("  key recovery <file>        Make a spare key in a password-protected file, kept away from this PC.");
        Console.WriteLine("  key show <tpm:name> [file] Print a TPM key for src\\ReleaseKeys.cs, and its fingerprint.");
        Console.WriteLine("  key delete <tpm:name>      Delete a TPM key made for practice.");
        Console.WriteLine("  sign <zip> <signer>...     Sign a release zip with tpm:<name>, security-key, or a spare key file.");
        Console.WriteLine("                             Each signature is added to <zip>.sig.txt.");
        Console.WriteLine("  verify <zip> <keys> [sig]  Check a release against the keys an older version pins:");
        Console.WriteLine("                             its src\\ReleaseKeys.cs, its release zip, or one key.");
        Console.WriteLine("  practice <folder> <zip>    Put a signed zip in a folder the updater can use for a practice run.");
        return 2;
    }

    static List<string> Rest(string[] args, int from)
    {
        List<string> r = new List<string>();
        for (int i = from; i < args.Length; i++) r.Add(args[i]);
        return r;
    }

    static CngProvider TpmProvider { get { return new CngProvider(Tpm); } }

    static string TpmName(string name)
    {
        return name.StartsWith("tpm:", StringComparison.OrdinalIgnoreCase) ? name.Substring(4) : name;
    }

    static void Save(string file, string key)
    {
        if (file == null) return;
        File.WriteAllText(file, key + "\r\n", new UTF8Encoding(false));
        Console.WriteLine("Also written to " + file + ".");
    }

    static int NewTpmKey(string name, string file)
    {
        name = TpmName(name);
        if (CngKey.Exists(name, TpmProvider)) { Console.Error.WriteLine("A TPM key named " + name + " already exists."); return 1; }
        CngKeyCreationParameters cp = new CngKeyCreationParameters();
        cp.Provider = TpmProvider;
        cp.KeyUsage = CngKeyUsages.Signing;
        cp.ExportPolicy = CngExportPolicies.None;
        using (CngKey key = CngKey.Create(CngAlgorithm.ECDsaP256, name, cp))
        {
            string text = EcKey(key);
            Console.WriteLine("TPM key " + name + " is ready. It can't leave this PC's chip, and signing with it needs no PIN.");
            Print(text);
            Save(file, text);
        }
        return 0;
    }

    static string EcKey(CngKey key)
    {
        byte[] blob = key.Export(CngKeyBlobFormat.EccPublicBlob);
        if (blob.Length != 72 || BitConverter.ToUInt32(blob, 0) != 0x31534345 || BitConverter.ToUInt32(blob, 4) != 32) throw new InvalidDataException("the TPM key is not a P-256 signing key");
        return ReleaseKey.FormatEc(Slice(blob, 8, 32), Slice(blob, 40, 32));
    }

    static int NewSecurityKey(string file)
    {
        if (file != null && File.Exists(file)) { Console.Error.WriteLine(file + " already exists."); return 1; }
        Console.WriteLine("Windows asks you to insert your security key and touch it. Use the key you will sign releases with.");
        string clientData = "{\"type\":\"webauthn.create\",\"challenge\":\"" + ReleaseKey.Base64Url(Random(32)) + "\",\"origin\":\"" + ReleaseKey.FidoRp + "\"}";
        byte[] auth, credentialId;
        WebAuthn.MakeCredential(ReleaseKey.FidoRp, "MinGent releases", Random(16), "MinGent release signing", clientData, out auth, out credentialId);
        string err;
        string key = ReleaseKey.FidoKeyFrom(auth, out err);
        if (key == null) { Console.Error.WriteLine("Failed: " + err + "."); return 1; }
        byte[] id, point;
        ReleaseKey.ParseFido(key, out id, out point);
        if (credentialId.Length > 0 && Convert.ToBase64String(credentialId) != Convert.ToBase64String(id)) { Console.Error.WriteLine("Failed: Windows and the security key name different credentials."); return 1; }
        Console.WriteLine("Touch it once more, to check that it signs.");
        string body = "MinGent security key check " + Guid.NewGuid().ToString("N");
        string used;
        ReleaseSignature s = SignFido(new List<string>(new string[] { key }), body, out used);
        if (!ReleaseKey.Verify(key, body, s.Authenticator, s.Signature)) { Console.Error.WriteLine("Failed: the security key's test signature did not check out."); return 1; }
        Console.WriteLine();
        Console.WriteLine("Security key registered. Its private key stays on the security key.");
        Console.WriteLine("Don't reset the security key's FIDO settings: that erases this key for good.");
        Print(key);
        Save(file, key);
        return 0;
    }

    static int NewRecoveryKey(string file)
    {
        if (File.Exists(file)) { Console.Error.WriteLine(file + " already exists."); return 1; }
        string pass = Password("Password for the recovery key (at least 12 characters): ");
        if (pass.Length < 12) { Console.Error.WriteLine("The password is too short."); return 1; }
        if (Password("The same password again: ") != pass) { Console.Error.WriteLine("The passwords don't match."); return 1; }
        using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048, new CspParameters(24)))
        {
            rsa.PersistKeyInCsp = false;
            string text = ReleaseKey.Format(rsa.ExportParameters(false));
            byte[] blob = rsa.ExportCspBlob(true);
            try { File.WriteAllText(file, Seal(text, blob, pass), new UTF8Encoding(false)); }
            finally { Array.Clear(blob, 0, blob.Length); }
            Console.WriteLine();
            Console.WriteLine("Recovery key saved to " + file + ". Keep it on the USB stick, away from this PC, and don't lose the password.");
            Print(text);
        }
        return 0;
    }

    static void Print(string key)
    {
        Console.WriteLine("Key:         " + key);
        Console.WriteLine("Fingerprint: " + ReleaseKey.Fingerprint(key));
    }

    static int Show(string which, string file)
    {
        string key;
        if (File.Exists(which)) key = Field(File.ReadAllText(which), "key");
        else using (CngKey k = CngKey.Open(TpmName(which), TpmProvider)) key = EcKey(k);
        Print(key);
        Save(file, key);
        return 0;
    }

    static int Delete(string name)
    {
        if (TpmName(name).IndexOf("practice", StringComparison.OrdinalIgnoreCase) < 0)
        {
            Console.Error.WriteLine("Only practice keys can be deleted here, ones with \"practice\" in their name. " + TpmName(name) + " was kept.");
            return 1;
        }
        using (CngKey key = CngKey.Open(TpmName(name), TpmProvider)) key.Delete();
        Console.WriteLine("Deleted TPM key " + TpmName(name) + ".");
        return 0;
    }

    static string Seal(string publicKey, byte[] blob, string pass)
    {
        byte[] salt = Random(16), iv = Random(16), enc, mac;
        byte[] keys = Derive(pass, salt);
        try
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Slice(keys, 0, 32);
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform t = aes.CreateEncryptor()) enc = t.TransformFinalBlock(blob, 0, blob.Length);
            }
            using (HMACSHA256 h = new HMACSHA256(Slice(keys, 32, 32))) mac = h.ComputeHash(Concat(salt, iv, enc));
        }
        finally { Array.Clear(keys, 0, keys.Length); }
        return RecoveryHead + "\nkey: " + publicKey + "\nfingerprint: " + ReleaseKey.Fingerprint(publicKey) + "\nkdf: pbkdf2-sha256 " + Iterations.ToString(CultureInfo.InvariantCulture) +
               "\nsalt: " + Convert.ToBase64String(salt) + "\niv: " + Convert.ToBase64String(iv) + "\ndata: " + Convert.ToBase64String(enc) + "\nmac: " + Convert.ToBase64String(mac) + "\n";
    }

    public static byte[] Open(string text, string pass)
    {
        if (!text.Replace("\r\n", "\n").StartsWith(RecoveryHead + "\n", StringComparison.Ordinal)) throw new InvalidDataException("this is not a MinGent recovery key file");
        byte[] salt = Convert.FromBase64String(Field(text, "salt")), iv = Convert.FromBase64String(Field(text, "iv"));
        byte[] enc = Convert.FromBase64String(Field(text, "data")), mac = Convert.FromBase64String(Field(text, "mac"));
        byte[] keys = Derive(pass, salt);
        try
        {
            using (HMACSHA256 h = new HMACSHA256(Slice(keys, 32, 32)))
            {
                byte[] want = h.ComputeHash(Concat(salt, iv, enc));
                int diff = want.Length ^ mac.Length;
                for (int i = 0; i < Math.Min(want.Length, mac.Length); i++) diff |= want[i] ^ mac[i];
                if (diff != 0) throw new CryptographicException("wrong password, or the file was changed");
            }
            using (Aes aes = Aes.Create())
            {
                aes.Key = Slice(keys, 0, 32);
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform t = aes.CreateDecryptor()) return t.TransformFinalBlock(enc, 0, enc.Length);
            }
        }
        finally { Array.Clear(keys, 0, keys.Length); }
    }

    static byte[] Derive(string pass, byte[] salt)
    {
        using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(pass, salt, Iterations, HashAlgorithmName.SHA256)) return kdf.GetBytes(64);
    }

    static byte[] Random(int n)
    {
        byte[] b = new byte[n];
        using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b);
        return b;
    }

    static byte[] Slice(byte[] b, int from, int n)
    {
        byte[] r = new byte[n];
        Array.Copy(b, from, r, 0, n);
        return r;
    }

    static byte[] Concat(params byte[][] parts)
    {
        int n = 0;
        foreach (byte[] p in parts) n += p.Length;
        byte[] r = new byte[n];
        int at = 0;
        foreach (byte[] p in parts) { Array.Copy(p, 0, r, at, p.Length); at += p.Length; }
        return r;
    }

    static string Field(string text, string name)
    {
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            if (line.StartsWith(name + ": ", StringComparison.Ordinal)) return line.Substring(name.Length + 2).Trim();
        throw new InvalidDataException("the file has no " + name);
    }

    static string Password(string prompt)
    {
        Console.Write(prompt);
        StringBuilder sb = new StringBuilder();
        if (Console.IsInputRedirected)
        {
            string l = Console.ReadLine() ?? "";
            Console.WriteLine();
            return l;
        }
        while (true)
        {
            ConsoleKeyInfo k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }

    static string ZipText(string zip, string path)
    {
        using (ZipArchive za = ZipFile.OpenRead(zip))
            foreach (ZipArchiveEntry e in za.Entries)
            {
                string n = e.FullName.Replace('\\', '/');
                if (n == path || n.EndsWith("/" + path, StringComparison.Ordinal))
                    using (StreamReader r = new StreamReader(e.Open(), Encoding.UTF8)) return r.ReadToEnd();
            }
        return null;
    }

    static string BuiltProgram(string zip)
    {
        using (ZipArchive za = ZipFile.OpenRead(zip))
            foreach (ZipArchiveEntry e in za.Entries)
            {
                string n = e.FullName.ToLowerInvariant();
                if (n.EndsWith(".exe", StringComparison.Ordinal) || n.EndsWith(".dll", StringComparison.Ordinal)) return e.FullName;
            }
        return null;
    }

    static List<string> KeysFrom(string where)
    {
        if (where.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(where)) return ReleaseKey.InText(ZipText(where, "src/ReleaseKeys.cs") ?? "");
        if (File.Exists(where)) return ReleaseKey.InText(File.ReadAllText(where));
        return ReleaseKey.InText(where);
    }

    static string Kind(string key)
    {
        string k = ReleaseKey.Kind(key);
        return k == "EC" ? "TPM key" : k == "FIDO" ? "security key" : k == "RSA" ? "spare key" : "key";
    }

    static ZipRelease ReadZip(string zip)
    {
        ZipRelease r = new ZipRelease();
        r.Name = Path.GetFileName(zip);
        Match m = ReleaseVersion.ZipName.Match(r.Name);
        if (!m.Success) { Console.Error.WriteLine("The zip must be named like MinGent-1.0.6-beta.zip."); return null; }
        string program = BuiltProgram(zip);
        if (program != null) { Console.Error.WriteLine("The zip holds " + program + ". A release is source only; MinGent builds the program itself."); return null; }
        r.Version = m.Groups[1].Value;
        string keysText = ZipText(zip, "src/ReleaseKeys.cs"), programText = ZipText(zip, "src/Program.cs");
        if (keysText == null || programText == null) { Console.Error.WriteLine("The zip has no src/ReleaseKeys.cs or src/Program.cs."); return null; }
        Match pv = Regex.Match(programText, "AssemblyInformationalVersion\\(\"([^\"]+)\"\\)");
        if (!pv.Success || ReleaseVersion.Compare(pv.Groups[1].Value, r.Version) != 0 || ReleaseVersion.Display(r.Version) != pv.Groups[1].Value)
        {
            Console.Error.WriteLine("The zip's name says " + r.Version + " but its Program.cs says " + (pv.Success ? pv.Groups[1].Value : "nothing") + ".");
            return null;
        }
        string bad = ReleaseKey.Pinned(keysText, r.Pinned);
        if (bad != null)
        {
            Console.Error.WriteLine("src/ReleaseKeys.cs in the zip can't be used: " + bad + ".");
            Console.Error.WriteLine("It must hold only the Pinned list, each key on its own line in quotes with a comma after it, and pin at least " + MinPinned + " keys, such as the TPM key and the security key.");
            return null;
        }
        List<string> kinds = new List<string>();
        foreach (string k in r.Pinned)
        {
            r.Fingerprints.Add(ReleaseKey.Fingerprint(k));
            if (!kinds.Contains(ReleaseKey.Kind(k))) kinds.Add(ReleaseKey.Kind(k));
        }
        if (kinds.Count == 1) Console.WriteLine("Note: every key the zip pins is a " + Kind(r.Pinned[0]) + ". Keys kept on different devices, such as this PC's TPM key and a security key, are harder to lose together.");
        FileInfo fi = new FileInfo(zip);
        r.Bytes = fi.Length;
        r.Sha256 = ReleaseKey.FileHash(zip);
        return r;
    }

    static int Sign(string zip, List<string> signers)
    {
        ZipRelease r = ReadZip(zip);
        if (r == null) return 1;
        string body = ReleaseNote.Compose(r.Version, r.Name, r.Bytes, r.Sha256, r.Fingerprints);
        string notePath = zip + NoteSuffix, err;
        ReleaseNote note;
        if (File.Exists(notePath))
        {
            note = ReleaseNote.Parse(File.ReadAllText(notePath), out err);
            if (note == null) { Console.Error.WriteLine(Path.GetFileName(notePath) + " can't be read: " + err + ". Delete it to start over."); return 1; }
            if (note.Body != body) { Console.Error.WriteLine(Path.GetFileName(notePath) + " is for another build of " + r.Name + ". Delete it to start over."); return 1; }
        }
        else
        {
            note = ReleaseNote.Parse(body, out err);
            if (note == null) { Console.Error.WriteLine("Failed: " + err + "."); return 1; }
        }
        foreach (string signer in signers)
        {
            string key;
            ReleaseSignature s = SignWith(signer, body, r, out key);
            if (s == null) return 1;
            if (!ReleaseKey.Verify(key, body, s.Authenticator, s.Signature)) { Console.Error.WriteLine("The signature from " + signer + " did not check out. Nothing was added."); return 1; }
            note.Put(s);
            File.WriteAllText(notePath, note.Text(), new UTF8Encoding(false));
            Console.WriteLine("Signed with the " + Kind(key) + " " + ReleaseKey.Short(s.Key) + (r.Pinned.Contains(key) ? "." : ". The new version doesn't pin this key."));
        }
        int count = note.SignedBy(r.Pinned).Count;
        Console.WriteLine("Wrote " + notePath + ".");
        if (count >= ReleaseKey.Needed) Console.WriteLine(count + " of the " + r.Pinned.Count + " keys " + r.Name + " pins have signed it, so versions that pin the same keys will install it. Upload it with the zip.");
        else Console.WriteLine(count + " of the " + r.Pinned.Count + " keys " + r.Name + " pins have signed it. It needs " + ReleaseKey.Needed + ".");
        if (count >= ReleaseKey.Needed) Console.WriteLine("If the previous release pins other keys, check with: verify " + r.Name + " <previous release zip>");
        return 0;
    }

    static ReleaseSignature SignWith(string signer, string body, ZipRelease r, out string key)
    {
        key = null;
        if (signer.StartsWith("tpm:", StringComparison.OrdinalIgnoreCase)) return SignTpm(signer.Substring(4), body, out key);
        if (signer == "security-key" || signer.StartsWith("security-key:", StringComparison.Ordinal))
        {
            List<string> fido = new List<string>();
            foreach (string k in signer == "security-key" ? r.Pinned : KeysFrom(signer.Substring(13))) if (ReleaseKey.Kind(k) == "FIDO") fido.Add(k);
            if (fido.Count == 0) { Console.Error.WriteLine("No security key is pinned " + (signer == "security-key" ? "in " + r.Name : "there") + "."); return null; }
            Console.WriteLine("Windows asks for your security key. Insert it and touch it.");
            return SignFido(fido, body, out key);
        }
        if (File.Exists(signer)) return SignRecovery(signer, body, out key);
        Console.Error.WriteLine("Nothing called " + signer + " can sign. Use tpm:<name>, security-key, or a recovery key file.");
        return null;
    }

    static ReleaseSignature SignTpm(string name, string body, out string key)
    {
        using (CngKey k = CngKey.Open(TpmName(name), TpmProvider))
        using (ECDsaCng ec = new ECDsaCng(k))
        {
            key = EcKey(k);
            ReleaseSignature s = new ReleaseSignature();
            s.Key = ReleaseKey.Fingerprint(key);
            s.Signature = ec.SignData(Encoding.UTF8.GetBytes(body), HashAlgorithmName.SHA256);
            return s;
        }
    }

    static ReleaseSignature SignFido(List<string> keys, string body, out string key)
    {
        List<byte[]> ids = new List<byte[]>();
        foreach (string k in keys)
        {
            byte[] id, point;
            if (ReleaseKey.ParseFido(k, out id, out point)) ids.Add(id);
        }
        byte[] auth, sig, used;
        WebAuthn.GetAssertion(ReleaseKey.FidoRp, ReleaseKey.FidoClientData(body), ids, out auth, out sig, out used);
        key = keys[0];
        foreach (string k in keys)
        {
            byte[] id, point;
            if (ReleaseKey.ParseFido(k, out id, out point) && Convert.ToBase64String(id) == Convert.ToBase64String(used)) key = k;
        }
        ReleaseSignature s = new ReleaseSignature();
        s.Key = ReleaseKey.Fingerprint(key);
        s.Authenticator = auth;
        s.Signature = sig;
        return s;
    }

    static ReleaseSignature SignRecovery(string file, string body, out string key)
    {
        string text = File.ReadAllText(file);
        key = Field(text, "key");
        byte[] blob = Open(text, Password("Password for " + Path.GetFileName(file) + ": "));
        try
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(new CspParameters(24)))
            {
                rsa.PersistKeyInCsp = false;
                rsa.ImportCspBlob(blob);
                if (ReleaseKey.Format(rsa.ExportParameters(false)) != key) throw new InvalidDataException("the file's public key doesn't match its private key");
                ReleaseSignature s = new ReleaseSignature();
                s.Key = ReleaseKey.Fingerprint(key);
                s.Signature = rsa.SignData(Encoding.UTF8.GetBytes(body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return s;
            }
        }
        finally { Array.Clear(blob, 0, blob.Length); }
    }

    static int Verify(string zip, string keysFrom, string sigPath)
    {
        string sigFile = sigPath ?? zip + NoteSuffix, err;
        ReleaseNote note = ReleaseNote.Parse(File.ReadAllText(sigFile), out err);
        if (note == null) { Console.Error.WriteLine("NOT VALID: the signature file can't be read: " + err + "."); return 1; }
        List<string> trusted = KeysFrom(keysFrom);
        if (trusted.Count == 0) { Console.Error.WriteLine("No release keys found in " + keysFrom + "."); return 1; }
        FileInfo fi = new FileInfo(zip);
        if (note.File != fi.Name || note.Bytes != fi.Length || note.Sha256 != ReleaseKey.FileHash(zip)) { Console.Error.WriteLine("NOT VALID: the zip is not the file that was signed."); return 1; }
        err = note.KeysMatch(ZipText(zip, "src/ReleaseKeys.cs") ?? "");
        if (err != null) { Console.Error.WriteLine("NOT VALID: " + err + "."); return 1; }
        List<string> by = note.SignedBy(trusted);
        List<string> names = new List<string>();
        foreach (string k in by) names.Add(Kind(k) + " " + ReleaseKey.Short(ReleaseKey.Fingerprint(k)));
        if (trusted.Count == 1)
        {
            if (by.Count == 0) { Console.Error.WriteLine("NOT VALID: that key did not sign " + fi.Name + "."); return 1; }
            Console.WriteLine("That key signed " + fi.Name + ". MinGent installs it once two of the keys it pins have signed.");
            return 0;
        }
        if (by.Count < ReleaseKey.Needed)
        {
            Console.Error.WriteLine("NOT VALID: " + by.Count + " of those " + trusted.Count + " keys signed " + fi.Name + (names.Count > 0 ? " (" + string.Join(", ", names.ToArray()) + ")" : "") + ", and MinGent needs " + ReleaseKey.Needed + ".");
            return 1;
        }
        Console.WriteLine("Valid: " + fi.Name + " is MinGent " + ReleaseVersion.Display(note.Version) + ", signed with " + by.Count + " of those " + trusted.Count + " keys: " + string.Join(", ", names.ToArray()) + ".");
        Console.WriteLine("It pins " + note.Keys.Count + " keys for the releases after it.");
        return 0;
    }

    static int Practice(string folder, string zip)
    {
        string sig = zip + NoteSuffix;
        if (!File.Exists(zip) || !File.Exists(sig)) { Console.Error.WriteLine("Sign the zip first: both " + Path.GetFileName(zip) + " and " + Path.GetFileName(sig) + " are needed."); return 1; }
        string err;
        ReleaseNote note = ReleaseNote.Parse(File.ReadAllText(sig), out err);
        if (note == null) { Console.Error.WriteLine("The signature file can't be read: " + err + "."); return 1; }
        Directory.CreateDirectory(folder);
        File.Copy(zip, Path.Combine(folder, Path.GetFileName(zip)), true);
        File.Copy(sig, Path.Combine(folder, Path.GetFileName(sig)), true);
        string tag = "v" + note.Version;
        string json = "[{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":true,\"html_url\":\"https://github.com/zenpositive/MinGent/releases/tag/" + tag + "\",\"assets\":[" +
                      "{\"name\":\"" + Path.GetFileName(zip) + "\",\"size\":" + new FileInfo(zip).Length.ToString(CultureInfo.InvariantCulture) + ",\"browser_download_url\":\"" + Path.GetFileName(zip) + "\"}," +
                      "{\"name\":\"" + Path.GetFileName(sig) + "\",\"size\":" + new FileInfo(sig).Length.ToString(CultureInfo.InvariantCulture) + ",\"browser_download_url\":\"" + Path.GetFileName(sig) + "\"}]}]";
        File.WriteAllText(Path.Combine(folder, "releases.json"), json, new UTF8Encoding(false));
        Console.WriteLine("Practice folder ready: " + folder);
        Console.WriteLine("Start the older MinGent with MINGENT_UPDATE_SOURCE set to that folder, then use Settings > General > Check for updates.");
        return 0;
    }
}

static class WebAuthn
{
    const uint CrossPlatform = 2, Discouraged = 3, NoAttestation = 1, Timeout = 120000;
    const int Es256 = -7;

    [StructLayout(LayoutKind.Sequential)]
    struct RpInfo { public uint Version; public IntPtr Id, Name, Icon; }

    [StructLayout(LayoutKind.Sequential)]
    struct UserInfo { public uint Version, IdLength; public IntPtr Id, Name, Icon, DisplayName; }

    [StructLayout(LayoutKind.Sequential)]
    struct CoseParameter { public uint Version; public IntPtr Type; public int Algorithm; }

    [StructLayout(LayoutKind.Sequential)]
    struct Items { public uint Count; public IntPtr First; }

    [StructLayout(LayoutKind.Sequential)]
    struct ClientData { public uint Version, Length; public IntPtr Json, HashAlgorithm; }

    [StructLayout(LayoutKind.Sequential)]
    struct Credential { public uint Version, IdLength; public IntPtr Id, Type; }

    [StructLayout(LayoutKind.Sequential)]
    struct MakeOptions { public uint Version, TimeoutMs; public Items Exclude, Extensions; public uint Attachment; public int RequireResidentKey; public uint UserVerification, Attestation, Flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct GetOptions { public uint Version, TimeoutMs; public Items Allow, Extensions; public uint Attachment, UserVerification, Flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct Attestation { public uint Version; public IntPtr Format; public uint AuthDataLength; public IntPtr AuthData; public uint AttestationLength; public IntPtr AttestationData; public uint DecodeType; public IntPtr Decoded; public uint ObjectLength; public IntPtr Object; public uint CredentialIdLength; public IntPtr CredentialId; }

    [StructLayout(LayoutKind.Sequential)]
    struct Assertion { public uint Version, AuthDataLength; public IntPtr AuthData; public uint SignatureLength; public IntPtr Signature; public Credential Credential; public uint UserIdLength; public IntPtr UserId; }

    [DllImport("webauthn.dll")]
    static extern uint WebAuthNGetApiVersionNumber();

    [DllImport("webauthn.dll")]
    static extern int WebAuthNAuthenticatorMakeCredential(IntPtr hWnd, ref RpInfo rp, ref UserInfo user, ref Items coseParameters, ref ClientData clientData, ref MakeOptions options, out IntPtr attestation);

    [DllImport("webauthn.dll", CharSet = CharSet.Unicode)]
    static extern int WebAuthNAuthenticatorGetAssertion(IntPtr hWnd, string rpId, ref ClientData clientData, ref GetOptions options, out IntPtr assertion);

    [DllImport("webauthn.dll")]
    static extern void WebAuthNFreeCredentialAttestation(IntPtr attestation);

    [DllImport("webauthn.dll")]
    static extern void WebAuthNFreeAssertion(IntPtr assertion);

    [DllImport("webauthn.dll")]
    static extern IntPtr WebAuthNGetErrorName(int hr);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(IntPtr hWnd);

    sealed class Native : IDisposable
    {
        readonly List<IntPtr> blocks = new List<IntPtr>();

        public IntPtr Bytes(byte[] b)
        {
            IntPtr p = Marshal.AllocHGlobal(Math.Max(1, b.Length));
            blocks.Add(p);
            if (b.Length > 0) Marshal.Copy(b, 0, p, b.Length);
            return p;
        }

        public IntPtr Text(string s)
        {
            IntPtr p = Marshal.StringToHGlobalUni(s);
            blocks.Add(p);
            return p;
        }

        public IntPtr Structs<T>(List<T> items) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            IntPtr p = Marshal.AllocHGlobal(Math.Max(1, size * items.Count));
            blocks.Add(p);
            for (int i = 0; i < items.Count; i++) Marshal.StructureToPtr(items[i], new IntPtr(p.ToInt64() + (long)i * size), false);
            return p;
        }

        public void Dispose()
        {
            foreach (IntPtr p in blocks) Marshal.FreeHGlobal(p);
            blocks.Clear();
        }
    }

    static void Ready()
    {
        uint v = 0;
        try { v = WebAuthNGetApiVersionNumber(); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        if (v < 1) throw new PlatformNotSupportedException("this Windows can't use security keys from programs. Windows 10 version 1903 or newer is needed");
    }

    static IntPtr Window()
    {
        IntPtr h = GetConsoleWindow();
        if (h != IntPtr.Zero && IsWindowVisible(h)) return h;
        h = GetForegroundWindow();
        if (h != IntPtr.Zero && IsWindowVisible(h)) return h;
        return GetDesktopWindow();
    }

    static void Check(int hr)
    {
        if (hr == 0) return;
        uint u = unchecked((uint)hr);
        if (u == 0x80090036 || u == 0x800704C7 || u == 0x80004004) throw new OperationCanceledException("the security key prompt was cancelled");
        if (u == 0x800705B4) throw new TimeoutException("the security key was not touched in time");
        string name = "";
        try { IntPtr p = WebAuthNGetErrorName(hr); if (p != IntPtr.Zero) name = Marshal.PtrToStringUni(p) ?? ""; }
        catch { }
        throw new InvalidOperationException("Windows could not use the security key (" + (name.Length > 0 ? name + ", " : "") + "0x" + u.ToString("X8", CultureInfo.InvariantCulture) + ")");
    }

    static byte[] Copy(IntPtr p, uint n)
    {
        if (p == IntPtr.Zero || n == 0 || n > 65536) return new byte[0];
        byte[] b = new byte[n];
        Marshal.Copy(p, b, 0, (int)n);
        return b;
    }

    static ClientData Client(Native n, string json)
    {
        byte[] b = Encoding.UTF8.GetBytes(json);
        ClientData cd = new ClientData();
        cd.Version = 1;
        cd.Length = (uint)b.Length;
        cd.Json = n.Bytes(b);
        cd.HashAlgorithm = n.Text("SHA-256");
        return cd;
    }

    public static void MakeCredential(string rpId, string rpName, byte[] userId, string userName, string clientDataJson, out byte[] authData, out byte[] credentialId)
    {
        Ready();
        using (Native n = new Native())
        {
            RpInfo rp = new RpInfo();
            rp.Version = 1;
            rp.Id = n.Text(rpId);
            rp.Name = n.Text(rpName);
            UserInfo user = new UserInfo();
            user.Version = 1;
            user.IdLength = (uint)userId.Length;
            user.Id = n.Bytes(userId);
            user.Name = n.Text(userName);
            user.DisplayName = n.Text(userName);
            CoseParameter es256 = new CoseParameter();
            es256.Version = 1;
            es256.Type = n.Text("public-key");
            es256.Algorithm = Es256;
            Items cose = new Items();
            cose.Count = 1;
            cose.First = n.Structs(new List<CoseParameter>(new CoseParameter[] { es256 }));
            ClientData cd = Client(n, clientDataJson);
            MakeOptions o = new MakeOptions();
            o.Version = 1;
            o.TimeoutMs = Timeout;
            o.Attachment = CrossPlatform;
            o.UserVerification = Discouraged;
            o.Attestation = NoAttestation;
            IntPtr result;
            Check(WebAuthNAuthenticatorMakeCredential(Window(), ref rp, ref user, ref cose, ref cd, ref o, out result));
            try
            {
                Attestation a = (Attestation)Marshal.PtrToStructure(result, typeof(Attestation));
                authData = Copy(a.AuthData, a.AuthDataLength);
                credentialId = Copy(a.CredentialId, a.CredentialIdLength);
            }
            finally { WebAuthNFreeCredentialAttestation(result); }
        }
    }

    public static void GetAssertion(string rpId, string clientDataJson, List<byte[]> allow, out byte[] authData, out byte[] signature, out byte[] credentialId)
    {
        Ready();
        using (Native n = new Native())
        {
            IntPtr type = n.Text("public-key");
            List<Credential> creds = new List<Credential>();
            foreach (byte[] id in allow)
            {
                Credential c = new Credential();
                c.Version = 1;
                c.IdLength = (uint)id.Length;
                c.Id = n.Bytes(id);
                c.Type = type;
                creds.Add(c);
            }
            ClientData cd = Client(n, clientDataJson);
            GetOptions o = new GetOptions();
            o.Version = 1;
            o.TimeoutMs = Timeout;
            o.Allow.Count = (uint)creds.Count;
            o.Allow.First = n.Structs(creds);
            o.Attachment = CrossPlatform;
            o.UserVerification = Discouraged;
            IntPtr result;
            Check(WebAuthNAuthenticatorGetAssertion(Window(), rpId, ref cd, ref o, out result));
            try
            {
                Assertion a = (Assertion)Marshal.PtrToStructure(result, typeof(Assertion));
                authData = Copy(a.AuthData, a.AuthDataLength);
                signature = Copy(a.Signature, a.SignatureLength);
                credentialId = Copy(a.Credential.Id, a.Credential.IdLength);
            }
            finally { WebAuthNFreeAssertion(result); }
        }
    }
}
