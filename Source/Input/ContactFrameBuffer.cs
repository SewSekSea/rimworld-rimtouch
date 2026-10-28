// RimTouch continuation, 2026. GPL-3.0-or-later. See LICENSE and PORTING.md.
using System;
using System.Collections.Generic;

namespace RimTouch.TouchPlatform
{
    public enum ContactEventKind { Down, Move, Up, Cancel }
    public enum ContactPhase { Began, Moved, Stationary, Ended }

    public struct ContactSample
    {
        public uint Id;
        // Normalized client coordinates, with the origin at the top left.
        public float X, Y, DeltaX, DeltaY;
        public ContactPhase Phase;
    }

    public sealed class ContactFrame
    {
        public readonly ContactSample[] Contacts;
        public readonly bool Canceled;
        public readonly string CancelReason;
        public ContactFrame(ContactSample[] contacts, bool canceled, string reason)
        {
            Contacts = contacts;
            Canceled = canceled;
            CancelReason = reason;
        }
    }

    // No Unity, native handles, clocks, or game state. The message callback copies
    // values here; the game takes a single immutable snapshot for each frame.
    public sealed class ContactFrameBuffer
    {
        private const int MaxPending = 512;
        private const int MaxContacts = 32;
        private readonly object gate = new object();
        private readonly Queue<Pending> pending = new Queue<Pending>();
        private readonly List<ContactSample> live = new List<ContactSample>();
        private string cancelReason;
        private struct Pending { public uint Id; public ContactEventKind Kind; public float X, Y; }

        public void Enqueue(uint id, ContactEventKind kind, float x, float y)
        {
            lock (gate)
            {
                if (kind == ContactEventKind.Cancel) { Cancel("native contact canceled"); return; }
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
                { Cancel("invalid coordinates"); return; }
                if (kind < ContactEventKind.Down || kind > ContactEventKind.Cancel)
                { Cancel("invalid contact phase"); return; }
                if (kind == ContactEventKind.Down && (x < 0 || x > 1 || y < 0 || y > 1)) return;
                if (pending.Count >= MaxPending) { Cancel("input queue overflow"); return; }
                pending.Enqueue(new Pending { Id = id, Kind = kind, X = x, Y = y });
            }
        }

        public void Cancel(string reason)
        {
            lock (gate)
            {
                pending.Clear();
                live.Clear();
                cancelReason = reason ?? "input reset";
            }
        }

        public ContactFrame Capture()
        {
            lock (gate)
            {
                if (cancelReason != null)
                {
                    string reason = cancelReason;
                    cancelReason = null;
                    return new ContactFrame(new ContactSample[0], true, reason);
                }
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    ContactSample c = live[i];
                    if (c.Phase == ContactPhase.Ended) { live.RemoveAt(i); continue; }
                    c.Phase = ContactPhase.Stationary;
                    c.DeltaX = c.DeltaY = 0;
                    live[i] = c;
                }
                while (pending.Count > 0)
                {
                    Pending e = pending.Peek();
                    int index = live.FindIndex(c => c.Id == e.Id);
                    if (index >= 0 && (live[index].Phase == ContactPhase.Began || live[index].Phase == ContactPhase.Ended))
                    {
                        // Preserve both edges even when a whole tap/drag arrives
                        // between two game frames. Do not turn it into a held finger.
                        if (e.Kind != ContactEventKind.Down || live[index].Phase == ContactPhase.Ended) break;
                    }
                    pending.Dequeue();
                    if (e.Kind == ContactEventKind.Down)
                    {
                        if (index >= 0 || live.Count >= MaxContacts)
                        {
                            Cancel(index >= 0 ? "duplicate active contact" : "contact limit exceeded");
                            return Capture();
                        }
                        live.Add(new ContactSample { Id = e.Id, X = e.X, Y = e.Y, Phase = ContactPhase.Began });
                    }
                    else if (index >= 0)
                    {
                        ContactSample c = live[index];
                        // Accumulate from the previous frame, including a final
                        // release position that can lie outside the game window.
                        c.DeltaX += e.X - c.X;
                        c.DeltaY += e.Y - c.Y;
                        c.X = e.X; c.Y = e.Y;
                        c.Phase = e.Kind == ContactEventKind.Up ? ContactPhase.Ended
                            : (c.DeltaX != 0 || c.DeltaY != 0 ? ContactPhase.Moved : ContactPhase.Stationary);
                        live[index] = c;
                    }
                    // Moves/releases without a preceding down cannot resurrect
                    // contacts that were canceled on focus loss or disable.
                }
                return new ContactFrame(live.ToArray(), false, null);
            }
        }
    }
}
