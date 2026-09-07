using System;
using Tsunagu.Localization;
using Tsunagu.Native;
using Tsunagu.Usb;

namespace Tsunagu.Power
{
    public sealed class ChargeReading
    {
        public bool HasBattery;
        public bool AcConnected;
        public bool Charging;
        public bool Discharging;
        public double Watts;          // 正なら充電、負なら放電
        public double Volts;
        public int Percent = -1;

        /// <summary>判定は文言ではなくキーで持つ。言語を切り替えても作り直せる。</summary>
        public string VerdictKey;
        public object[] VerdictArgs;
        public VerdictLevel Level;

        public string Verdict => Loc.T(VerdictKey, VerdictArgs);
    }

    /// <summary>
    /// バッテリードライバーから充電レート（mW）を直接読む。
    /// 「つないでいるのに充電が進まない」「充電が遅い」ケーブルの切り分けに使う。
    /// </summary>
    public static class BatteryReader
    {
        public static ChargeReading Read()
        {
            var r = new ChargeReading();

            Win32.SYSTEM_POWER_STATUS sps;
            if (Win32.GetSystemPowerStatus(out sps))
            {
                r.AcConnected = sps.ACLineStatus == 1;
                if (sps.BatteryLifePercent <= 100) r.Percent = sps.BatteryLifePercent;
                r.HasBattery = (sps.BatteryFlag & 0x80) == 0;
            }

            ReadRate(r);
            Judge(r);
            return r;
        }

        private static void ReadRate(ChargeReading r)
        {
            foreach (var dev in DeviceInterfaceEnumerator.Enumerate(Win32.GUID_DEVICE_BATTERY))
            {
                using (var h = Win32.CreateFile(dev.DevicePath,
                           Win32.GENERIC_READ | Win32.GENERIC_WRITE,
                           Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE,
                           IntPtr.Zero, Win32.OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) continue;

                    var timeout = BitConverter.GetBytes(0u);
                    var tagBuf = new byte[4];
                    int returned;
                    if (!Win32.DeviceIoControl(h, Win32.IOCTL_BATTERY_QUERY_TAG,
                            timeout, timeout.Length, tagBuf, tagBuf.Length, out returned, IntPtr.Zero))
                        continue;
                    uint tag = BitConverter.ToUInt32(tagBuf, 0);
                    if (tag == 0) continue;

                    // BATTERY_WAIT_STATUS { Tag, Timeout, PowerState, LowCapacity, HighCapacity }
                    var wait = new byte[20];
                    BitConverter.GetBytes(tag).CopyTo(wait, 0);

                    // BATTERY_STATUS { PowerState, Capacity, Voltage, Rate }
                    var statusBuf = new byte[16];
                    if (!Win32.DeviceIoControl(h, Win32.IOCTL_BATTERY_QUERY_STATUS,
                            wait, wait.Length, statusBuf, statusBuf.Length, out returned, IntPtr.Zero))
                        continue;

                    uint powerState = BitConverter.ToUInt32(statusBuf, 0);
                    uint voltage = BitConverter.ToUInt32(statusBuf, 8);
                    int rate = BitConverter.ToInt32(statusBuf, 12);

                    r.HasBattery = true;
                    r.Charging = (powerState & Win32.BATTERY_CHARGING) != 0;
                    r.Discharging = (powerState & Win32.BATTERY_DISCHARGING) != 0;
                    if (voltage != Win32.BATTERY_UNKNOWN_VOLTAGE) r.Volts = voltage / 1000.0;
                    if (rate != Win32.BATTERY_UNKNOWN_RATE) r.Watts = rate / 1000.0;
                    return;
                }
            }
        }

        private static void Judge(ChargeReading r)
        {
            if (!r.HasBattery)
            {
                r.Level = VerdictLevel.None;
                r.VerdictKey = "powNoBattery";
                return;
            }

            if (!r.AcConnected && !r.Charging)
            {
                r.Level = VerdictLevel.None;
                r.VerdictKey = "powNoSource";
                return;
            }

            double w = r.Watts;

            if (r.Discharging && w <= 0)
            {
                r.Level = VerdictLevel.Fault;
                r.VerdictKey = "powDischarging";
                r.VerdictArgs = new object[] { Math.Abs(w).ToString("0.0") };
                return;
            }

            var watts = new object[] { w.ToString("0.0") };

            if (w >= 45)
            {
                r.Level = VerdictLevel.Good;
                r.VerdictKey = "powHigh";
                r.VerdictArgs = watts;
            }
            else if (w >= 27)
            {
                r.Level = VerdictLevel.Good;
                r.VerdictKey = "powGood";
                r.VerdictArgs = watts;
            }
            else if (w >= 15)
            {
                r.Level = VerdictLevel.Caution;
                r.VerdictKey = "powLow";
                r.VerdictArgs = watts;
            }
            else if (w > 0)
            {
                r.Level = VerdictLevel.Fault;
                r.VerdictKey = "powVeryLow";
                r.VerdictArgs = watts;
            }
            else
            {
                r.Level = VerdictLevel.Caution;
                r.VerdictKey = "powUnknown";
            }
        }
    }
}
