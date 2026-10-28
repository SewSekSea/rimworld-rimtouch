// Bounded, explicitly opted-in private diagnostics. GPL-3.0-or-later.
using System;
using System.IO;
using System.Text.Json;
using UnityEngine;
using Verse;

namespace RimTouch.TouchPlatform
{
    public static class TouchInputProof
    {
        private static readonly bool Enabled = Array.Exists(Environment.GetCommandLineArgs(), a => a == "-rimtouch-input-proof");
        private static int records, lastFrame = -1000, lastCount = -1;
        private static string lastStatus;
        private static long playCalls, entryCalls, lastHeartbeat;
        public static void RecordTwist(float degrees)
        {
            if (!Enabled || records >= 1024) return;
            Append(JsonSerializer.Serialize(new { kind = "worldTwist", utc = DateTime.UtcNow, frame = Time.frameCount, clockwiseDegrees = degrees }));
        }
        public static void MarkUpdate(bool play)
        {
            if (!Enabled) return;
            if (play) playCalls++; else entryCalls++;
        }
        public static void OnGuiHeartbeat()
        {
            if (!Enabled || records >= 1024 || Environment.TickCount64 - lastHeartbeat < 2000) return;
            lastHeartbeat = Environment.TickCount64;
            Append(JsonSerializer.Serialize(new { kind = "heartbeat", utc = DateTime.UtcNow, frame = Time.frameCount, pumpCalls = TouchSource.PumpCalls, playCalls, entryCalls, touchEnabled = RimTouchMod.Settings != null && RimTouchMod.Settings.enableTouchMode, status = TouchSource.Status }));
        }
        public static void Record(ContactFrame frame, ContactTouch[] contacts, string status)
        {
            if (!Enabled || records >= 1024) return;
            bool changed = frame.Canceled || lastCount != contacts.Length || lastStatus != status;
            foreach (ContactTouch c in contacts) if (c.phase != ContactPhase.Stationary) changed = true;
            if (!changed && (contacts.Length == 0 || Time.frameCount - lastFrame < 30)) return;
            var rows = new object[contacts.Length];
            for (int i = 0; i < contacts.Length; i++)
            {
                ContactTouch c = contacts[i];
                rows[i] = new { id = c.fingerId, phase = c.phase.ToString(), x = c.position.x, y = c.position.y, dx = c.deltaPosition.x, dy = c.deltaPosition.y };
            }
            Append(JsonSerializer.Serialize(new { utc = DateTime.UtcNow, frame = Time.frameCount, status, width = Screen.width, height = Screen.height, canceled = frame.Canceled, reason = frame.CancelReason, contacts = rows }));
            lastFrame = Time.frameCount; lastCount = contacts.Length; lastStatus = status;
        }
        private static void Append(string json)
        {
            try
            {
                File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "RimTouch-Input-Proof.jsonl"),
                    json + Environment.NewLine);
                records++;
            }
            catch (Exception ex)
            {
                // Diagnostic I/O must never disable input or flood a low-space disk.
                records = 1024;
                Log.Warning("[RimTouch] Optional input proof stopped: " + ex.GetType().Name);
            }
        }
    }
}
