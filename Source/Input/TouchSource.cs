// 1.7 Windows candidate input facade. GPL-3.0-or-later.
using System;
using UnityEngine;

namespace RimTouch.TouchPlatform
{
    public struct ContactTouch
    {
        public long fingerId;
        public ContactPhase phase;
        public Vector2 position, deltaPosition;
    }

    public static class TouchSource
    {
        private static readonly ContactFrameBuffer Frames = new ContactFrameBuffer();
        private static WindowsTouchBackend backend;
        private static IWindowsTouchApi api;
        private static ContactTouch[] contacts = new ContactTouch[0];
        private static int lastFrame = -1;
        private static float nextAttachAttempt;
        private static bool wasEnabled, failed;
        public static long PumpCalls { get; private set; }
        public static bool Canceled { get; private set; }
        public static string Status { get; private set; } = "Windows touch candidate not initialized";
        public static int Count { get { return contacts.Length; } }
        public static ContactTouch GetTouch(int index) { return contacts[index]; }

        public static void Update(bool enabled)
        {
            PumpCalls++;
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            Canceled = false;
            try
            {
                if (!enabled)
                {
                    if (wasEnabled)
                    {
                        if (backend != null) backend.Detach();
                        Frames.Cancel("touch disabled");
                        failed = false;
                        nextAttachAttempt = 0;
                    }
                    wasEnabled = false;
                    contacts = new ContactTouch[0];
                    Canceled = Frames.Capture().Canceled;
                    Status = backend == null ? "Touch input disabled" : backend.Status;
                    return;
                }
                wasEnabled = true;
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                { contacts = new ContactTouch[0]; Status = "This candidate supports Windows only"; return; }
                if (failed) { contacts = new ContactTouch[0]; return; }
                if (backend == null)
                {
                    api = new WindowsTouchApi();
                    backend = new WindowsTouchBackend(Frames, api);
                }
                if (!backend.Attached && Time.realtimeSinceStartup >= nextAttachAttempt)
                {
                    nextAttachAttempt = Time.realtimeSinceStartup + 2f;
                    backend.TryAttach(WindowsTouchBackend.FindGameWindow(api));
                }
                backend.CheckFocus();
                Status = backend.Status;
                ContactFrame frame = Frames.Capture();
                Canceled = frame.Canceled;
                contacts = ConvertFrame(frame, Screen.width, Screen.height);
                TouchInputProof.Record(frame, contacts, Status);
            }
            catch (Exception ex)
            {
                // Retain the backend/delegate if it owns a native callback. A
                // failed cleanup must never leave Windows pointing at collected code.
                if (backend != null) backend.Suspend();
                Frames.Cancel("touch backend failed");
                Frames.Capture();
                contacts = new ContactTouch[0];
                Canceled = true;
                failed = true;
                Status = "Touch backend unavailable: " + ex.GetType().Name + "; mouse input remains available";
            }
        }

        public static ContactTouch[] ConvertFrame(ContactFrame frame, int width, int height)
        {
            if (frame.Canceled || width <= 0 || height <= 0) return new ContactTouch[0];
            var result = new ContactTouch[frame.Contacts.Length];
            for (int i = 0; i < result.Length; i++)
            {
                ContactSample c = frame.Contacts[i];
                result[i] = new ContactTouch
                {
                    fingerId = c.Id, phase = c.Phase,
                    // Existing gestures expect framebuffer pixels and a bottom-left origin.
                    position = new Vector2(c.X * width, (1f - c.Y) * height),
                    deltaPosition = new Vector2(c.DeltaX * width, -c.DeltaY * height)
                };
            }
            return result;
        }
    }
}
