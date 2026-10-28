// Windows-first continuation. GPL-3.0-or-later. See PORTING.md for API sources.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace RimTouch.TouchPlatform
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr SubclassCallback(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowsTouchPacket
    {
        public int X, Y;
        public IntPtr Source;
        public uint Id, Flags, Mask, Time;
        public UIntPtr ExtraInfo;
        public uint ContactWidth, ContactHeight;
    }

    public interface IWindowsTouchApi
    {
        uint CurrentThreadId { get; }
        uint CurrentProcessId { get; }
        uint WindowThread(IntPtr window, out uint process);
        IEnumerable<IntPtr> ThreadWindows();
        bool IsVisibleTopLevel(IntPtr window);
        string ClassName(IntPtr window);
        bool IsWindow(IntPtr window);
        bool IsFocused(IntPtr window);
        bool IsTouchWindow(IntPtr window, out uint flags);
        bool RegisterTouchWindow(IntPtr window);
        bool UnregisterTouchWindow(IntPtr window);
        bool Install(IntPtr window, SubclassCallback callback);
        bool Remove(IntPtr window, SubclassCallback callback);
        IntPtr Forward(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
        WindowsTouchPacket[] ReadContacts(IntPtr handle, int count);
        bool TryClientPosition(IntPtr window, int physicalX100, int physicalY100, out float x, out float y);
    }

    public sealed class WindowsTouchBackend
    {
        private readonly ContactFrameBuffer frames;
        private readonly IWindowsTouchApi api;
        // Keep the delegate alive for the entire native callback lifetime. The
        // game facade also retains this backend even if native removal fails.
        private readonly SubclassCallback callback;
        private IntPtr window;
        private bool registeredByUs, enabled, wasFocused;
        public string Status { get; private set; }
        public bool Attached { get { return window != IntPtr.Zero && enabled; } }

        public WindowsTouchBackend(ContactFrameBuffer frames, IWindowsTouchApi api)
        {
            this.frames = frames;
            this.api = api;
            callback = OnMessage;
            Status = "Waiting for the game window";
        }

        public static IntPtr FindGameWindow(IWindowsTouchApi api)
        {
            IntPtr found = IntPtr.Zero;
            foreach (IntPtr h in api.ThreadWindows())
            {
                uint process;
                if (api.WindowThread(h, out process) != api.CurrentThreadId || process != api.CurrentProcessId
                    || !api.IsVisibleTopLevel(h) || api.ClassName(h) != "GLFW30") continue;
                if (found != IntPtr.Zero) return IntPtr.Zero;
                found = h;
            }
            return found;
        }

        public bool TryAttach(IntPtr target)
        {
            if (window != IntPtr.Zero) return target == window && enabled;
            uint process;
            if (target == IntPtr.Zero || api.WindowThread(target, out process) != api.CurrentThreadId || process != api.CurrentProcessId)
            { Status = "No unique game window on the input thread; mouse input remains available"; return false; }
            uint existingFlags;
            bool alreadyRegistered = api.IsTouchWindow(target, out existingFlags);
            if (!api.Install(target, callback)) { Status = "Windows touch callback could not be installed"; return false; }
            window = target;
            if (!alreadyRegistered && !api.RegisterTouchWindow(target))
            {
                bool removed = api.Remove(target, callback);
                if (removed) window = IntPtr.Zero;
                Status = removed ? "Windows touch registration failed" : "Touch registration and callback cleanup failed; restart required";
                return false;
            }
            registeredByUs = !alreadyRegistered;
            enabled = true;
            wasFocused = api.IsFocused(target);
            Status = "Windows touch messages attached (hardware validation pending)";
            return true;
        }

        public void CheckFocus()
        {
            if (!Attached) return;
            if (!api.IsWindow(window))
            { frames.Cancel("game window closed"); window = IntPtr.Zero; enabled = registeredByUs = false; return; }
            bool focused = api.IsFocused(window);
            if (!focused && wasFocused) frames.Cancel("focus lost");
            wasFocused = focused;
        }

        public bool Detach()
        {
            enabled = false;
            frames.Cancel("touch disabled");
            if (window == IntPtr.Zero) { Status = "Touch input disabled"; return true; }
            uint process;
            if (api.WindowThread(window, out process) != api.CurrentThreadId || process != api.CurrentProcessId)
            { Status = "Cannot detach from a different window thread; restart required"; return false; }
            if (registeredByUs)
            {
                if (!api.UnregisterTouchWindow(window)) { Status = "Touch registration cleanup failed; restart required"; return false; }
                registeredByUs = false;
            }
            if (!api.Remove(window, callback)) { Status = "Touch callback cleanup failed; restart required"; return false; }
            window = IntPtr.Zero;
            Status = "Touch input disabled";
            return true;
        }

        public void Suspend()
        {
            enabled = false;
            frames.Cancel("touch backend suspended");
        }

        private IntPtr OnMessage(IntPtr h, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
        {
            // Never let managed exceptions unwind into the native game loop.
            // In all cases, the original message (including its live touch
            // handle) goes on to the next procedure. It owns handle disposal.
            try
            {
                if (message == 0x0082) // WM_NCDESTROY
                {
                    frames.Cancel("game window destroyed");
                    api.Remove(h, callback);
                    window = IntPtr.Zero; enabled = registeredByUs = false;
                }
                else if (enabled)
                {
                    if (message == 0x0008 || message == 0x001F || (message == 0x001C && wParam == UIntPtr.Zero))
                        frames.Cancel("focus or input canceled"); // KILLFOCUS, CANCELMODE, ACTIVATEAPP
                    else if (message == 0x0005 || message == 0x0003 || message == 0x02E0)
                        frames.Cancel("window geometry changed"); // SIZE, MOVE, DPICHANGED
                    else if (message == 0x0240 && api.IsFocused(h)) // WM_TOUCH
                    {
                        int count = (int)(wParam.ToUInt64() & 0xFFFF);
                        if (count < 1 || count > 256) frames.Cancel("invalid native contact count");
                        else
                        {
                            WindowsTouchPacket[] packets = api.ReadContacts(lParam, count);
                            foreach (WindowsTouchPacket p in packets)
                            {
                                if ((p.Flags & (0x0040 | 0x0080)) != 0)
                                {
                                    // A finger can be reclassified as a palm. Ignoring
                                    // its final packet would leave that old contact held.
                                    frames.Cancel("pen or palm contact");
                                    break;
                                }
                                float x, y;
                                if (!api.TryClientPosition(h, p.X, p.Y, out x, out y))
                                { frames.Cancel("client coordinates unavailable"); break; }
                                ContactEventKind kind;
                                if ((p.Flags & 0x0002) != 0) kind = ContactEventKind.Down;
                                else if ((p.Flags & 0x0004) != 0) kind = ContactEventKind.Up;
                                else if ((p.Flags & 0x0001) != 0) kind = ContactEventKind.Move;
                                else continue;
                                frames.Enqueue(p.Id, kind, x, y);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                frames.Cancel("native input failure");
                enabled = false;
                Status = "Windows touch stopped: " + ex.GetType().Name + "; mouse input remains available";
            }
            return api.Forward(h, message, wParam, lParam);
        }
    }

    public sealed class WindowsTouchApi : IWindowsTouchApi
    {
        private static readonly UIntPtr SubclassId = new UIntPtr(0x52494D54);
        public uint CurrentThreadId { get { return GetCurrentThreadId(); } }
        public uint CurrentProcessId { get { return GetCurrentProcessId(); } }
        public uint WindowThread(IntPtr h, out uint process) { return GetWindowThreadProcessId(h, out process); }
        public IEnumerable<IntPtr> ThreadWindows()
        {
            var result = new List<IntPtr>();
            EnumWindowsCallback visit = delegate(IntPtr h, IntPtr data) { result.Add(h); return true; };
            EnumThreadWindows(CurrentThreadId, visit, IntPtr.Zero);
            GC.KeepAlive(visit);
            return result;
        }
        public bool IsVisibleTopLevel(IntPtr h) { return IsWindowVisible(h) && GetParent(h) == IntPtr.Zero; }
        public string ClassName(IntPtr h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
        bool IWindowsTouchApi.IsWindow(IntPtr h) { return IsWindow(h); }
        public bool IsFocused(IntPtr h) { return GetForegroundWindow() == h; }
        bool IWindowsTouchApi.IsTouchWindow(IntPtr h, out uint flags) { return IsTouchWindow(h, out flags); }
        public bool RegisterTouchWindow(IntPtr h) { return RegisterTouchWindow(h, 0); }
        bool IWindowsTouchApi.UnregisterTouchWindow(IntPtr h) { return UnregisterTouchWindow(h); }
        public bool Install(IntPtr h, SubclassCallback proc) { return SetWindowSubclass(h, proc, SubclassId, UIntPtr.Zero); }
        public bool Remove(IntPtr h, SubclassCallback proc) { return RemoveWindowSubclass(h, proc, SubclassId); }
        public IntPtr Forward(IntPtr h, uint m, UIntPtr w, IntPtr l) { return DefSubclassProc(h, m, w, l); }
        public WindowsTouchPacket[] ReadContacts(IntPtr handle, int count)
        {
            var packets = new WindowsTouchPacket[count];
            if (!GetTouchInputInfo(handle, (uint)count, packets, Marshal.SizeOf(typeof(WindowsTouchPacket))))
                throw new InvalidOperationException("GetTouchInputInfo failed");
            return packets;
        }
        public bool TryClientPosition(IntPtr h, int x100, int y100, out float x, out float y)
        {
            x = y = 0;
            var point = new Point { X = (int)Math.Round(x100 / 100.0), Y = (int)Math.Round(y100 / 100.0) };
            Rect rect;
            // TOUCHINPUT uses physical screen pixels; match this window's DPI
            // coordinates before converting to its client and framebuffer units.
            if (!PhysicalToLogicalPointForPerMonitorDPI(h, ref point) || !ScreenToClient(h, ref point)
                || !GetClientRect(h, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return false;
            x = (point.X - rect.Left) / (float)(rect.Right - rect.Left);
            y = (point.Y - rect.Top) / (float)(rect.Bottom - rect.Top);
            return true;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr data);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint process);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumThreadWindows(uint thread, EnumWindowsCallback callback, IntPtr data);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsTouchWindow(IntPtr h, out uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterTouchWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterTouchWindow(IntPtr h);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTouchInputInfo(IntPtr h, uint count, [Out] WindowsTouchPacket[] inputs, int size);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PhysicalToLogicalPointForPerMonitorDPI(IntPtr h, ref Point point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(IntPtr h, ref Point point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr h, out Rect rect);
        [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr h, SubclassCallback callback, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr h, SubclassCallback callback, UIntPtr id);
        [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr h, uint message, UIntPtr wParam, IntPtr lParam);
    }
}
