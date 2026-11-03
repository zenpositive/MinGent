using System;
using System.Runtime.InteropServices;

namespace MinGent
{
    [StructLayout(LayoutKind.Sequential)] struct WvRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct WvPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct WvColor { public byte A, R, G, B; }
    [StructLayout(LayoutKind.Sequential)] struct WvKeyStatus { public uint RepeatCount, ScanCode; public int IsExtendedKey, IsMenuKeyDown, WasKeyDown, IsKeyReleased; }

    [ComImport, Guid("b96d755e-0319-4e92-a296-23436f46a1fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2Environment
    {
        [PreserveSig] int CreateCoreWebView2Controller(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2CreateCoreWebView2ControllerCompletedHandler p1);
        [PreserveSig] int CreateWebResourceResponse(IntPtr p0, int p1, [MarshalAs(UnmanagedType.LPWStr)] string p2, [MarshalAs(UnmanagedType.LPWStr)] string p3, out IntPtr p4);
        [PreserveSig] int BrowserVersionString(out IntPtr p0);
        [PreserveSig] int add_NewBrowserVersionAvailable(IntPtr p0, out long p1);
        [PreserveSig] int remove_NewBrowserVersionAvailable(long p0);
    }

    [ComImport, Guid("c979903e-d4ca-4228-92eb-47ee3fa96eab"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2Controller2
    {
        [PreserveSig] int IsVisible(out int p0);
        [PreserveSig] int SetIsVisible(int p0);
        [PreserveSig] int Bounds(out WvRect p0);
        [PreserveSig] int SetBounds(WvRect p0);
        [PreserveSig] int ZoomFactor(out double p0);
        [PreserveSig] int SetZoomFactor(double p0);
        [PreserveSig] int add_ZoomFactorChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_ZoomFactorChanged(long p0);
        [PreserveSig] int SetBoundsAndZoomFactor(WvRect p0, double p1);
        [PreserveSig] int MoveFocus(int p0);
        [PreserveSig] int add_MoveFocusRequested(IntPtr p0, out long p1);
        [PreserveSig] int remove_MoveFocusRequested(long p0);
        [PreserveSig] int add_GotFocus([MarshalAs(UnmanagedType.Interface)] ICoreWebView2FocusChangedEventHandler p0, out long p1);
        [PreserveSig] int remove_GotFocus(long p0);
        [PreserveSig] int add_LostFocus(IntPtr p0, out long p1);
        [PreserveSig] int remove_LostFocus(long p0);
        [PreserveSig] int add_AcceleratorKeyPressed([MarshalAs(UnmanagedType.Interface)] ICoreWebView2AcceleratorKeyPressedEventHandler p0, out long p1);
        [PreserveSig] int remove_AcceleratorKeyPressed(long p0);
        [PreserveSig] int ParentWindow(out IntPtr p0);
        [PreserveSig] int SetParentWindow(IntPtr p0);
        [PreserveSig] int NotifyParentWindowPositionChanged();
        [PreserveSig] int Close();
        [PreserveSig] int CoreWebView2([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2_4 p0);
        [PreserveSig] int DefaultBackgroundColor(out WvColor p0);
        [PreserveSig] int SetDefaultBackgroundColor(WvColor p0);
    }

    [ComImport, Guid("20d02d59-6df2-42dc-bd06-f98a694b1302"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2_4
    {
        [PreserveSig] int Settings([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2Settings p0);
        [PreserveSig] int Source(out IntPtr p0);
        [PreserveSig] int Navigate([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int NavigateToString([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int add_NavigationStarting([MarshalAs(UnmanagedType.Interface)] ICoreWebView2NavigationStartingEventHandler p0, out long p1);
        [PreserveSig] int remove_NavigationStarting(long p0);
        [PreserveSig] int add_ContentLoading([MarshalAs(UnmanagedType.Interface)] ICoreWebView2ContentLoadingEventHandler p0, out long p1);
        [PreserveSig] int remove_ContentLoading(long p0);
        [PreserveSig] int add_SourceChanged([MarshalAs(UnmanagedType.Interface)] ICoreWebView2SourceChangedEventHandler p0, out long p1);
        [PreserveSig] int remove_SourceChanged(long p0);
        [PreserveSig] int add_HistoryChanged([MarshalAs(UnmanagedType.Interface)] ICoreWebView2HistoryChangedEventHandler p0, out long p1);
        [PreserveSig] int remove_HistoryChanged(long p0);
        [PreserveSig] int add_NavigationCompleted([MarshalAs(UnmanagedType.Interface)] ICoreWebView2NavigationCompletedEventHandler p0, out long p1);
        [PreserveSig] int remove_NavigationCompleted(long p0);
        [PreserveSig] int add_FrameNavigationStarting([MarshalAs(UnmanagedType.Interface)] ICoreWebView2NavigationStartingEventHandler p0, out long p1);
        [PreserveSig] int remove_FrameNavigationStarting(long p0);
        [PreserveSig] int add_FrameNavigationCompleted([MarshalAs(UnmanagedType.Interface)] ICoreWebView2NavigationCompletedEventHandler p0, out long p1);
        [PreserveSig] int remove_FrameNavigationCompleted(long p0);
        [PreserveSig] int add_ScriptDialogOpening([MarshalAs(UnmanagedType.Interface)] ICoreWebView2ScriptDialogOpeningEventHandler p0, out long p1);
        [PreserveSig] int remove_ScriptDialogOpening(long p0);
        [PreserveSig] int add_PermissionRequested([MarshalAs(UnmanagedType.Interface)] ICoreWebView2PermissionRequestedEventHandler p0, out long p1);
        [PreserveSig] int remove_PermissionRequested(long p0);
        [PreserveSig] int add_ProcessFailed([MarshalAs(UnmanagedType.Interface)] ICoreWebView2ProcessFailedEventHandler p0, out long p1);
        [PreserveSig] int remove_ProcessFailed(long p0);
        [PreserveSig] int AddScriptToExecuteOnDocumentCreated([MarshalAs(UnmanagedType.LPWStr)] string p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler p1);
        [PreserveSig] int RemoveScriptToExecuteOnDocumentCreated([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int ExecuteScript([MarshalAs(UnmanagedType.LPWStr)] string p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2ExecuteScriptCompletedHandler p1);
        [PreserveSig] int CapturePreview(int p0, IntPtr p1, IntPtr p2);
        [PreserveSig] int Reload();
        [PreserveSig] int PostWebMessageAsJson([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int PostWebMessageAsString([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int add_WebMessageReceived(IntPtr p0, out long p1);
        [PreserveSig] int remove_WebMessageReceived(long p0);
        [PreserveSig] int CallDevToolsProtocolMethod([MarshalAs(UnmanagedType.LPWStr)] string p0, [MarshalAs(UnmanagedType.LPWStr)] string p1, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2CallDevToolsProtocolMethodCompletedHandler p2);
        [PreserveSig] int BrowserProcessId(out uint p0);
        [PreserveSig] int CanGoBack(out int p0);
        [PreserveSig] int CanGoForward(out int p0);
        [PreserveSig] int GoBack();
        [PreserveSig] int GoForward();
        [PreserveSig] int GetDevToolsProtocolEventReceiver([MarshalAs(UnmanagedType.LPWStr)] string p0, out IntPtr p1);
        [PreserveSig] int Stop();
        [PreserveSig] int add_NewWindowRequested([MarshalAs(UnmanagedType.Interface)] ICoreWebView2NewWindowRequestedEventHandler p0, out long p1);
        [PreserveSig] int remove_NewWindowRequested(long p0);
        [PreserveSig] int add_DocumentTitleChanged([MarshalAs(UnmanagedType.Interface)] ICoreWebView2DocumentTitleChangedEventHandler p0, out long p1);
        [PreserveSig] int remove_DocumentTitleChanged(long p0);
        [PreserveSig] int DocumentTitle(out IntPtr p0);
        [PreserveSig] int AddHostObjectToScript([MarshalAs(UnmanagedType.LPWStr)] string p0, IntPtr p1);
        [PreserveSig] int RemoveHostObjectFromScript([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int OpenDevToolsWindow();
        [PreserveSig] int add_ContainsFullScreenElementChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_ContainsFullScreenElementChanged(long p0);
        [PreserveSig] int ContainsFullScreenElement(out int p0);
        [PreserveSig] int add_WebResourceRequested(IntPtr p0, out long p1);
        [PreserveSig] int remove_WebResourceRequested(long p0);
        [PreserveSig] int AddWebResourceRequestedFilter([MarshalAs(UnmanagedType.LPWStr)] string p0, int p1);
        [PreserveSig] int RemoveWebResourceRequestedFilter([MarshalAs(UnmanagedType.LPWStr)] string p0, int p1);
        [PreserveSig] int add_WindowCloseRequested([MarshalAs(UnmanagedType.Interface)] ICoreWebView2WindowCloseRequestedEventHandler p0, out long p1);
        [PreserveSig] int remove_WindowCloseRequested(long p0);
        [PreserveSig] int add_WebResourceResponseReceived(IntPtr p0, out long p1);
        [PreserveSig] int remove_WebResourceResponseReceived(long p0);
        [PreserveSig] int NavigateWithWebResourceRequest(IntPtr p0);
        [PreserveSig] int add_DOMContentLoaded(IntPtr p0, out long p1);
        [PreserveSig] int remove_DOMContentLoaded(long p0);
        [PreserveSig] int CookieManager(out IntPtr p0);
        [PreserveSig] int Environment([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2Environment p0);
        [PreserveSig] int TrySuspend(IntPtr p0);
        [PreserveSig] int Resume();
        [PreserveSig] int IsSuspended(out int p0);
        [PreserveSig] int SetVirtualHostNameToFolderMapping([MarshalAs(UnmanagedType.LPWStr)] string p0, [MarshalAs(UnmanagedType.LPWStr)] string p1, int p2);
        [PreserveSig] int ClearVirtualHostNameToFolderMapping([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int add_FrameCreated(IntPtr p0, out long p1);
        [PreserveSig] int remove_FrameCreated(long p0);
        [PreserveSig] int add_DownloadStarting([MarshalAs(UnmanagedType.Interface)] ICoreWebView2DownloadStartingEventHandler p0, out long p1);
        [PreserveSig] int remove_DownloadStarting(long p0);
    }

    [ComImport, Guid("e562e4f0-d7fa-43ac-8d71-c05150499f00"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2Settings
    {
        [PreserveSig] int IsScriptEnabled(out int p0);
        [PreserveSig] int SetIsScriptEnabled(int p0);
        [PreserveSig] int IsWebMessageEnabled(out int p0);
        [PreserveSig] int SetIsWebMessageEnabled(int p0);
        [PreserveSig] int AreDefaultScriptDialogsEnabled(out int p0);
        [PreserveSig] int SetAreDefaultScriptDialogsEnabled(int p0);
        [PreserveSig] int IsStatusBarEnabled(out int p0);
        [PreserveSig] int SetIsStatusBarEnabled(int p0);
        [PreserveSig] int AreDevToolsEnabled(out int p0);
        [PreserveSig] int SetAreDevToolsEnabled(int p0);
        [PreserveSig] int AreDefaultContextMenusEnabled(out int p0);
        [PreserveSig] int SetAreDefaultContextMenusEnabled(int p0);
        [PreserveSig] int AreHostObjectsAllowed(out int p0);
        [PreserveSig] int SetAreHostObjectsAllowed(int p0);
        [PreserveSig] int IsZoomControlEnabled(out int p0);
        [PreserveSig] int SetIsZoomControlEnabled(int p0);
        [PreserveSig] int IsBuiltInErrorPageEnabled(out int p0);
        [PreserveSig] int SetIsBuiltInErrorPageEnabled(int p0);
    }

    [ComImport, Guid("5b495469-e119-438a-9b18-7604f25f2e49"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2NavigationStartingEventArgs
    {
        [PreserveSig] int Uri(out IntPtr p0);
        [PreserveSig] int IsUserInitiated(out int p0);
        [PreserveSig] int IsRedirected(out int p0);
        [PreserveSig] int RequestHeaders(out IntPtr p0);
        [PreserveSig] int Cancel(out int p0);
        [PreserveSig] int SetCancel(int p0);
        [PreserveSig] int NavigationId(out ulong p0);
    }

    [ComImport, Guid("30d68b7d-20d9-4752-a9ca-ec8448fbb5c1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2NavigationCompletedEventArgs
    {
        [PreserveSig] int IsSuccess(out int p0);
        [PreserveSig] int WebErrorStatus(out int p0);
        [PreserveSig] int NavigationId(out ulong p0);
    }

    [ComImport, Guid("34acb11c-fc37-4418-9132-f9c21d1eafb9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2NewWindowRequestedEventArgs
    {
        [PreserveSig] int Uri(out IntPtr p0);
        [PreserveSig] int SetNewWindow([MarshalAs(UnmanagedType.Interface)] ICoreWebView2_4 p0);
        [PreserveSig] int NewWindow([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2_4 p0);
        [PreserveSig] int SetHandled(int p0);
        [PreserveSig] int Handled(out int p0);
        [PreserveSig] int IsUserInitiated(out int p0);
        [PreserveSig] int GetDeferral([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2Deferral p0);
        [PreserveSig] int WindowFeatures([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2WindowFeatures p0);
    }

    [ComImport, Guid("973ae2ef-ff18-4894-8fb2-3c758f046810"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2PermissionRequestedEventArgs
    {
        [PreserveSig] int Uri(out IntPtr p0);
        [PreserveSig] int PermissionKind(out int p0);
        [PreserveSig] int IsUserInitiated(out int p0);
        [PreserveSig] int State(out int p0);
        [PreserveSig] int SetState(int p0);
        [PreserveSig] int GetDeferral([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2Deferral p0);
    }

    [ComImport, Guid("e61670bc-3dce-4177-86d2-c629ae3cb6ac"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2PermissionRequestedEventArgs3
    {
        [PreserveSig] int Uri(out IntPtr p0);
        [PreserveSig] int PermissionKind(out int p0);
        [PreserveSig] int IsUserInitiated(out int p0);
        [PreserveSig] int State(out int p0);
        [PreserveSig] int SetState(int p0);
        [PreserveSig] int GetDeferral(out IntPtr p0);
        [PreserveSig] int Handled(out int p0);
        [PreserveSig] int SetHandled(int p0);
        [PreserveSig] int SavesInProfile(out int p0);
        [PreserveSig] int SetSavesInProfile(int p0);
    }

    [ComImport, Guid("e99bbe21-43e9-4544-a732-282764eafa60"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2DownloadStartingEventArgs
    {
        [PreserveSig] int DownloadOperation([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2DownloadOperation p0);
        [PreserveSig] int Cancel(out int p0);
        [PreserveSig] int SetCancel(int p0);
        [PreserveSig] int ResultFilePath(out IntPtr p0);
        [PreserveSig] int SetResultFilePath([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int Handled(out int p0);
        [PreserveSig] int SetHandled(int p0);
        [PreserveSig] int GetDeferral([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2Deferral p0);
    }

    [ComImport, Guid("8155a9a4-1474-4a86-8cae-151b0fa6b8ca"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2ProcessFailedEventArgs
    {
        [PreserveSig] int ProcessFailedKind(out int p0);
    }

    [ComImport, Guid("31e0e545-1dba-4266-8914-f63848a1f7d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2SourceChangedEventArgs
    {
        [PreserveSig] int IsNewDocument(out int p0);
    }

    [ComImport, Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
    {
        [PreserveSig] int Invoke(int p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2Environment p1);
    }

    [ComImport, Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
    {
        [PreserveSig] int Invoke(int p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2Controller2 p1);
    }

    [ComImport, Guid("97055cd4-512c-4264-8b5f-e3f446cea6a5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2WebResourceRequest
    {
        [PreserveSig] int Uri(out IntPtr p0);
    }

    [ComImport, Guid("453e667f-12c7-49d4-be6d-ddbe7956f57a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2WebResourceRequestedEventArgs
    {
        [PreserveSig] int Request([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2WebResourceRequest p0);
        [PreserveSig] int Response(out IntPtr p0);
        [PreserveSig] int SetResponse(IntPtr p0);
    }

    [ComImport, Guid("ab00b74c-15f1-4646-80e8-e76341d25d71"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2WebResourceRequestedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2WebResourceRequestedEventArgs p1);
    }

    [ComImport, Guid("9adbe429-f36d-432b-9ddc-f8881fbd76e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2NavigationStartingEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2NavigationStartingEventArgs p1);
    }

    [ComImport, Guid("d33a35bf-1c49-4f98-93ab-006e0533fe1c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2NavigationCompletedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2NavigationCompletedEventArgs p1);
    }

    [ComImport, Guid("d4c185fe-c81c-4989-97af-2d3fa7ab5651"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2NewWindowRequestedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2NewWindowRequestedEventArgs p1);
    }

    [ComImport, Guid("15e1c6a3-c72a-4df3-91d7-d097fbec6bfd"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2PermissionRequestedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2PermissionRequestedEventArgs p1);
    }

    [ComImport, Guid("efedc989-c396-41ca-83f7-07f845a55724"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2DownloadStartingEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2DownloadStartingEventArgs p1);
    }

    [ComImport, Guid("f5f2b923-953e-4042-9f95-f3a118e1afd4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2DocumentTitleChangedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, IntPtr p1);
    }

    [ComImport, Guid("3c067f9f-5388-4772-8b48-79f7ef1ab37c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2SourceChangedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2SourceChangedEventArgs p1);
    }

    [ComImport, Guid("c79a420c-efd9-4058-9295-3e8b4bcab645"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2HistoryChangedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, IntPtr p1);
    }

    [ComImport, Guid("364471e7-f2be-4910-bdba-d72077d51c4b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2ContentLoadingEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, IntPtr p1);
    }

    [ComImport, Guid("79e0aea4-990b-42d9-aa1d-0fcc2e5bc7f1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2ProcessFailedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2ProcessFailedEventArgs p1);
    }

    [ComImport, Guid("49511172-cc67-4bca-9923-137112f4c4cc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2ExecuteScriptCompletedHandler
    {
        [PreserveSig] int Invoke(int p0, [MarshalAs(UnmanagedType.LPWStr)] string p1);
    }

    [ComImport, Guid("5c4889f0-5ef6-4c5a-952c-d8f1b92d0574"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2CallDevToolsProtocolMethodCompletedHandler
    {
        [PreserveSig] int Invoke(int p0, [MarshalAs(UnmanagedType.LPWStr)] string p1);
    }

    [ComImport, Guid("b32ca51a-8371-45e9-9317-af021d080367"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2DevToolsProtocolEventReceiver
    {
        [PreserveSig] int add_DevToolsProtocolEventReceived([MarshalAs(UnmanagedType.Interface)] ICoreWebView2DevToolsProtocolEventReceivedEventHandler p0, out long p1);
        [PreserveSig] int remove_DevToolsProtocolEventReceived(long p0);
    }

    [ComImport, Guid("e2fda4be-5456-406c-a261-3d452138362c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2DevToolsProtocolEventReceivedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2DevToolsProtocolEventReceivedEventArgs p1);
    }

    [ComImport, Guid("653c2959-bb3a-4377-8632-b58ada4e66c4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2DevToolsProtocolEventReceivedEventArgs
    {
        [PreserveSig] int ParameterObjectAsJson(out IntPtr p0);
    }

    [ComImport, Guid("b99369f3-9b11-47b5-bc6f-8e7895fcea17"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler
    {
        [PreserveSig] int Invoke(int p0, [MarshalAs(UnmanagedType.LPWStr)] string p1);
    }

    [ComImport, Guid("7390bb70-abe0-4843-9529-f143b31b03d6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2ScriptDialogOpeningEventArgs
    {
        [PreserveSig] int Uri(out IntPtr p0);
        [PreserveSig] int Kind(out int p0);
        [PreserveSig] int Message(out IntPtr p0);
        [PreserveSig] int Accept();
        [PreserveSig] int DefaultText(out IntPtr p0);
        [PreserveSig] int ResultText(out IntPtr p0);
        [PreserveSig] int SetResultText([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int GetDeferral([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2Deferral p0);
    }

    [ComImport, Guid("ef381bf9-afa8-4e37-91c4-8ac48524bdfb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2ScriptDialogOpeningEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2ScriptDialogOpeningEventArgs p1);
    }

    [ComImport, Guid("c10e7f7b-b585-46f0-a623-8befbf3e4ee0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2Deferral
    {
        [PreserveSig] int Complete();
    }

    [ComImport, Guid("5eaf559f-b46e-4397-8860-e422f287ff1e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2WindowFeatures
    {
        [PreserveSig] int HasPosition(out int p0);
        [PreserveSig] int HasSize(out int p0);
        [PreserveSig] int Left(out uint p0);
        [PreserveSig] int Top(out uint p0);
        [PreserveSig] int Height(out uint p0);
        [PreserveSig] int Width(out uint p0);
        [PreserveSig] int ShouldDisplayMenuBar(out int p0);
        [PreserveSig] int ShouldDisplayStatus(out int p0);
        [PreserveSig] int ShouldDisplayToolbar(out int p0);
        [PreserveSig] int ShouldDisplayScrollBars(out int p0);
    }

    [ComImport, Guid("5c19e9e0-092f-486b-affa-ca8231913039"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2WindowCloseRequestedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, IntPtr p1);
    }

    [ComImport, Guid("3d6b6cf2-afe1-44c7-a995-c65117714336"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2DownloadOperation
    {
        [PreserveSig] int add_BytesReceivedChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_BytesReceivedChanged(long p0);
        [PreserveSig] int add_EstimatedEndTimeChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_EstimatedEndTimeChanged(long p0);
        [PreserveSig] int add_StateChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_StateChanged(long p0);
        [PreserveSig] int Uri(out IntPtr p0);
        [PreserveSig] int ContentDisposition(out IntPtr p0);
        [PreserveSig] int MimeType(out IntPtr p0);
        [PreserveSig] int TotalBytesToReceive(out long p0);
        [PreserveSig] int BytesReceived(out long p0);
        [PreserveSig] int EstimatedEndTime(out IntPtr p0);
        [PreserveSig] int ResultFilePath(out IntPtr p0);
        [PreserveSig] int State(out int p0);
        [PreserveSig] int InterruptReason(out int p0);
        [PreserveSig] int Cancel();
        [PreserveSig] int Pause();
        [PreserveSig] int Resume();
        [PreserveSig] int CanResume(out int p0);
    }

    [ComImport, Guid("9f760f8a-fb79-42be-9990-7b56900fa9c7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2AcceleratorKeyPressedEventArgs
    {
        [PreserveSig] int KeyEventKind(out int p0);
        [PreserveSig] int VirtualKey(out uint p0);
        [PreserveSig] int KeyEventLParam(out int p0);
        [PreserveSig] int PhysicalKeyStatus(out WvKeyStatus p0);
        [PreserveSig] int Handled(out int p0);
        [PreserveSig] int SetHandled(int p0);
    }

    [ComImport, Guid("b29c7e28-fa79-41a8-8e44-65811c76dcb2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2AcceleratorKeyPressedEventHandler
    {
        [PreserveSig] int Invoke([MarshalAs(UnmanagedType.Interface)] ICoreWebView2Controller2 p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2AcceleratorKeyPressedEventArgs p1);
    }

    [ComImport, Guid("f75f09a8-667e-4983-88d6-c8773f315e84"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2_13
    {
        [PreserveSig] int Settings(out IntPtr p0);
        [PreserveSig] int Source(out IntPtr p0);
        [PreserveSig] int Navigate([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int NavigateToString([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int add_NavigationStarting(IntPtr p0, out long p1);
        [PreserveSig] int remove_NavigationStarting(long p0);
        [PreserveSig] int add_ContentLoading(IntPtr p0, out long p1);
        [PreserveSig] int remove_ContentLoading(long p0);
        [PreserveSig] int add_SourceChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_SourceChanged(long p0);
        [PreserveSig] int add_HistoryChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_HistoryChanged(long p0);
        [PreserveSig] int add_NavigationCompleted(IntPtr p0, out long p1);
        [PreserveSig] int remove_NavigationCompleted(long p0);
        [PreserveSig] int add_FrameNavigationStarting(IntPtr p0, out long p1);
        [PreserveSig] int remove_FrameNavigationStarting(long p0);
        [PreserveSig] int add_FrameNavigationCompleted(IntPtr p0, out long p1);
        [PreserveSig] int remove_FrameNavigationCompleted(long p0);
        [PreserveSig] int add_ScriptDialogOpening(IntPtr p0, out long p1);
        [PreserveSig] int remove_ScriptDialogOpening(long p0);
        [PreserveSig] int add_PermissionRequested(IntPtr p0, out long p1);
        [PreserveSig] int remove_PermissionRequested(long p0);
        [PreserveSig] int add_ProcessFailed(IntPtr p0, out long p1);
        [PreserveSig] int remove_ProcessFailed(long p0);
        [PreserveSig] int AddScriptToExecuteOnDocumentCreated([MarshalAs(UnmanagedType.LPWStr)] string p0, IntPtr p1);
        [PreserveSig] int RemoveScriptToExecuteOnDocumentCreated([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int ExecuteScript([MarshalAs(UnmanagedType.LPWStr)] string p0, IntPtr p1);
        [PreserveSig] int CapturePreview(int p0, IntPtr p1, IntPtr p2);
        [PreserveSig] int Reload();
        [PreserveSig] int PostWebMessageAsJson([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int PostWebMessageAsString([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int add_WebMessageReceived(IntPtr p0, out long p1);
        [PreserveSig] int remove_WebMessageReceived(long p0);
        [PreserveSig] int CallDevToolsProtocolMethod([MarshalAs(UnmanagedType.LPWStr)] string p0, [MarshalAs(UnmanagedType.LPWStr)] string p1, IntPtr p2);
        [PreserveSig] int BrowserProcessId(out uint p0);
        [PreserveSig] int CanGoBack(out int p0);
        [PreserveSig] int CanGoForward(out int p0);
        [PreserveSig] int GoBack();
        [PreserveSig] int GoForward();
        [PreserveSig] int GetDevToolsProtocolEventReceiver([MarshalAs(UnmanagedType.LPWStr)] string p0, out IntPtr p1);
        [PreserveSig] int Stop();
        [PreserveSig] int add_NewWindowRequested(IntPtr p0, out long p1);
        [PreserveSig] int remove_NewWindowRequested(long p0);
        [PreserveSig] int add_DocumentTitleChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_DocumentTitleChanged(long p0);
        [PreserveSig] int DocumentTitle(out IntPtr p0);
        [PreserveSig] int AddHostObjectToScript([MarshalAs(UnmanagedType.LPWStr)] string p0, IntPtr p1);
        [PreserveSig] int RemoveHostObjectFromScript([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int OpenDevToolsWindow();
        [PreserveSig] int add_ContainsFullScreenElementChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_ContainsFullScreenElementChanged(long p0);
        [PreserveSig] int ContainsFullScreenElement(out int p0);
        [PreserveSig] int add_WebResourceRequested(IntPtr p0, out long p1);
        [PreserveSig] int remove_WebResourceRequested(long p0);
        [PreserveSig] int AddWebResourceRequestedFilter([MarshalAs(UnmanagedType.LPWStr)] string p0, int p1);
        [PreserveSig] int RemoveWebResourceRequestedFilter([MarshalAs(UnmanagedType.LPWStr)] string p0, int p1);
        [PreserveSig] int add_WindowCloseRequested(IntPtr p0, out long p1);
        [PreserveSig] int remove_WindowCloseRequested(long p0);
        [PreserveSig] int add_WebResourceResponseReceived(IntPtr p0, out long p1);
        [PreserveSig] int remove_WebResourceResponseReceived(long p0);
        [PreserveSig] int NavigateWithWebResourceRequest(IntPtr p0);
        [PreserveSig] int add_DOMContentLoaded(IntPtr p0, out long p1);
        [PreserveSig] int remove_DOMContentLoaded(long p0);
        [PreserveSig] int CookieManager(out IntPtr p0);
        [PreserveSig] int Environment(out IntPtr p0);
        [PreserveSig] int TrySuspend(IntPtr p0);
        [PreserveSig] int Resume();
        [PreserveSig] int IsSuspended(out int p0);
        [PreserveSig] int SetVirtualHostNameToFolderMapping([MarshalAs(UnmanagedType.LPWStr)] string p0, [MarshalAs(UnmanagedType.LPWStr)] string p1, int p2);
        [PreserveSig] int ClearVirtualHostNameToFolderMapping([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int add_FrameCreated(IntPtr p0, out long p1);
        [PreserveSig] int remove_FrameCreated(long p0);
        [PreserveSig] int add_DownloadStarting(IntPtr p0, out long p1);
        [PreserveSig] int remove_DownloadStarting(long p0);
        [PreserveSig] int add_ClientCertificateRequested(IntPtr p0, out long p1);
        [PreserveSig] int remove_ClientCertificateRequested(long p0);
        [PreserveSig] int OpenTaskManagerWindow();
        [PreserveSig] int PrintToPdf([MarshalAs(UnmanagedType.LPWStr)] string p0, IntPtr p1, IntPtr p2);
        [PreserveSig] int add_IsMutedChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_IsMutedChanged(long p0);
        [PreserveSig] int IsMuted(out int p0);
        [PreserveSig] int SetIsMuted(int p0);
        [PreserveSig] int add_IsDocumentPlayingAudioChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_IsDocumentPlayingAudioChanged(long p0);
        [PreserveSig] int IsDocumentPlayingAudio(out int p0);
        [PreserveSig] int add_IsDefaultDownloadDialogOpenChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_IsDefaultDownloadDialogOpenChanged(long p0);
        [PreserveSig] int IsDefaultDownloadDialogOpen(out int p0);
        [PreserveSig] int OpenDefaultDownloadDialog();
        [PreserveSig] int CloseDefaultDownloadDialog();
        [PreserveSig] int DefaultDownloadDialogCornerAlignment(out int p0);
        [PreserveSig] int SetDefaultDownloadDialogCornerAlignment(int p0);
        [PreserveSig] int DefaultDownloadDialogMargin(out WvPoint p0);
        [PreserveSig] int SetDefaultDownloadDialogMargin(WvPoint p0);
        [PreserveSig] int add_BasicAuthenticationRequested(IntPtr p0, out long p1);
        [PreserveSig] int remove_BasicAuthenticationRequested(long p0);
        [PreserveSig] int CallDevToolsProtocolMethodForSession([MarshalAs(UnmanagedType.LPWStr)] string p0, [MarshalAs(UnmanagedType.LPWStr)] string p1, [MarshalAs(UnmanagedType.LPWStr)] string p2, IntPtr p3);
        [PreserveSig] int add_ContextMenuRequested(IntPtr p0, out long p1);
        [PreserveSig] int remove_ContextMenuRequested(long p0);
        [PreserveSig] int add_StatusBarTextChanged(IntPtr p0, out long p1);
        [PreserveSig] int remove_StatusBarTextChanged(long p0);
        [PreserveSig] int StatusBarText(out IntPtr p0);
        [PreserveSig] int Profile([MarshalAs(UnmanagedType.Interface)] out ICoreWebView2Profile p0);
    }

    [ComImport, Guid("79110ad3-cd5d-4373-8bc3-c60658f17a5f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2Profile
    {
        [PreserveSig] int ProfileName(out IntPtr p0);
        [PreserveSig] int IsInPrivateModeEnabled(out int p0);
        [PreserveSig] int ProfilePath(out IntPtr p0);
        [PreserveSig] int DefaultDownloadFolderPath(out IntPtr p0);
        [PreserveSig] int SetDefaultDownloadFolderPath([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int PreferredColorScheme(out int p0);
        [PreserveSig] int SetPreferredColorScheme(int p0);
    }

    [ComImport, Guid("fa740d4b-5eae-4344-a8ad-74be31925397"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2Profile2
    {
        [PreserveSig] int ProfileName(out IntPtr p0);
        [PreserveSig] int IsInPrivateModeEnabled(out int p0);
        [PreserveSig] int ProfilePath(out IntPtr p0);
        [PreserveSig] int DefaultDownloadFolderPath(out IntPtr p0);
        [PreserveSig] int SetDefaultDownloadFolderPath([MarshalAs(UnmanagedType.LPWStr)] string p0);
        [PreserveSig] int PreferredColorScheme(out int p0);
        [PreserveSig] int SetPreferredColorScheme(int p0);
        [PreserveSig] int ClearBrowsingData(int p0, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2ClearBrowsingDataCompletedHandler p1);
        [PreserveSig] int ClearBrowsingDataInTimeRange(int p0, double p1, double p2, [MarshalAs(UnmanagedType.Interface)] ICoreWebView2ClearBrowsingDataCompletedHandler p3);
        [PreserveSig] int ClearBrowsingDataAll([MarshalAs(UnmanagedType.Interface)] ICoreWebView2ClearBrowsingDataCompletedHandler p0);
    }

    [ComImport, Guid("e9710a06-1d1d-49b2-8234-226f35846ae5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2ClearBrowsingDataCompletedHandler
    {
        [PreserveSig] int Invoke(int p0);
    }

    [ComImport, Guid("05ea24bd-6452-4926-9014-4b82b498135d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICoreWebView2FocusChangedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr p0, IntPtr p1);
    }
}
