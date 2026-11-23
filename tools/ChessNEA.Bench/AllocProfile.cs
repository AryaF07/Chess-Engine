using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using ChessNEA.Engine;

namespace ChessNEA.Bench
{
    internal sealed class AllocTypeRow
    {
        public string Type { get; set; }
        public int Samples { get; set; }
        public long SampledBytes { get; set; }
        public double Share { get; set; }
    }

    /// <summary>
    /// Sampled allocation profile of one search, using the runtime's own GCAllocationTick events
    /// (EventPipe, no external tools). The runtime raises one event roughly every 100 KB allocated,
    /// naming the type of the object that crossed the threshold, so event counts per type are
    /// proportional to bytes allocated per type. It is a sample, not an exact count.
    /// </summary>
    internal static class AllocProfile
    {
        sealed class Listener : EventListener
        {
            public readonly Dictionary<string, (int count, long bytes)> ByType = new();
            public bool Recording;

            protected override void OnEventSourceCreated(EventSource source)
            {
                if (source.Name == "Microsoft-Windows-DotNETRuntime")
                    EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1); // GC keyword
            }

            protected override void OnEventWritten(EventWrittenEventArgs e)
            {
                if (!Recording || e.EventName == null || !e.EventName.StartsWith("GCAllocationTick")) return;
                int iType = e.PayloadNames.IndexOf("TypeName");
                int iAmount = e.PayloadNames.IndexOf("AllocationAmount64");
                string type = iType >= 0 ? e.Payload[iType] as string ?? "?" : "?";
                long bytes = iAmount >= 0 ? Convert.ToInt64(e.Payload[iAmount]) : 0;
                lock (ByType)
                {
                    ByType.TryGetValue(type, out var v);
                    ByType[type] = (v.count + 1, v.bytes + bytes);
                }
            }
        }

        public static List<AllocTypeRow> Run(string fen, int depth)
        {
            using var listener = new Listener();
            new Bot().Search(Position.FromFen(fen), Math.Min(depth, 2)); // warm-up outside the recording
            System.Threading.Thread.Sleep(500);
            listener.Recording = true;
            new Bot().Search(Position.FromFen(fen), depth);
            System.Threading.Thread.Sleep(1000); // events are delivered asynchronously
            listener.Recording = false;
            long total;
            List<KeyValuePair<string, (int count, long bytes)>> items;
            lock (listener.ByType)
            {
                items = listener.ByType.ToList();
                total = items.Sum(kv => kv.Value.bytes);
            }
            return items.OrderByDescending(kv => kv.Value.bytes)
                        .Select(kv => new AllocTypeRow
                        {
                            Type = kv.Key, Samples = kv.Value.count, SampledBytes = kv.Value.bytes,
                            Share = total == 0 ? 0 : (double)kv.Value.bytes / total,
                        }).ToList();
        }
    }
}
