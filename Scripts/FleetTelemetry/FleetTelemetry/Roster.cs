using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;

namespace IngameScript
{
    public partial class Program
    {
        // Runtime-only. Wiped on recompile or reload; repopulates from the next broadcasts.
        sealed class Contact
        {
            public readonly Telemetry Data = new Telemetry();
            // Stamped with this script's own clock when the message arrives, so the
            // sender's clock never matters.
            public double LastSeen;

            public double Age(double now) { return Math.Max(0, now - LastSeen); }
            public bool Stale(double now, double staleSeconds) { return Age(now) > staleSeconds; }
        }

        // Keyed case-insensitively so a track target matches the way every other name in
        // configuration does, and so one grid cannot appear twice over a capital letter.
        readonly Dictionary<string, Contact> roster = new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);
        readonly List<Contact> ordered = new List<Contact>();
        readonly List<string> expired = new List<string>();
        readonly Telemetry incoming = new Telemetry();
        IMyBroadcastListener listener;
        string listenerTag = "";
        int received;
        int rejected;

        void EnsureListener()
        {
            if (listener != null && listenerTag == config.Channel) return;
            if (listener != null) IGC.DisableBroadcastListener(listener);
            listener = IGC.RegisterBroadcastListener(config.Channel);
            listenerTag = config.Channel;
        }

        void Receive()
        {
            EnsureListener();
            while (listener.HasPendingMessage)
            {
                MyIGCMessage message = listener.AcceptMessage();
                string data = message.Data as string;
                if (!Telemetry.TryDecode(data, incoming)) { rejected++; continue; }
                // Another PB on this grid, or an echo: this grid's own screens use local data.
                if (string.Equals(incoming.Name, local.Name, StringComparison.OrdinalIgnoreCase)) continue;
                Contact contact;
                if (!roster.TryGetValue(incoming.Name, out contact))
                {
                    contact = new Contact();
                    roster[incoming.Name] = contact;
                }
                Telemetry.TryDecode(data, contact.Data);
                contact.LastSeen = totalSeconds;
                received++;
            }
        }

        void Broadcast()
        {
            IGC.SendBroadcastMessage(config.Channel, local.Encode(encodeBuffer), TransmissionDistance.TransmissionDistanceMax);
        }

        // Drops contacts past drop_seconds and rebuilds the name-sorted list.
        void Prune()
        {
            expired.Clear();
            foreach (KeyValuePair<string, Contact> pair in roster)
                if (pair.Value.Age(totalSeconds) > config.DropSeconds) expired.Add(pair.Key);
            foreach (string name in expired) roster.Remove(name);
            ordered.Clear();
            ordered.AddRange(roster.Values);
            ordered.Sort((a, b) => string.Compare(a.Data.Name, b.Data.Name, StringComparison.OrdinalIgnoreCase));
        }

        Contact Find(string name)
        {
            Contact contact;
            return name != null && roster.TryGetValue(name, out contact) ? contact : null;
        }

        static string AgeText(double seconds)
        {
            if (seconds < 60) return seconds.ToString("0") + "s";
            if (seconds < 3600) return (seconds / 60).ToString("0") + "m";
            return (seconds / 3600).ToString("0.0") + "h";
        }
    }
}
