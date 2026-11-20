using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Accessibility;

namespace MinGent
{
    [StructLayout(LayoutKind.Sequential)]
    struct UiaRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct UiaPoint { public int X, Y; }

    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaCondition { }

    [ComImport, Guid("b32a92b5-bc25-4078-9c08-d7ee95c48e03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaCacheRequest
    {
        void AddProperty(int propertyId);
        void AddPattern(int patternId);
        IUiaCacheRequest Clone();
        int get_TreeScope();
        void put_TreeScope(int scope);
        IUiaCondition get_TreeFilter();
        void put_TreeFilter(IUiaCondition filter);
        int get_AutomationElementMode();
        void put_AutomationElementMode(int mode);
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaElementArray
    {
        int get_Length();
        IUiaElement GetElement(int index);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaElement
    {
        void SetFocus();
        IntPtr GetRuntimeId();
        IUiaElement FindFirst(int scope, IUiaCondition condition);
        IUiaElementArray FindAll(int scope, IUiaCondition condition);
        IUiaElement FindFirstBuildCache(int scope, IUiaCondition condition, IUiaCacheRequest cacheRequest);
        IUiaElementArray FindAllBuildCache(int scope, IUiaCondition condition, IUiaCacheRequest cacheRequest);
        IUiaElement BuildUpdatedCache(IUiaCacheRequest cacheRequest);
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValueEx(int propertyId, int ignoreDefaultValue);
        [return: MarshalAs(UnmanagedType.Struct)] object GetCachedPropertyValue(int propertyId);
        [return: MarshalAs(UnmanagedType.Struct)] object GetCachedPropertyValueEx(int propertyId, int ignoreDefaultValue);
        IntPtr GetCurrentPatternAs(int patternId, ref Guid riid);
        IntPtr GetCachedPatternAs(int patternId, ref Guid riid);
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetCurrentPattern(int patternId);
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetCachedPattern(int patternId);
        IUiaElement GetCachedParent();
        IUiaElementArray GetCachedChildren();
        int get_CurrentProcessId();
        int get_CurrentControlType();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentLocalizedControlType();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentName();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentAcceleratorKey();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentAccessKey();
        int get_CurrentHasKeyboardFocus();
        int get_CurrentIsKeyboardFocusable();
        int get_CurrentIsEnabled();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentAutomationId();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentClassName();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentHelpText();
        int get_CurrentCulture();
        int get_CurrentIsControlElement();
        int get_CurrentIsContentElement();
        int get_CurrentIsPassword();
        IntPtr get_CurrentNativeWindowHandle();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentItemType();
        int get_CurrentIsOffscreen();
        int get_CurrentOrientation();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentFrameworkId();
        int get_CurrentIsRequiredForForm();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentItemStatus();
        UiaRect get_CurrentBoundingRectangle();
    }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaAutomation
    {
        int CompareElements(IUiaElement a, IUiaElement b);
        int CompareRuntimeIds(IntPtr a, IntPtr b);
        IUiaElement GetRootElement();
        IUiaElement ElementFromHandle(IntPtr hwnd);
        IUiaElement ElementFromPoint(UiaPoint pt);
        IUiaElement GetFocusedElement();
        IUiaElement GetRootElementBuildCache(IUiaCacheRequest cacheRequest);
        IUiaElement ElementFromHandleBuildCache(IntPtr hwnd, IUiaCacheRequest cacheRequest);
        IUiaElement ElementFromPointBuildCache(UiaPoint pt, IUiaCacheRequest cacheRequest);
        IUiaElement GetFocusedElementBuildCache(IUiaCacheRequest cacheRequest);
        [return: MarshalAs(UnmanagedType.IUnknown)] object CreateTreeWalker(IUiaCondition condition);
        [return: MarshalAs(UnmanagedType.IUnknown)] object get_ControlViewWalker();
        [return: MarshalAs(UnmanagedType.IUnknown)] object get_ContentViewWalker();
        [return: MarshalAs(UnmanagedType.IUnknown)] object get_RawViewWalker();
        IUiaCondition get_RawViewCondition();
        IUiaCondition get_ControlViewCondition();
        IUiaCondition get_ContentViewCondition();
        IUiaCacheRequest CreateCacheRequest();
        IUiaCondition CreateTrueCondition();
        IUiaCondition CreateFalseCondition();
        IUiaCondition CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
        IUiaCondition CreatePropertyConditionEx(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, int flags);
        IUiaCondition CreateAndCondition(IUiaCondition a, IUiaCondition b);
    }

    [ComImport, Guid("828055ad-355b-4435-86d5-3b51c14a9b1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaLegacy
    {
        void Select(int flags);
        void DoDefaultAction();
        void SetValue([MarshalAs(UnmanagedType.LPWStr)] string value);
        int get_CurrentChildId();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentName();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentValue();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentDescription();
        int get_CurrentRole();
        int get_CurrentState();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentHelp();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentKeyboardShortcut();
        IUiaElementArray GetCurrentSelection();
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentDefaultAction();
    }

    [ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaInvoke
    {
        void Invoke();
    }

    [ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaSelectionItem
    {
        void Select();
        void AddToSelection();
        void RemoveFromSelection();
        int get_CurrentIsSelected();
    }

    [ComImport, Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaValue
    {
        void SetValue([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentValue();
        int get_CurrentIsReadOnly();
    }

    [ComImport, Guid("c690fdb2-27a8-423c-812d-429773c9084e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaItemContainer
    {
        IUiaElement FindItemByProperty(IUiaElement startAfter, int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
    }

    [ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUiaTreeWalker
    {
        IUiaElement GetParentElement(IUiaElement element);
    }

    class UiaItem
    {
        public IUiaElement Element;
        public string Name = "", AutomationId = "", ClassName = "", Framework = "", Value = "", DefaultAction = "";
        public int ControlType, Role, State;
        public bool Enabled, Offscreen, Focusable, Writable;
        public Rectangle Rect;
        public IntPtr Hwnd;
    }

    static class Uia
    {
        public const int ScopeElement = 1, ScopeChildren = 2, ScopeDescendants = 4, ScopeSubtree = 7;
        public const int PBoundingRectangle = 30001, PControlType = 30003, PName = 30005, PIsKeyboardFocusable = 30009, PIsEnabled = 30010, PAutomationId = 30011,
                         PClassName = 30012, PNativeWindowHandle = 30020, PIsOffscreen = 30022, PFrameworkId = 30024, PValueValue = 30045, PLegacyValue = 30093,
                         PIsValuePatternAvailable = 30043, PValueIsReadOnly = 30046, PLegacyRole = 30095, PLegacyState = 30096, PLegacyDefaultAction = 30100;
        public const int PatInvoke = 10000, PatValue = 10002, PatSelectionItem = 10010, PatLegacy = 10018;

        static IUiaAutomation client;
        static readonly object gate = new object();
        static bool broken;

        public static IUiaAutomation Client
        {
            get
            {
                lock (gate)
                {
                    if (client != null || broken || Environment.OSVersion.Platform != PlatformID.Win32NT) return client;
                    try { client = (IUiaAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"))); }
                    catch { broken = true; }
                    return client;
                }
            }
        }

        public const int PatItemContainer = 10019, PExpandState = 30070;
        static readonly int[] Leaves = { 50000, 50002, 50004, 50005, 50006, 50012, 50013, 50014, 50015, 50020, 50022, 50027, 50038 };
        static readonly int[] Lists = { 50003, 50008, 50023, 50028 };

        public static List<UiaItem> Read(IntPtr hwnd, int max, out bool cut)
        {
            cut = false;
            List<UiaItem> items = new List<UiaItem>();
            IUiaAutomation u = Client;
            if (u == null || hwnd == IntPtr.Zero) return items;
            IUiaElement root = u.ElementFromHandle(hwnd);
            if (root == null) return items;
            IUiaCacheRequest req = u.CreateCacheRequest();
            foreach (int p in new int[] { PName, PControlType, PBoundingRectangle, PAutomationId, PClassName, PIsEnabled, PIsOffscreen, PFrameworkId,
                                          PNativeWindowHandle, PIsKeyboardFocusable, PLegacyRole, PLegacyState, PLegacyValue, PLegacyDefaultAction, PValueValue,
                                          PIsValuePatternAvailable, PValueIsReadOnly, PExpandState })
                req.AddProperty(p);
            IUiaCondition view = u.get_ControlViewCondition();
            req.put_TreeFilter(view);
            Walk(root, 0, true, req, view, items, max, ref cut);
            return items;
        }

        static void Walk(IUiaElement parent, int depth, bool isRoot, IUiaCacheRequest req, IUiaCondition view, List<UiaItem> items, int max, ref bool cut)
        {
            if (cut || depth > 40) return;
            foreach (IUiaElement e in Kids(parent, isRoot, req, view))
            {
                if (items.Count >= max) { cut = true; return; }
                UiaItem it = Item(e);
                items.Add(it);
                if (Array.IndexOf(Leaves, it.ControlType) < 0) Walk(e, depth + 1, false, req, view, items, max, ref cut);
                if (cut) return;
            }
        }

        static List<IUiaElement> Kids(IUiaElement parent, bool isRoot, IUiaCacheRequest req, IUiaCondition view)
        {
            List<IUiaElement> l = new List<IUiaElement>();
            int type = isRoot ? 0 : Int(parent, PControlType);
            if (Array.IndexOf(Lists, type) >= 0)
            {
                if (type == 50003 && Int(parent, PExpandState) != 1) return l;
                IUiaItemContainer ic = null;
                try { ic = parent.GetCurrentPattern(PatItemContainer) as IUiaItemContainer; } catch { }
                if (ic != null)
                {
                    IUiaElement prev = null;
                    int shown = 0, hiddenAfter = 0;
                    Rectangle last = Rectangle.Empty;
                    string lastName = null;
                    for (int i = 0; i < 300; i++)
                    {
                        IUiaElement next = ic.FindItemByProperty(prev, 0, null);
                        if (next == null) break;
                        prev = next;
                        IUiaElement c = next.BuildUpdatedCache(req);
                        Rectangle at = c == null ? Rectangle.Empty : RectOf(Cached(c, PBoundingRectangle));
                        if (at.IsEmpty)
                        {
                            if (shown > 0 && ++hiddenAfter >= 3) break;
                            continue;
                        }
                        string name = Str(c, PName);
                        if (at == last && name == lastName) break;
                        last = at;
                        lastName = name;
                        hiddenAfter = 0;
                        shown++;
                        l.Add(c);
                    }
                    return l;
                }
                if (type == 50003) return l;
            }
            IUiaElementArray all = parent.FindAllBuildCache(ScopeChildren, view, req);
            int n = all == null ? 0 : all.get_Length();
            for (int i = 0; i < n; i++)
            {
                IUiaElement e = all.GetElement(i);
                if (e != null) l.Add(e);
            }
            return l;
        }

        static UiaItem Item(IUiaElement e)
        {
            UiaItem it = new UiaItem();
            it.Element = e;
            it.Name = Str(e, PName);
            it.ControlType = Int(e, PControlType);
            it.AutomationId = Str(e, PAutomationId);
            it.ClassName = Str(e, PClassName);
            it.Framework = Str(e, PFrameworkId);
            it.Enabled = Int(e, PIsEnabled) != 0;
            it.Offscreen = Int(e, PIsOffscreen) != 0;
            it.Focusable = Int(e, PIsKeyboardFocusable) != 0;
            it.Role = Int(e, PLegacyRole);
            if (it.Role == 0) it.Role = RoleOf(it.ControlType);
            it.State = Int(e, PLegacyState);
            it.Value = Str(e, PValueValue);
            it.Writable = Int(e, PIsValuePatternAvailable) != 0 && Int(e, PValueIsReadOnly) == 0;
            if (it.Value.Length == 0) it.Value = Str(e, PLegacyValue);
            it.DefaultAction = Str(e, PLegacyDefaultAction);
            object h = Cached(e, PNativeWindowHandle);
            it.Hwnd = h is int ? new IntPtr((int)h) : h is long ? new IntPtr((long)h) : IntPtr.Zero;
            it.Rect = RectOf(Cached(e, PBoundingRectangle));
            return it;
        }

        static readonly string[] Dismissers = { "Flyout", "MenuFlyout", "FlyoutPresenter", "MenuFlyoutPresenter", "CommandBarFlyoutCommandBar", "ComboBoxItem" };

        public static bool LightDismissFocus(IntPtr hwnd, Rectangle rect)
        {
            IUiaAutomation u = Client;
            if (u == null || rect.Width <= 0 || rect.Height <= 0) return false;
            Rectangle room = Rectangle.Inflate(rect, 2, 2);
            IUiaElement e = u.GetFocusedElement();
            if (e == null || !room.Contains(RectOf(e.get_CurrentBoundingRectangle()))) return false;
            IUiaTreeWalker up = u.get_RawViewWalker() as IUiaTreeWalker;
            for (int i = 0; i < 30 && e != null; i++)
            {
                if (Array.IndexOf(Dismissers, e.get_CurrentClassName()) >= 0) return true;
                if (up == null) return false;
                e = up.GetParentElement(e);
            }
            return false;
        }

        static Rectangle RectOf(UiaRect r)
        {
            return r.Right > r.Left && r.Bottom > r.Top ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;
        }

        public static int RoleOf(int controlType)
        {
            switch (controlType)
            {
                case 50000: return 0x2B;
                case 50002: return 0x2C;
                case 50003: return 0x2E;
                case 50004: return 0x2A;
                case 50005: return 0x1E;
                case 50006: return 0x28;
                case 50007: return 0x22;
                case 50008: return 0x21;
                case 50009: return 0x0B;
                case 50010: return 0x02;
                case 50011: return 0x0C;
                case 50012: return 0x30;
                case 50013: return 0x2D;
                case 50014: return 0x03;
                case 50015: return 0x33;
                case 50016: return 0x34;
                case 50017: return 0x17;
                case 50018: return 0x3C;
                case 50019: return 0x25;
                case 50020: return 0x29;
                case 50021: return 0x16;
                case 50022: return 0x0D;
                case 50023: return 0x23;
                case 50024: return 0x24;
                case 50026: return 0x14;
                case 50028: return 0x18;
                case 50029: return 0x1D;
                case 50030: return 0x0F;
                case 50031: return 0x3E;
                case 50032: return 0x09;
                case 50033: return 0x10;
                case 50034: return 0x19;
                case 50035: return 0x19;
                case 50036: return 0x18;
                case 50037: return 0x01;
                case 50038: return 0x15;
                case 50040: return 0x16;
                default: return 0;
            }
        }

        static object Cached(IUiaElement e, int p)
        {
            try { return e.GetCachedPropertyValue(p); } catch { return null; }
        }

        static string Str(IUiaElement e, int p)
        {
            string s = Cached(e, p) as string;
            return s == null ? "" : s;
        }

        static int Int(IUiaElement e, int p)
        {
            object o = Cached(e, p);
            if (o is int) return (int)o;
            if (o is bool) return (bool)o ? 1 : 0;
            if (o is uint) return unchecked((int)(uint)o);
            return 0;
        }

        public static Rectangle RectOf(object o)
        {
            double[] d = o as double[];
            if (d == null || d.Length < 4 || double.IsInfinity(d[0]) || double.IsNaN(d[0]) || d[2] <= 0 || d[3] <= 0) return Rectangle.Empty;
            return new Rectangle((int)Math.Round(d[0]), (int)Math.Round(d[1]), (int)Math.Round(d[2]), (int)Math.Round(d[3]));
        }
    }

    sealed class UiaAccessible : IAccessible
    {
        readonly IUiaElement el;
        readonly int role;
        readonly string name;

        public UiaAccessible(UiaItem it)
        {
            el = it.Element;
            role = it.Role;
            name = it.Name;
        }

        T Pattern<T>(int id) where T : class
        {
            try { return el.GetCurrentPattern(id) as T; } catch { return null; }
        }

        IUiaLegacy Legacy { get { return Pattern<IUiaLegacy>(Uia.PatLegacy); } }

        void IAccessible.accDoDefaultAction(object child)
        {
            IUiaInvoke inv = Pattern<IUiaInvoke>(Uia.PatInvoke);
            if (inv == null) throw new COMException("no invoke", unchecked((int)0x80020003));
            inv.Invoke();
        }

        object IAccessible.accHitTest(int x, int y) { return null; }

        void IAccessible.accLocation(out int l, out int t, out int w, out int h, object child)
        {
            UiaRect r = el.get_CurrentBoundingRectangle();
            l = r.Left;
            t = r.Top;
            w = Math.Max(0, r.Right - r.Left);
            h = Math.Max(0, r.Bottom - r.Top);
        }

        object IAccessible.accNavigate(int dir, object start) { return null; }

        void IAccessible.accSelect(int flags, object child)
        {
            if ((flags & 1) != 0) el.SetFocus();
            if ((flags & 2) != 0)
            {
                IUiaSelectionItem si = Pattern<IUiaSelectionItem>(Uia.PatSelectionItem);
                if (si != null) si.Select();
                else
                {
                    IUiaLegacy lg = Legacy;
                    if (lg == null) throw new COMException("no selection", unchecked((int)0x80020003));
                    lg.Select(flags & ~1);
                }
            }
        }

        object IAccessible.accParent { get { return null; } }
        int IAccessible.accChildCount { get { return 0; } }
        object IAccessible.accFocus { get { return null; } }
        object IAccessible.accSelection { get { return null; } }
        object IAccessible.get_accChild(object child) { return null; }

        string IAccessible.get_accDefaultAction(object child)
        {
            IUiaLegacy lg = Legacy;
            return lg == null ? "" : lg.get_CurrentDefaultAction() ?? "";
        }

        string IAccessible.get_accDescription(object child) { return ""; }
        string IAccessible.get_accHelp(object child) { return ""; }
        int IAccessible.get_accHelpTopic(out string file, object child) { file = ""; return 0; }
        string IAccessible.get_accKeyboardShortcut(object child) { return ""; }

        string IAccessible.get_accName(object child)
        {
            try { return el.get_CurrentName() ?? ""; } catch { return name; }
        }

        object IAccessible.get_accRole(object child) { return role; }

        object IAccessible.get_accState(object child)
        {
            IUiaLegacy lg = Legacy;
            if (lg != null) return lg.get_CurrentState();
            int s = 0;
            if (el.get_CurrentIsEnabled() == 0) s |= 0x1;
            if (el.get_CurrentIsOffscreen() != 0) s |= 0x10000;
            return s;
        }

        string IAccessible.get_accValue(object child)
        {
            IUiaValue v = Pattern<IUiaValue>(Uia.PatValue);
            if (v != null) return v.get_CurrentValue() ?? "";
            IUiaLegacy lg = Legacy;
            return lg == null ? "" : lg.get_CurrentValue() ?? "";
        }

        void IAccessible.set_accName(object child, string value) { }

        void IAccessible.set_accValue(object child, string value)
        {
            IUiaValue v = Pattern<IUiaValue>(Uia.PatValue);
            if (v != null) { v.SetValue(value); return; }
            IUiaLegacy lg = Legacy;
            if (lg != null) lg.SetValue(value);
        }
    }
}
