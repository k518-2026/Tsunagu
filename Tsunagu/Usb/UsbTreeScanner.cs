using System;
using System.Collections.Generic;
using Microsoft.Win32.SafeHandles;
using Tsunagu.Localization;
using Tsunagu.Native;

namespace Tsunagu.Usb
{
    /// <summary>
    /// USB ホストコントローラーからルートハブ、各ポートへとたどり、
    /// 「機器の能力」「いま出ている速度」「ポートの素性」を突き合わせて
    /// ケーブルとポートのどちらが足を引っぱっているかを推定する。
    /// </summary>
    public sealed class UsbTreeScanner
    {
        // USB_CONNECTION_STATUS
        private const uint NoDeviceConnected = 0;
        private const uint DeviceConnected = 1;
        private const uint DeviceFailedEnumeration = 2;
        private const uint DeviceGeneralFailure = 3;
        private const uint DeviceCausedOvercurrent = 4;
        private const uint DeviceNotEnoughPower = 5;
        private const uint DeviceNotEnoughBandwidth = 6;
        private const uint DeviceHubNestedTooDeeply = 7;
        private const uint DeviceInLegacyHub = 8;
        private const uint DeviceEnumerating = 9;
        private const uint DeviceReset = 10;

        /// <summary>空きポートもツリーに表示するか。</summary>
        public bool ShowEmptyPorts { get; set; }

        /// <summary>ポートごとに集めた事実。判定はこれを材料に組み立てる。</summary>
        private sealed class PortFacts
        {
            public uint Status;
            public byte Speed;                 // USB_DEVICE_SPEED
            public ushort BcdUsb;
            public bool OperatingSuperSpeed;
            public bool OperatingSuperSpeedPlus;
            public bool CapableSuperSpeed;
            public bool CapableSuperSpeedPlus;
            public bool HasV2;
            public bool PortSupportsUsb2;
            public bool PortSupportsUsb3;
            public bool PortIsTypeC;
            public bool PortIsUserConnectable = true;
            public bool PortHasCompanion;
            public bool HasConnectorInfo;
            public int HubType;                // 1=ルート 2=USB2.0 3=USB3.x
            public DeviceCapabilityInfo Caps = new DeviceCapabilityInfo();
        }

