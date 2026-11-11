using System;
using System.Runtime.InteropServices;

namespace MinGent
{
    static class QuickEntry
    {
        public const int Message = 0x0312, Id = 0x4D47;
        public static readonly string[] Keys = { "", "ctrl+alt+space", "alt+shift+space", "ctrl+alt+enter" };
        public static readonly string[] Names = { "Off", "Ctrl+Alt+Space", "Alt+Shift+Space", "Ctrl+Alt+Enter" };

        const uint Alt = 1, Control = 2, Shift = 4, NoRepeat = 0x4000;

        static IntPtr owner = IntPtr.Zero;
        static string held = "";

        public static int Index(string key)
        {
            string k = (key ?? "").Trim().ToLowerInvariant();
            for (int i = 1; i < Keys.Length; i++) if (Keys[i] == k) return i;
            return 0;
        }

        public static string Clean(string key) { return Keys[Index(key)]; }

        public static string Name(string key) { return Names[Index(key)]; }

        public static bool Parse(string key, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            string k = Clean(key);
            if (k.Length == 0) return false;
            foreach (string part in k.Split('+'))
            {
                switch (part)
                {
                    case "ctrl": mods |= Control; break;
                    case "alt": mods |= Alt; break;
                    case "shift": mods |= Shift; break;
                    case "space": vk = 0x20; break;
                    case "enter": vk = 0x0D; break;
                    default: return false;
                }
            }
            return vk != 0 && mods != 0;
        }

        public static string Held { get { return held; } }

        public static bool Set(IntPtr window, string key)
        {
            string k = Clean(key);
            if (k == held && (k.Length == 0 || owner == window)) return true;
            Release();
            if (k.Length == 0) return true;
            if (window == IntPtr.Zero) return false;
            uint mods, vk;
            if (!Parse(k, out mods, out vk)) return false;
            bool ok;
            try { ok = RegisterHotKey(window, Id, mods | NoRepeat, vk); } catch { ok = false; }
            if (ok) { owner = window; held = k; }
            return ok;
        }

        public static void Release()
        {
            if (owner != IntPtr.Zero)
            {
                try { UnregisterHotKey(owner, Id); } catch { }
            }
            owner = IntPtr.Zero;
            held = "";
        }

        public static void Lost(IntPtr window)
        {
            if (owner == window) { owner = IntPtr.Zero; held = ""; }
        }

        public static bool Hit(int msg, IntPtr wParam)
        {
            return msg == Message && wParam.ToInt64() == Id;
        }

        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
