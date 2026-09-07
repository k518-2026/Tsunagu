using System;
using System.Collections.Generic;
using System.IO;
using Tsunagu.Localization;
using Tsunagu.Native;

namespace Tsunagu.Storage
{
    public sealed class DriveEntry
    {
        public string Root;          // "E:\"
        public string Label;
        public string BusType;
        public bool IsUsb;
        public long FreeBytes;

        public string Display => Loc.T("driveDisplay",
            Root.TrimEnd('\\'),
            string.IsNullOrEmpty(Label) ? Loc.Get("driveNoLabel") : Label,
            BusType,
            (FreeBytes / 1024.0 / 1024 / 1024).ToString("0.0"));
    }

    public static class DriveScanner
    {
        public static List<DriveEntry> List()
        {
            var list = new List<DriveEntry>();
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!d.IsReady) continue;
                if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed) continue;

                string label = null;
                long free = 0;
                try
                {
                    label = d.VolumeLabel;
                    free = d.AvailableFreeSpace;
                }
                catch { }

                int bus = GetBusType(d.Name);
                list.Add(new DriveEntry
                {
                    Root = d.Name,
                    Label = label,
                    BusType = BusTypeName(bus),
                    IsUsb = bus == 0x07,
                    FreeBytes = free
                });
            }

            list.Sort((a, b) => b.IsUsb.CompareTo(a.IsUsb));
            return list;
        }

        /// <summary>STORAGE_DEVICE_DESCRIPTOR.BusType を取得する。取れなければ -1。</summary>
        private static int GetBusType(string root)
        {
            string path = @"\\.\" + root.TrimEnd('\\');
            using (var h = Win32.CreateFile(path, 0, Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE,
                       IntPtr.Zero, Win32.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return -1;

                var query = new byte[12];   // STORAGE_PROPERTY_QUERY（既定値でよい）
                var output = new byte[1024];
                int returned;
                if (!Win32.DeviceIoControl(h, Win32.IOCTL_STORAGE_QUERY_PROPERTY,
                        query, query.Length, output, output.Length, out returned, IntPtr.Zero))
                    return -1;

                if (returned < 32) return -1;
                return output[28];          // BusType のオフセット
            }
        }

        /// <summary>バス名は各国で通じる略称なので訳さない。</summary>
        private static string BusTypeName(int bus)
        {
            switch (bus)
            {
                case 0x01: return "SCSI";
                case 0x02: return "ATAPI";
                case 0x03: return "ATA";
                case 0x04: return "IEEE1394";
                case 0x07: return "USB";
                case 0x08: return "RAID";
                case 0x09: return "iSCSI";
                case 0x0A: return "SAS";
                case 0x0B: return "SATA";
                case 0x0C: return "SD";
                case 0x0D: return "MMC";
                case 0x11: return "NVMe";
                default: return Loc.Get("busOther");
            }
        }
    }
}
