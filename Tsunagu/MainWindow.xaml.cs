using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Tsunagu.Localization;
using Tsunagu.Monitor;
using Tsunagu.Power;
using Tsunagu.Storage;
using Tsunagu.Usb;

namespace Tsunagu
{
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;

        private readonly UsbTreeScanner _scanner = new UsbTreeScanner();
        private readonly StabilityMonitor _monitor = new StabilityMonitor();

        private readonly DispatcherTimer _deviceChangeDebounce = new DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(600) };
        private readonly DispatcherTimer _monitorTick = new DispatcherTimer
        { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _powerTick = new DispatcherTimer
        { Interval = TimeSpan.FromSeconds(2) };

        private List<UsbNode> _lastScan = new List<UsbNode>();
        private BenchmarkResult _lastBench;
        private ChargeReading _lastCharge;
        private CancellationTokenSource _benchCts;
        private bool _switchingLanguage;

        private sealed class SizeOption
        {
            public string Label { get; set; }
            public long Bytes { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();

            MonitorLog.ItemsSource = _monitor.Log;

            SizeBox.DisplayMemberPath = "Label";
            SizeBox.ItemsSource = new[]
            {
                new SizeOption { Label = "64 MB",  Bytes = 64L * 1024 * 1024 },
                new SizeOption { Label = "256 MB", Bytes = 256L * 1024 * 1024 },
                new SizeOption { Label = "1 GB",   Bytes = 1024L * 1024 * 1024 }
            };
            SizeBox.SelectedIndex = 1;

            _switchingLanguage = true;
            LanguageBox.ItemsSource = Loc.Available;
            LanguageBox.SelectedValue = Loc.CurrentCode;
            _switchingLanguage = false;

            Loc.LanguageChanged += (s, e) => OnLanguageChanged();

            _deviceChangeDebounce.Tick += (s, e) =>
            {
                _deviceChangeDebounce.Stop();
                _monitor.Refresh();
                UpdateMonitorUi();
            };

            _monitorTick.Tick += (s, e) => UpdateMonitorUi();
            _powerTick.Tick += (s, e) => ReadPower();

            Loaded += (s, e) =>
            {
                LoadDrives();
                RunScan();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE && _monitor.IsRunning)
            {
                _deviceChangeDebounce.Stop();
                _deviceChangeDebounce.Start();
            }
            return IntPtr.Zero;
        }

        // ================= 言語 =================

        private void Language_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_switchingLanguage) return;
            var code = LanguageBox.SelectedValue as string;
            if (!string.IsNullOrEmpty(code)) Loc.SetLanguage(code);
        }

        /// <summary>
        /// XAML の文言はバインディングで勝手に入れ替わる。
        /// ここで直すのは、コードで組み立てた文字列のほう。
        /// </summary>
        private void OnLanguageChanged()
        {
            FontFamily = new FontFamily(Loc.Get("uiFont"));

            _switchingLanguage = true;
            LanguageBox.SelectedValue = Loc.CurrentCode;
            _switchingLanguage = false;

            LoadDrives();
            _monitor.RefreshTexts();
            UpdateMonitorUi();

            if (_lastScan.Count > 0)
            {
                RunScan();
            }
            else
            {
                HeadlineText.Text = Loc.Get("headStart");
                HeadlineSub.Text = Loc.Get("headStartSub");
                ScanStatus.Text = "";
            }

            if (_lastBench != null)
            {
                BenchVerdict.Text = _lastBench.Verdict;
                BenchStatus.Text = "";
            }

            if (_lastCharge != null) ReadPower();
        }

        // ================= 接続診断 =================

        private void Scan_Click(object sender, RoutedEventArgs e) => RunScan();

        private async void RunScan()
        {
            ScanButton.IsEnabled = false;
            ScanStatus.Text = Loc.Get("scanning");

            bool showEmpty = ShowEmptyPorts.IsChecked == true;
            List<UsbNode> roots = null;
            string error = null;

            await Task.Run(() =>
            {
                try
                {
                    _scanner.ShowEmptyPorts = showEmpty;
                    roots = _scanner.Scan();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            });

            ScanButton.IsEnabled = true;

            if (error != null)
            {
                ScanStatus.Text = Loc.T("scanFailed", error);
                return;
            }

            _lastScan = roots ?? new List<UsbNode>();
            UsbTree.ItemsSource = _lastScan;

            var all = _lastScan.SelectMany(r => r.Flatten())
                .Where(n => n.Kind == NodeKind.Device || n.Kind == NodeKind.Hub).ToList();
            int faults = all.Count(n => n.Level == VerdictLevel.Fault);
            int cautions = all.Count(n => n.Level == VerdictLevel.Caution);

            ScanStatus.Text = Loc.T("scanDone", all.Count, DateTime.Now.ToString("HH:mm:ss"));

            if (all.Count == 0)
                SetHeadline(Loc.Get("headNoDevice"), VerdictLevel.Caution, Loc.Get("headNoDeviceSub"));
            else if (faults > 0)
                SetHeadline(Loc.T("headFaults", faults), VerdictLevel.Fault, Loc.Get("headFaultsSub"));
            else if (cautions > 0)
                SetHeadline(Loc.T("headCautions", cautions), VerdictLevel.Caution, Loc.Get("headCautionsSub"));
            else
                SetHeadline(Loc.Get("headOk"), VerdictLevel.Good, Loc.T("headOkSub", all.Count));
        }

        private void SetHeadline(string text, VerdictLevel level, string sub)
        {
            HeadlineText.Text = text;
            HeadlineText.Foreground = HeadlineBrush(level);
            HeadlineSub.Text = sub;
        }

        /// <summary>濃い帯の上なので、判定色は明るいほうに寄せる。</summary>
        private static Brush HeadlineBrush(VerdictLevel level)
        {
            switch (level)
            {
                case VerdictLevel.Fault: return new SolidColorBrush(Color.FromRgb(0xFF, 0x9C, 0x9C));
                case VerdictLevel.Caution: return new SolidColorBrush(Color.FromRgb(0xFF, 0xC7, 0x7A));
                case VerdictLevel.Good: return new SolidColorBrush(Color.FromRgb(0x8E, 0xE0, 0xC8));
                default: return Brushes.White;
            }
        }

        // ================= 転送速度 =================

        private void RefreshDrives_Click(object sender, RoutedEventArgs e) => LoadDrives();

        private void LoadDrives()
        {
            var selected = (DriveBox.SelectedItem as DriveEntry)?.Root;
            var drives = DriveScanner.List();
            DriveBox.ItemsSource = drives;

            DriveBox.SelectedItem = drives.FirstOrDefault(d => d.Root == selected)
                                    ?? drives.FirstOrDefault(d => d.IsUsb)
                                    ?? drives.FirstOrDefault();
        }

        private async void Bench_Click(object sender, RoutedEventArgs e)
        {
            if (_benchCts != null)
            {
                _benchCts.Cancel();
                return;
            }

            var drive = DriveBox.SelectedItem as DriveEntry;
            var size = SizeBox.SelectedItem as SizeOption;
            if (drive == null || size == null)
            {
                BenchStatus.Text = Loc.Get("benchSelectDrive");
                return;
            }

            if (drive.FreeBytes < size.Bytes * 2)
            {
                BenchStatus.Text = Loc.Get("benchNoSpace");
                return;
            }

            _benchCts = new CancellationTokenSource();
            BenchButton.Content = Loc.Get("btnCancelMeasure");
            WriteValue.Text = "—";
            ReadValue.Text = "—";
            BenchVerdict.Text = "";

            var progress = new Progress<string>(s => BenchStatus.Text = s);

            try
            {
                var result = await TransferBenchmark.RunAsync(drive.Root, size.Bytes, progress, _benchCts.Token);
                _lastBench = result;

                WriteValue.Text = result.WriteMBps.ToString("0") + " MB/s";
                ReadValue.Text = result.ReadMBps.ToString("0") + " MB/s";
                BenchStatus.Text = Loc.T("benchDone", drive.Root.TrimEnd('\\'),
                    result.Bytes / 1024 / 1024, DateTime.Now.ToString("HH:mm:ss"));
                BenchVerdict.Text = result.Verdict;
                BenchVerdict.Foreground = BrushFor(result.Level);

                SetHeadline(Math.Max(result.WriteMBps, result.ReadMBps).ToString("0") + " MB/s",
                    result.Level, result.Verdict);
            }
            catch (OperationCanceledException)
            {
                BenchStatus.Text = Loc.Get("benchCancelled");
            }
            catch (Exception ex)
            {
                BenchStatus.Text = ex.Message;
            }
            finally
            {
                _benchCts.Dispose();
                _benchCts = null;
                BenchButton.Content = Loc.Get("btnMeasure");
            }
        }

        // ================= 接続の安定性 =================

        private void Monitor_Click(object sender, RoutedEventArgs e)
        {
            if (_monitor.IsRunning)
            {
                _monitor.Stop();
                _monitorTick.Stop();
                MonitorButton.Content = Loc.Get("btnMonitorStart");
            }
            else
            {
                _monitor.Start();
                _monitorTick.Start();
                MonitorButton.Content = Loc.Get("btnMonitorStop");
            }
            UpdateMonitorUi();
        }

        private void UpdateMonitorUi()
        {
            VerdictLevel level;
            MonitorVerdict.Text = _monitor.BuildVerdict(out level);
            MonitorVerdict.Foreground = BrushFor(level);
            MonitorStatus.Text = _monitor.IsRunning ? _monitor.StatusLine() : "";

            if (_monitor.IsRunning && level != VerdictLevel.None)
            {
                int events = _monitor.DetachCount + _monitor.AttachCount;
                SetHeadline(events == 0 ? Loc.Get("headStable") : Loc.T("headDetach", _monitor.DetachCount),
                    level, MonitorVerdict.Text);
            }
        }

        // ================= 給電 =================

        private void Power_Click(object sender, RoutedEventArgs e)
        {
            ReadPower();
            if (AutoRefreshPower.IsChecked == true) _powerTick.Start();
            else _powerTick.Stop();
        }

        private void ReadPower()
        {
            var r = BatteryReader.Read();
            _lastCharge = r;

            WattValue.Text = r.Watts == 0 ? "—" : Math.Abs(r.Watts).ToString("0.0") + " W";
            VoltValue.Text = r.Volts == 0 ? "—" : r.Volts.ToString("0.00") + " V";
            PercentValue.Text = r.Percent < 0 ? "—" : r.Percent + " %";

            PowerVerdict.Text = r.Verdict;
            PowerVerdict.Foreground = BrushFor(r.Level);

            if (r.Level != VerdictLevel.None)
                SetHeadline(Math.Abs(r.Watts).ToString("0.0") + " W", r.Level, r.Verdict);
        }

        // ================= レポート =================

        private void SaveReport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = Loc.Get("saveDialogTitle"),
                Filter = Loc.Get("saveFilter"),
                FileName = Loc.T("saveFileName", DateTime.Now.ToString("yyyyMMdd_HHmm"))
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName, BuildReport(), new UTF8Encoding(true));
                HeadlineSub.Text = Loc.T("reportSaved", dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.T("saveError", ex.Message),
                    Loc.Get("appTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Loc.Get("repTitle"));
            sb.AppendLine(Loc.T("repCreated", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
            sb.AppendLine(Loc.T("repMachine", Environment.MachineName));
            sb.AppendLine();

            sb.AppendLine(Loc.Get("repSecConn"));
            if (_lastScan.Count == 0) sb.AppendLine("  " + Loc.Get("repNotRun"));
            foreach (var root in _lastScan) AppendNode(sb, root, 1);
            sb.AppendLine();

            sb.AppendLine(Loc.Get("repSecSpeed"));
            if (_lastBench == null)
            {
                sb.AppendLine("  " + Loc.Get("repNotRun"));
            }
            else
            {
                sb.AppendLine("  " + Loc.T("repWrite", _lastBench.WriteMBps.ToString("0.0")));
                sb.AppendLine("  " + Loc.T("repRead", _lastBench.ReadMBps.ToString("0.0")));
                sb.AppendLine("  " + Loc.T("repVerdict", _lastBench.Verdict));
            }
            sb.AppendLine();

            sb.AppendLine(Loc.Get("repSecStab"));
            VerdictLevel level;
            sb.AppendLine("  " + _monitor.BuildVerdict(out level));
            foreach (var entry in _monitor.Log.Take(50)) sb.AppendLine("  " + entry.Text);
            sb.AppendLine();

            sb.AppendLine(Loc.Get("repSecPower"));
            if (_lastCharge == null)
            {
                sb.AppendLine("  " + Loc.Get("repNotRun"));
            }
            else
            {
                sb.AppendLine("  " + Loc.T("repPowerLine",
                    _lastCharge.Watts.ToString("0.0"),
                    _lastCharge.Volts.ToString("0.00"),
                    _lastCharge.Percent));
                sb.AppendLine("  " + Loc.T("repVerdict", _lastCharge.Verdict));
            }

            return sb.ToString();
        }

        private static void AppendNode(StringBuilder sb, UsbNode node, int depth)
        {
            string indent = new string(' ', depth * 2);
            sb.AppendLine(indent + node.Title);
            if (!string.IsNullOrEmpty(node.Subtitle)) sb.AppendLine(indent + "  " + node.Subtitle);
            if (!string.IsNullOrEmpty(node.LinkText)) sb.AppendLine(indent + "  " + node.LinkText);
            if (!string.IsNullOrEmpty(node.CapabilityText)) sb.AppendLine(indent + "  " + node.CapabilityText);
            if (!string.IsNullOrEmpty(node.PortText)) sb.AppendLine(indent + "  " + node.PortText);
            if (!string.IsNullOrEmpty(node.IdText)) sb.AppendLine(indent + "  " + node.IdText);
            if (!string.IsNullOrEmpty(node.Verdict)) sb.AppendLine(indent + "  -> " + node.Verdict);
            foreach (var child in node.Children) AppendNode(sb, child, depth + 1);
        }

        // ================= 共通 =================

        private static Brush BrushFor(VerdictLevel level)
        {
            switch (level)
            {
                case VerdictLevel.Good: return Palette.Good;
                case VerdictLevel.Caution: return Palette.Caution;
                case VerdictLevel.Fault: return Palette.Fault;
                default: return Palette.Muted;
            }
        }
    }
}
