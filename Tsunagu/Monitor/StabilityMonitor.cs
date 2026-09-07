using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Tsunagu.Localization;
using Tsunagu.Usb;

namespace Tsunagu.Monitor
{
    /// <summary>ログ 1 行。表示文言ではなくキーと引数で持つので、言語を切り替えても作り直せる。</summary>
    public sealed class LogEntry
    {
        public string Key;
        public object[] Args;

        public string Text => Loc.T(Key, Args);
        public override string ToString() => Text;
    }

    /// <summary>
    /// USB ツリーのスナップショットを比較して、切断・再接続の回数を数える。
    /// ケーブルの接触不良は「速度」ではなく「途切れ」に出るため、この監視が要になる。
    /// </summary>
    public sealed class StabilityMonitor
    {
        private readonly UsbTreeScanner _scanner = new UsbTreeScanner();
        private Dictionary<string, string> _snapshot = new Dictionary<string, string>();
        private TimeSpan _stoppedElapsed;

        public bool IsRunning { get; private set; }
        public DateTime StartedAt { get; private set; }
        public int DetachCount { get; private set; }
        public int AttachCount { get; private set; }

        public ObservableCollection<LogEntry> Log { get; } = new ObservableCollection<LogEntry>();

        public void Start()
        {
            _snapshot = Snapshot();
            StartedAt = DateTime.Now;
            _stoppedElapsed = TimeSpan.Zero;
            DetachCount = 0;
            AttachCount = 0;
            IsRunning = true;
            Add("logStart", Now(), _snapshot.Count);
        }

        public void Stop()
        {
            _stoppedElapsed = DateTime.Now - StartedAt;
            IsRunning = false;
            Add("logStop", Now(), Format(_stoppedElapsed));
        }

        public TimeSpan Elapsed => IsRunning ? DateTime.Now - StartedAt : _stoppedElapsed;

        /// <summary>デバイス構成の変化通知を受けたときに呼ぶ。</summary>
        public void Refresh()
        {
            if (!IsRunning) return;

            var now = Snapshot();

            foreach (var key in _snapshot.Keys.Where(k => !now.ContainsKey(k)).ToList())
            {
                DetachCount++;
                Add("logDetach", Now(), _snapshot[key]);
            }

            foreach (var key in now.Keys.Where(k => !_snapshot.ContainsKey(k)).ToList())
            {
                AttachCount++;
                Add("logAttach", Now(), now[key]);
            }

            while (Log.Count > 300) Log.RemoveAt(Log.Count - 1);
            _snapshot = now;
        }

        /// <summary>言語を切り替えたときに、既存のログ表示を作り直す。</summary>
        public void RefreshTexts()
        {
            var copy = Log.ToList();
            Log.Clear();
            foreach (var entry in copy) Log.Add(entry);
        }

        public string BuildVerdict(out VerdictLevel level)
        {
            int events = DetachCount + AttachCount;
            var elapsed = Elapsed;
            string time = Format(elapsed);

            if (!IsRunning && events == 0 && elapsed == TimeSpan.Zero)
            {
                level = VerdictLevel.None;
                return Loc.Get("stabIdle");
            }

            if (IsRunning && events == 0 && elapsed < TimeSpan.FromSeconds(20))
            {
                level = VerdictLevel.None;
                return Loc.T("stabPrompt", time);
            }

            if (events == 0)
            {
                level = VerdictLevel.Good;
                return Loc.T("stabNoEvents", time);
            }

            if (DetachCount <= 1)
            {
                level = VerdictLevel.Caution;
                return Loc.T("stabFew", time, DetachCount, AttachCount);
            }

            level = VerdictLevel.Fault;
            return Loc.T("stabMany", time, DetachCount, AttachCount);
        }

        public string StatusLine() =>
            IsRunning ? Loc.T("stabStatus", Format(Elapsed), DetachCount, AttachCount) : string.Empty;

        // ------------------------------------------------------------------

        private void Add(string key, params object[] args) =>
            Log.Insert(0, new LogEntry { Key = key, Args = args });

        private static string Now() => DateTime.Now.ToString("HH:mm:ss");

        public static string Format(TimeSpan span) =>
            $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";

        private Dictionary<string, string> Snapshot()
        {
            var map = new Dictionary<string, string>();
            foreach (var root in _scanner.Scan())
            {
                foreach (var node in root.Flatten())
                {
                    if (node.Kind != NodeKind.Device && node.Kind != NodeKind.Hub) continue;
                    if (string.IsNullOrEmpty(node.Key)) continue;
                    map[node.Key] = node.Title;
                }
            }
            return map;
        }
    }
}
