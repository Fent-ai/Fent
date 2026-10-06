using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ClaudeSidecar
{
    /// <summary>Holds the latest value of every watched channel and records sessions to CSV.</summary>
    public sealed class LiveCapture : IDisposable
    {
        struct Sample { public long Ticks; public int Id; public int Value; }

        public readonly ConcurrentDictionary<int, int> Latest = new ConcurrentDictionary<int, int>();
        public readonly ConcurrentDictionary<int, long> Counts = new ConcurrentDictionary<int, long>();
        public readonly ConcurrentDictionary<int, string> Names = new ConcurrentDictionary<int, string>();

        readonly ConcurrentQueue<Sample> queue = new ConcurrentQueue<Sample>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly object writeLock = new object();
        StreamWriter writer;
        Timer flushTimer;
        long startTicks;

        public string CurrentFile { get; private set; }
        public long SamplesWritten { get; private set; }
        public bool Recording { get { return writer != null; } }

        /// <summary>Called from ECU Manager's comms thread for every new value.</summary>
        public void OnValue(int id, int raw)
        {
            Latest[id] = raw;
            Counts.AddOrUpdate(id, 1, (k, v) => v + 1);
            if (writer != null) queue.Enqueue(new Sample { Ticks = clock.ElapsedTicks, Id = id, Value = raw });
        }

        public void Start()
        {
            lock (writeLock)
            {
                if (writer != null) return;
                Sample junk; while (queue.TryDequeue(out junk)) { }
                CurrentFile = Path.Combine(Sidecar.DataFolder, "session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                writer = new StreamWriter(CurrentFile, false, new UTF8Encoding(false));
                writer.WriteLine("# ECU Manager Sidecar capture, started " + DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteLine("# raw_value is the ECU's own integer. Converting to real units is the next build step.");
                writer.WriteLine("time_s,channel_id,channel,raw_value");
                startTicks = clock.ElapsedTicks;
                SamplesWritten = 0;
                flushTimer = new Timer(_ => Flush(), null, 250, 250);
            }
        }

        void Flush()
        {
            lock (writeLock)
            {
                if (writer == null) return;
                Sample s;
                while (queue.TryDequeue(out s))
                {
                    double t = (s.Ticks - startTicks) / (double)Stopwatch.Frequency;
                    string name; Names.TryGetValue(s.Id, out name);
                    writer.Write(t.ToString("0.000", CultureInfo.InvariantCulture));
                    writer.Write(','); writer.Write(s.Id.ToString(CultureInfo.InvariantCulture));
                    writer.Write(','); writer.Write(name ?? "");
                    writer.Write(','); writer.WriteLine(s.Value.ToString(CultureInfo.InvariantCulture));
                    SamplesWritten++;
                }
                writer.Flush();
            }
        }

        public void Stop()
        {
            Timer t = flushTimer; flushTimer = null;
            if (t != null) t.Dispose();
            Flush();
            lock (writeLock)
            {
                if (writer != null) { writer.Dispose(); writer = null; }
            }
        }

        public void Dispose() { Stop(); }
    }
}
