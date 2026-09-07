using System;
using System.Collections.Generic;
using System.Linq;

namespace Tsunagu.Usb
{
    /// <summary>SuperSpeedPlus ディスクリプタが持つサブリンク 1 本分の情報。</summary>
    public sealed class SublinkSpeed
    {
        public int Id;
        public double Gbps;              // レーンあたりの速度
        public bool IsTransmit;          // false なら受信側
        public bool IsAsymmetric;
        public bool IsSuperSpeedPlus;    // false なら SuperSpeed (Gen 1) の符号化
    }

    /// <summary>
    /// BOS（Binary Device Object Store）ディスクリプタから読み取った機器の能力。
    /// リンク速度が Gbps 単位で書かれているので、Gen 1 と Gen 2 をここで確実に切り分けられる。
    /// </summary>
    public sealed class DeviceCapabilityInfo
    {
        public bool HasBos;

        // SuperSpeed USB Device Capability (0x03) の wSpeedsSupported
        public bool SupportsLowSpeed;
        public bool SupportsFullSpeed;
        public bool SupportsHighSpeed;
        public bool SupportsGen1;

        // SuperSpeedPlus USB Device Capability (0x0A)
        public bool HasSuperSpeedPlus;
        public List<SublinkSpeed> Sublinks = new List<SublinkSpeed>();
        public int MinRxLanes;
        public int MinTxLanes;

        public bool HasUsb20Extension;   // LPM 対応 (0x02)
        public bool HasBillboard;        // Alternate Mode 情報 (0x0D)
        public bool HasPowerDelivery;    // PD 対応 (0x06)

        /// <summary>機器が出せるレーンあたりの最大速度（Gbps）。不明なら 0。</summary>
        public double MaxLaneGbps =>
            Sublinks.Count > 0 ? Sublinks.Max(s => s.Gbps) : (SupportsGen1 ? 5.0 : 0.0);

        /// <summary>ディスクリプタが明示している最小レーン数（2 以上なら 2 レーン前提の機器）。</summary>
        public int DeclaredLanes => Math.Max(Math.Max(MinRxLanes, MinTxLanes), 1);

        public string SpeedsSupportedText()
        {
            var parts = new List<string>();
            if (SupportsLowSpeed) parts.Add("1.5Mbps");
            if (SupportsFullSpeed) parts.Add("12Mbps");
            if (SupportsHighSpeed) parts.Add("480Mbps");
            if (SupportsGen1) parts.Add("5Gbps");
            foreach (var g in Sublinks.Select(s => s.Gbps).Distinct().OrderBy(g => g))
            {
                string t = GenerationName.FormatSpeed(g);
                if (!parts.Contains(t)) parts.Add(t);
            }
            return parts.Count == 0 ? null : string.Join(Localization.Loc.Get("sepSlash"), parts);
        }
    }

    public static class BosParser
    {
        /// <summary>BOS ディスクリプタ全体（wTotalLength ぶん）を解析する。</summary>
        public static DeviceCapabilityInfo Parse(byte[] bos)
        {
            var info = new DeviceCapabilityInfo();
            if (bos == null || bos.Length < 5) return info;
            if (bos[1] != Native.Win32.USB_BOS_DESCRIPTOR_TYPE) return info;

            info.HasBos = true;
            int total = BitConverter.ToUInt16(bos, 2);
            if (total > bos.Length) total = bos.Length;

            int offset = bos[0];             // bLength（通常 5）
            while (offset + 3 <= total)
            {
                int length = bos[offset];
                if (length < 3 || offset + length > total) break;

                if (bos[offset + 1] == Native.Win32.USB_DEVICE_CAPABILITY_DESCRIPTOR_TYPE)
                {
                    switch (bos[offset + 2])
                    {
                        case 0x02: info.HasUsb20Extension = true; break;
                        case 0x03: ParseSuperSpeed(bos, offset, length, info); break;
                        case 0x06: info.HasPowerDelivery = true; break;
                        case 0x0A: ParseSuperSpeedPlus(bos, offset, length, info); break;
                        case 0x0D: info.HasBillboard = true; break;
                    }
                }

                offset += length;
            }

            return info;
        }

        private static void ParseSuperSpeed(byte[] b, int offset, int length, DeviceCapabilityInfo info)
        {
            if (length < 10) return;
            ushort speeds = BitConverter.ToUInt16(b, offset + 4);   // wSpeedsSupported
            info.SupportsLowSpeed = (speeds & 0x01) != 0;
            info.SupportsFullSpeed = (speeds & 0x02) != 0;
            info.SupportsHighSpeed = (speeds & 0x04) != 0;
            info.SupportsGen1 = (speeds & 0x08) != 0;
        }

        private static void ParseSuperSpeedPlus(byte[] b, int offset, int length, DeviceCapabilityInfo info)
        {
            if (length < 12) return;
            info.HasSuperSpeedPlus = true;

            uint attributes = BitConverter.ToUInt32(b, offset + 4);
            int attrCount = (int)(attributes & 0x1F) + 1;          // SublinkSpeedAttrCount

            ushort functionality = BitConverter.ToUInt16(b, offset + 8);
            info.MinRxLanes = (functionality >> 8) & 0x0F;
            info.MinTxLanes = (functionality >> 12) & 0x0F;

            for (int i = 0; i < attrCount; i++)
            {
                int p = offset + 12 + i * 4;
                if (p + 4 > offset + length) break;

                uint attr = BitConverter.ToUInt32(b, p);
                int id = (int)(attr & 0x0F);
                int exponent = (int)((attr >> 4) & 0x03);          // LSE
                int sublinkType = (int)((attr >> 6) & 0x03);       // ST
                int protocol = (int)((attr >> 14) & 0x03);         // LP
                int mantissa = (int)((attr >> 16) & 0xFFFF);       // LSM

                double gbps;
                switch (exponent)
                {
                    case 0: gbps = mantissa / 1e9; break;   // bit/s
                    case 1: gbps = mantissa / 1e6; break;   // Kbit/s
                    case 2: gbps = mantissa / 1e3; break;   // Mbit/s
                    default: gbps = mantissa; break;        // Gbit/s
                }
                if (gbps <= 0) continue;

                info.Sublinks.Add(new SublinkSpeed
                {
                    Id = id,
                    Gbps = gbps,
                    IsAsymmetric = (sublinkType & 0x01) != 0,
                    IsTransmit = (sublinkType & 0x02) != 0,
                    IsSuperSpeedPlus = protocol == 1
                });
            }
        }
    }
}