        public List<UsbNode> Scan()
        {
            var roots = new List<UsbNode>();
            foreach (var ctrl in DeviceInterfaceEnumerator.Enumerate(Win32.GUID_DEVINTERFACE_USB_HOST_CONTROLLER))
            {
                var node = new UsbNode
                {
                    Kind = NodeKind.Controller,
                    Title = string.IsNullOrEmpty(ctrl.Description) ? Loc.Get("ctrlDefault") : ctrl.Description,
                    Key = ctrl.DevicePath
                };

                using (var h = Win32.CreateFile(ctrl.DevicePath, Win32.GENERIC_WRITE, Win32.FILE_SHARE_WRITE,
                           IntPtr.Zero, Win32.OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid)
                    {
                        node.Subtitle = Loc.Get("ctrlOpenFail");
                        node.Level = VerdictLevel.Caution;
                    }
                    else
                    {
                        string rootHub = GetRootHubName(h);
                        if (rootHub != null)
                            ScanHub(@"\\.\" + rootHub, node, 0);
                        else
                            node.Subtitle = Loc.Get("rootHubFail");
                    }
                }

                roots.Add(node);
            }
            return roots;
        }

        // ==================================================================
        // ハブの走査
        // ==================================================================

        private static string GetRootHubName(SafeFileHandle controller)
        {
            var buf = new byte[1024];
            int returned;
            if (!Win32.DeviceIoControl(controller, Win32.IOCTL_USB_GET_ROOT_HUB_NAME,
                    buf, buf.Length, buf, buf.Length, out returned, IntPtr.Zero))
                return null;
            return Win32.ReadUnicodeZ(buf, 4, returned);   // USB_ROOT_HUB_NAME
        }

        private void ScanHub(string hubPath, UsbNode parent, int depth)
        {
            if (depth > 8) return;

            using (var hub = Win32.CreateFile(hubPath, Win32.GENERIC_WRITE, Win32.FILE_SHARE_WRITE,
                       IntPtr.Zero, Win32.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (hub.IsInvalid) return;

                int ports;
                int hubType = GetHubInformationEx(hub, out ports);

                if (ports <= 0)
                {
                    var info = new byte[0x200];
                    int returned;
                    if (!Win32.DeviceIoControl(hub, Win32.IOCTL_USB_GET_NODE_INFORMATION,
                            info, info.Length, info, info.Length, out returned, IntPtr.Zero))
                        return;
                    ports = info[6];   // USB_HUB_DESCRIPTOR.bNumberOfPorts
                }

                if (ports <= 0 || ports > 64) return;

                if (parent.Kind == NodeKind.Hub && string.IsNullOrEmpty(parent.Subtitle))
                    parent.Subtitle = Loc.T("hubSub", HubTypeText(hubType), ports);

                for (int port = 1; port <= ports; port++)
                {
                    var child = ScanPort(hub, hubPath, port, hubType, depth);
                    if (child != null) parent.Children.Add(child);
                }
            }
        }

        /// <summary>USB_HUB_INFORMATION_EX からハブ種別と最大ポート番号を取る。</summary>
        private static int GetHubInformationEx(SafeFileHandle hub, out int ports)
        {
            ports = 0;
            var buf = new byte[0x100];
            int returned;
            if (!Win32.DeviceIoControl(hub, Win32.IOCTL_USB_GET_HUB_INFORMATION_EX,
                    buf, buf.Length, buf, buf.Length, out returned, IntPtr.Zero))
                return 0;
            if (returned < 6) return 0;

            int hubType = BitConverter.ToInt32(buf, 0);
            ports = BitConverter.ToUInt16(buf, 4);
            return hubType;
        }

        private static string HubTypeText(int hubType)
        {
            switch (hubType)
            {
                case 1: return Loc.Get("hubRoot");
                case 2: return Loc.Get("hub20");
                case 3: return Loc.Get("hub30");
                default: return Loc.Get("hubGeneric");
            }
        }

        // ==================================================================
        // ポート 1 本の走査
        // ==================================================================

        private UsbNode ScanPort(SafeFileHandle hub, string hubPath, int port, int hubType, int depth)
        {
            var buf = new byte[0x400];
            BitConverter.GetBytes(port).CopyTo(buf, 0);

            int returned;
            if (!Win32.DeviceIoControl(hub, Win32.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX,
                    buf, buf.Length, buf, buf.Length, out returned, IntPtr.Zero))
            {
                return ShowEmptyPorts
                    ? new UsbNode { Kind = NodeKind.EmptyPort, Title = Loc.T("portLabel", port), Subtitle = Loc.Get("portStateFail") }
                    : null;
            }

            // USB_NODE_CONNECTION_INFORMATION_EX (pack=1)
            var facts = new PortFacts
            {
                BcdUsb = BitConverter.ToUInt16(buf, 6),
                Speed = buf[23],
                Status = BitConverter.ToUInt32(buf, 31),
                HubType = hubType
            };
            byte deviceClass = buf[8];
            ushort vid = BitConverter.ToUInt16(buf, 12);
            ushort pid = BitConverter.ToUInt16(buf, 14);
            byte iManufacturer = buf[18];
            byte iProduct = buf[19];
            bool isHub = buf[24] != 0;

            ReadConnectorProperties(hub, port, facts);

            if (facts.Status == NoDeviceConnected)
            {
                if (!ShowEmptyPorts) return null;
                return new UsbNode
                {
                    Kind = NodeKind.EmptyPort,
                    Title = Loc.T("portEmptyTitle", port),
                    PortText = BuildPortText(facts)
                };
            }

            var node = new UsbNode
            {
                Kind = isHub ? NodeKind.Hub : NodeKind.Device,
                Key = $"{hubPath}#{port}#{vid:X4}:{pid:X4}"
            };

            // 動作中の速度と、ハブが把握している対応状況
            uint protocols, flags;
            facts.HasV2 = QueryV2(hub, port, out protocols, out flags);
            if (facts.HasV2)
            {
                facts.OperatingSuperSpeed = (flags & 0x1) != 0;
                facts.CapableSuperSpeed = (flags & 0x2) != 0;
                facts.OperatingSuperSpeedPlus = (flags & 0x4) != 0;
                facts.CapableSuperSpeedPlus = (flags & 0x8) != 0;
                facts.PortSupportsUsb2 = (protocols & 0x2) != 0;
                facts.PortSupportsUsb3 = (protocols & 0x4) != 0;
            }

            // 機器自身が申告する能力（BOS ディスクリプタ）
            facts.Caps = ReadBos(hub, port);

            int langId = GetFirstLanguageId(hub, port);
            string product = GetStringDescriptor(hub, port, iProduct, langId);
            string vendor = GetStringDescriptor(hub, port, iManufacturer, langId);
            string name = Join(vendor, product);
            if (string.IsNullOrEmpty(name))
                name = isHub ? Loc.Get("devHub") : DescribeClass(deviceClass);

            node.Title = Loc.T("portTitle", port, name);
            node.LinkText = Loc.T("linkPrefix", BuildLinkText(facts));
            node.CapabilityText = Loc.T("capPrefix", BuildCapabilityText(facts));
            node.PortText = BuildPortText(facts);
            node.IdText = Loc.T("idText", vid.ToString("X4"), pid.ToString("X4"), BcdText(facts.BcdUsb));

            ApplyVerdict(node, facts);

            if (isHub)
            {
                string childHub = GetNodeConnectionName(hub, port);
                if (!string.IsNullOrEmpty(childHub))
                    ScanHub(@"\\.\" + childHub, node, depth + 1);
            }

            return node;
        }

        // ==================================================================
        // 表示テキストの組み立て
        // ==================================================================

        private static double OperatingLaneGbps(PortFacts f)
        {
            if (f.OperatingSuperSpeedPlus) return 10;
            if (f.OperatingSuperSpeed || f.Speed >= 3) return 5;
            switch (f.Speed)
            {
                case 2: return 0.48;
                case 1: return 0.012;
                default: return 0.0015;
            }
        }

        private static string BuildLinkText(PortFacts f)
        {
            // Windows は SuperSpeedPlus までしか報告しないため、
            // 10Gbps か 20Gbps かはここでは断定しない。
            if (f.OperatingSuperSpeedPlus) return Loc.Get("linkSsp");

            var name = UsbGeneration.Describe(OperatingLaneGbps(f), 1);
            return Loc.T("genOperating", name.OneLine);
        }

        private static string BuildCapabilityText(PortFacts f)
        {
            var caps = f.Caps;

            if (!caps.HasBos)
            {
                if (f.CapableSuperSpeedPlus) return Loc.Get("capNoBosSsp");
                if (f.CapableSuperSpeed) return Loc.Get("capNoBosSs");
                if (f.BcdUsb >= 0x0300) return Loc.Get("capNoBosUsb3");
                return Loc.T("capNoBosGeneric", BcdText(f.BcdUsb));
            }

            double lane = caps.MaxLaneGbps;
            if (lane <= 0)
            {
                if (f.CapableSuperSpeed) lane = 5;
                else return Loc.T("capUsb2Only", caps.SpeedsSupportedText() ?? Loc.Get("capUnknown"));
            }

            var name = UsbGeneration.Describe(lane, caps.DeclaredLanes);
            string text = Loc.T("capMain", GenerationName.FormatSpeed(lane), name.Current);
            if (!string.IsNullOrEmpty(name.Legacy)) text += Loc.T("capLegacyPart", name.Legacy);

            string all = caps.SpeedsSupportedText();
            if (!string.IsNullOrEmpty(all)) text += Loc.T("capSpeedsPart", all);

            if (caps.DeclaredLanes >= 2) text += Loc.Get("capTwoLane");
            if (caps.HasPowerDelivery) text += Loc.Get("capPd");
            if (caps.HasBillboard) text += Loc.Get("capAltMode");

            return text;
        }

        private static string BuildPortText(PortFacts f)
        {
            var parts = new List<string>();

            if (f.HasConnectorInfo)
                parts.Add(Loc.Get(f.PortIsTypeC ? "portTypeC" : "portNotTypeC"));

            if (f.HasV2)
                parts.Add(Loc.Get(f.PortSupportsUsb3 ? "portUsb3" : "portUsb2Only"));
            else if (f.HubType == 3)
                parts.Add(Loc.Get("portHub3"));

            if (f.PortHasCompanion) parts.Add(Loc.Get("portCompanion"));
            if (f.HasConnectorInfo && !f.PortIsUserConnectable) parts.Add(Loc.Get("portInternal"));

            return parts.Count == 0
                ? null
                : Loc.T("portPrefix", string.Join(Loc.Get("sepSlash"), parts));
        }

        // ==================================================================
        // 判定
        // ==================================================================

        private static void ApplyVerdict(UsbNode node, PortFacts f)
        {
            switch (f.Status)
            {
                case DeviceFailedEnumeration:
                    node.Level = VerdictLevel.Fault;
                    node.Verdict = Loc.Get("vFailedEnum");
                    return;
                case DeviceGeneralFailure:
                    node.Level = VerdictLevel.Fault;
                    node.Verdict = Loc.Get("vGeneralFail");
                    return;
                case DeviceCausedOvercurrent:
                    node.Level = VerdictLevel.Fault;
                    node.Verdict = Loc.Get("vOvercurrent");
                    return;
                case DeviceNotEnoughPower:
                    node.Level = VerdictLevel.Fault;
                    node.Verdict = Loc.Get("vNotEnoughPower");
                    return;
                case DeviceNotEnoughBandwidth:
                    node.Level = VerdictLevel.Caution;
                    node.Verdict = Loc.Get("vNotEnoughBandwidth");
                    return;
                case DeviceHubNestedTooDeeply:
                    node.Level = VerdictLevel.Caution;
                    node.Verdict = Loc.Get("vNested");
                    return;
                case DeviceInLegacyHub:
                    node.Level = VerdictLevel.Caution;
                    node.Verdict = Loc.Get("vLegacyHub");
                    return;
                case DeviceEnumerating:
                case DeviceReset:
                    node.Level = VerdictLevel.Caution;
                    node.Verdict = Loc.Get("vEnumerating");
                    return;
            }

            double deviceLane = f.Caps.MaxLaneGbps;
            if (deviceLane <= 0)
            {
                if (f.CapableSuperSpeedPlus) deviceLane = 10;
                else if (f.CapableSuperSpeed || f.BcdUsb >= 0x0300) deviceLane = 5;
            }
            double operatingLane = OperatingLaneGbps(f);

            // ポートが USB 2.0 なら、ケーブルの話をする前にポートの問題
            if (deviceLane >= 4.5 && f.HasV2 && !f.PortSupportsUsb3)
            {
                node.Level = VerdictLevel.Caution;
                node.Verdict = Loc.Get("vPortUsb2");
                return;
            }

            // 10Gbps 機器が 5Gbps に落ちている
            if (deviceLane >= 9.5 && operatingLane >= 4.5 && operatingLane < 9.5)
            {
                node.Level = VerdictLevel.Caution;
                node.Verdict = Loc.Get("vGen2AtGen1");
                return;
            }

            // USB 3.x 機器が USB 2.0 速度に落ちている
            if (deviceLane >= 4.5 && operatingLane < 1)
            {
                node.Level = VerdictLevel.Fault;
                node.Verdict = Loc.Get("vUsb3AtUsb2") + " "
                               + (f.PortIsTypeC ? Loc.Get("vUsb3AtUsb2C") : Loc.Get("vUsb3AtUsb2A"));
                return;
            }

            if (f.Caps.DeclaredLanes >= 2 && !f.OperatingSuperSpeedPlus)
            {
                node.Level = VerdictLevel.Caution;
                node.Verdict = Loc.Get("vTwoLane");
                return;
            }

            if (f.OperatingSuperSpeedPlus)
            {
                node.Level = VerdictLevel.Good;
                node.Verdict = Loc.Get("vGood10");
                return;
            }

            if (operatingLane >= 4.5)
            {
                node.Level = VerdictLevel.Good;
                node.Verdict = Loc.Get("vGood5");
                return;
            }

            node.Level = VerdictLevel.Good;
            node.Verdict = Loc.Get("vGoodNormal");
        }

        // ==================================================================
        // 各種 IOCTL
        // ==================================================================

        private static bool QueryV2(SafeFileHandle hub, int port, out uint protocols, out uint flags)
        {
            protocols = 0;
            flags = 0;
            var buf = new byte[16];
            BitConverter.GetBytes(port).CopyTo(buf, 0);
            BitConverter.GetBytes(16).CopyTo(buf, 4);   // Length

            int returned;
            if (!Win32.DeviceIoControl(hub, Win32.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2,
                    buf, buf.Length, buf, buf.Length, out returned, IntPtr.Zero))
                return false;

            protocols = BitConverter.ToUInt32(buf, 8);
            flags = BitConverter.ToUInt32(buf, 12);
            return true;
        }

        /// <summary>USB_PORT_CONNECTOR_PROPERTIES。Type-C かどうかはここでしかわからない。</summary>
        private static void ReadConnectorProperties(SafeFileHandle hub, int port, PortFacts facts)
        {
            var buf = new byte[0x200];
            BitConverter.GetBytes(port).CopyTo(buf, 0);
            BitConverter.GetBytes(buf.Length).CopyTo(buf, 4);   // ActualLength

            int returned;
            if (!Win32.DeviceIoControl(hub, Win32.IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES,
                    buf, buf.Length, buf, buf.Length, out returned, IntPtr.Zero))
                return;
            if (returned < 12) return;

            uint properties = BitConverter.ToUInt32(buf, 8);
            facts.HasConnectorInfo = true;
            facts.PortIsUserConnectable = (properties & 0x1) != 0;
            facts.PortHasCompanion = (properties & 0x4) != 0;
            facts.PortIsTypeC = (properties & 0x8) != 0;
        }

        /// <summary>BOS ディスクリプタを 2 段階で読む（まず長さ、次に全体）。</summary>
        private static DeviceCapabilityInfo ReadBos(SafeFileHandle hub, int port)
        {
            var head = GetDescriptor(hub, port, Win32.USB_BOS_DESCRIPTOR_TYPE, 0, 0, 5);
            if (head == null || head.Length < 5) return new DeviceCapabilityInfo();

            int total = BitConverter.ToUInt16(head, 2);
            if (total <= 5 || total > 2048) return BosParser.Parse(head);

            var full = GetDescriptor(hub, port, Win32.USB_BOS_DESCRIPTOR_TYPE, 0, 0, total);
            return BosParser.Parse(full ?? head);
        }

        /// <summary>機器が最初に返す言語 ID。英語決め打ちより確実。</summary>
        private static int GetFirstLanguageId(SafeFileHandle hub, int port)
        {
            var d = GetDescriptor(hub, port, Win32.USB_STRING_DESCRIPTOR_TYPE, 0, 0, 255);
            if (d == null || d.Length < 4) return 0x0409;
            return BitConverter.ToUInt16(d, 2);
        }

        /// <summary>指定のディスクリプタを取得し、ディスクリプタ本体だけを返す。</summary>
        private static byte[] GetDescriptor(SafeFileHandle hub, int port, byte type, byte index, int langId, int length)
        {
            if (length <= 0 || length > 4096) return null;

            var buf = new byte[12 + length];   // USB_DESCRIPTOR_REQUEST (pack=1)
            BitConverter.GetBytes(port).CopyTo(buf, 0);
            buf[4] = 0x80;                                                       // bmRequest: device-to-host
            buf[5] = 0x06;                                                       // bRequest: GET_DESCRIPTOR
            BitConverter.GetBytes((ushort)((type << 8) | index)).CopyTo(buf, 6); // wValue
            BitConverter.GetBytes((ushort)langId).CopyTo(buf, 8);                // wIndex
            BitConverter.GetBytes((ushort)length).CopyTo(buf, 10);               // wLength

            int returned;
            if (!Win32.DeviceIoControl(hub, Win32.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION,
                    buf, buf.Length, buf, buf.Length, out returned, IntPtr.Zero))
                return null;

            int payload = returned - 12;
            if (payload < 2) return null;
            if (payload > length) payload = length;

            var result = new byte[payload];
            Array.Copy(buf, 12, result, 0, payload);
            return result;
        }

        private static string GetStringDescriptor(SafeFileHandle hub, int port, byte index, int langId)
        {
            if (index == 0) return null;
            var d = GetDescriptor(hub, port, Win32.USB_STRING_DESCRIPTOR_TYPE, index, langId, 255);
            if (d == null || d.Length < 4) return null;

            int length = Math.Min(d[0], d.Length);
            if (length < 4) return null;
            return Win32.ReadUnicodeZ(d, 2, length)?.Trim();
        }

        private static string GetNodeConnectionName(SafeFileHandle hub, int port)
        {
            var buf = new byte[0x400];
            BitConverter.GetBytes(port).CopyTo(buf, 0);

            int returned;
            if (!Win32.DeviceIoControl(hub, Win32.IOCTL_USB_GET_NODE_CONNECTION_NAME,
                    buf, buf.Length, buf, buf.Length, out returned, IntPtr.Zero))
                return null;

            return Win32.ReadUnicodeZ(buf, 8, returned);   // USB_NODE_CONNECTION_NAME
        }

        // ==================================================================

        /// <summary>
        /// bcdUSB（BCD 表記のバージョン）を "3.20" のような文字列にする。
        /// 末尾の桁が 0 のときは "2.0" のように落とす。
        /// </summary>
        private static string BcdText(ushort bcdUsb)
        {
            int major = (bcdUsb >> 8) & 0xFF;
            int minor = (bcdUsb >> 4) & 0x0F;
            int sub = bcdUsb & 0x0F;

            return sub == 0
                ? $"{major:X}.{minor:X}"
                : $"{major:X}.{minor:X}{sub:X}";
        }

        private static string DescribeClass(byte deviceClass)
        {
            switch (deviceClass)
            {
                case 0x01: return Loc.Get("devAudio");
                case 0x03: return Loc.Get("devHid");
                case 0x06: return Loc.Get("devCamera");
                case 0x07: return Loc.Get("devPrinter");
                case 0x08: return Loc.Get("devStorage");
                case 0x09: return Loc.Get("devHub");
                case 0x0E: return Loc.Get("devVideo");
                case 0xE0: return Loc.Get("devWireless");
                default: return Loc.Get("devOther");
            }
        }

        private static string Join(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a)) return b?.Trim();
            if (string.IsNullOrWhiteSpace(b)) return a.Trim();
            return (a.Trim() + " " + b.Trim()).Trim();
        }
    }
}
