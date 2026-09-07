using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Tsunagu.Localization;
using Tsunagu.Native;
using Tsunagu.Usb;

namespace Tsunagu.Storage
{
    public sealed class BenchmarkResult
    {
        public double WriteMBps;
        public double ReadMBps;
        public long Bytes;

        /// <summary>判定はキーで持ち、表示のたびに現在の言語で組み立てる。</summary>
        public string VerdictKey;
        public VerdictLevel Level;

        public string Verdict => Loc.Get(VerdictKey);
    }

    /// <summary>
    /// USB ストレージに実データを書いて読み戻し、ケーブル経由の実効速度を測る。
    /// OS のキャッシュに邪魔されないよう FILE_FLAG_NO_BUFFERING を使う。
    /// </summary>
    public static class TransferBenchmark
    {
        private const int Chunk = 4 * 1024 * 1024;   // 4MB（セクタサイズの倍数）

        public static Task<BenchmarkResult> RunAsync(string driveRoot, long totalBytes,
            IProgress<string> progress, CancellationToken token)
        {
            return Task.Run(() => Run(driveRoot, totalBytes, progress, token), token);
        }

        private static BenchmarkResult Run(string driveRoot, long totalBytes,
            IProgress<string> progress, CancellationToken token)
        {
            string file = Path.Combine(driveRoot, "tsunagu_speedtest.tmp");
            IntPtr raw = Marshal.AllocHGlobal(Chunk + 4096);

            try
            {
                // NO_BUFFERING はセクタ境界に合ったバッファを要求する
                long aligned = (raw.ToInt64() + 4095) & ~4095L;
                IntPtr buffer = new IntPtr(aligned);
                FillPattern(buffer, Chunk);

                int chunks = (int)Math.Max(1, totalBytes / Chunk);
                long bytes = (long)chunks * Chunk;

                double writeSeconds = WritePass(file, buffer, chunks, progress, token);
                double readSeconds = ReadPass(file, buffer, chunks, progress, token);

                var result = new BenchmarkResult
                {
                    Bytes = bytes,
                    WriteMBps = bytes / 1024.0 / 1024.0 / Math.Max(writeSeconds, 0.001),
                    ReadMBps = bytes / 1024.0 / 1024.0 / Math.Max(readSeconds, 0.001)
                };
                Judge(result);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(raw);
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }

        private static double WritePass(string file, IntPtr buffer, int chunks,
            IProgress<string> progress, CancellationToken token)
        {
            using (var h = Win32.CreateFile(file, Win32.GENERIC_WRITE, 0, IntPtr.Zero,
                       Win32.CREATE_ALWAYS,
                       Win32.FILE_FLAG_NO_BUFFERING | Win32.FILE_FLAG_WRITE_THROUGH, IntPtr.Zero))
            {
                if (h.IsInvalid) throw new IOException(Loc.Get("benchErrCreate"));

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < chunks; i++)
                {
                    token.ThrowIfCancellationRequested();
                    int written;
                    if (!Win32.WriteFile(h, buffer, Chunk, out written, IntPtr.Zero) || written != Chunk)
                        throw new IOException(Loc.Get("benchErrWrite"));
                    if (i % 8 == 0)
                        progress?.Report(Loc.T("benchProgWrite", (i + 1) * 100 / chunks));
                }
                sw.Stop();
                return sw.Elapsed.TotalSeconds;
            }
        }

        private static double ReadPass(string file, IntPtr buffer, int chunks,
            IProgress<string> progress, CancellationToken token)
        {
            using (var h = Win32.CreateFile(file, Win32.GENERIC_READ, Win32.FILE_SHARE_READ, IntPtr.Zero,
                       Win32.OPEN_EXISTING,
                       Win32.FILE_FLAG_NO_BUFFERING | Win32.FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero))
            {
                if (h.IsInvalid) throw new IOException(Loc.Get("benchErrRead"));

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < chunks; i++)
                {
                    token.ThrowIfCancellationRequested();
                    int read;
                    if (!Win32.ReadFile(h, buffer, Chunk, out read, IntPtr.Zero) || read != Chunk)
                        throw new IOException(Loc.Get("benchErrRead"));
                    if (i % 8 == 0)
                        progress?.Report(Loc.T("benchProgRead", (i + 1) * 100 / chunks));
                }
                sw.Stop();
                return sw.Elapsed.TotalSeconds;
            }
        }

        private static void FillPattern(IntPtr buffer, int size)
        {
            var rnd = new Random(20260905);
            var tmp = new byte[size];
            rnd.NextBytes(tmp);
            Marshal.Copy(tmp, 0, buffer, size);
        }

        private static void Judge(BenchmarkResult r)
        {
            double best = Math.Max(r.WriteMBps, r.ReadMBps);

            if (best >= 900)
            {
                r.Level = VerdictLevel.Good;
                r.VerdictKey = "bench20";
            }
            else if (best >= 350)
            {
                r.Level = VerdictLevel.Good;
                r.VerdictKey = "bench10";
            }
            else if (best >= 130)
            {
                r.Level = VerdictLevel.Good;
                r.VerdictKey = "bench5";
            }
            else if (best >= 25)
            {
                r.Level = VerdictLevel.Caution;
                r.VerdictKey = "benchUsb2";
            }
            else
            {
                r.Level = VerdictLevel.Fault;
                r.VerdictKey = "benchSlow";
            }
        }
    }
}
