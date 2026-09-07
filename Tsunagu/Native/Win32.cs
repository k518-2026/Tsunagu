using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Tsunagu.Native
{
    /// <summary>
    /// USB ハブ / ストレージ / バッテリーを直接たたくための Win32 宣言。
    /// 外部パッケージを使わずに済むよう、必要なものだけをここに集約している。
    /// </summary>
    internal static class Win32
    {
        // ---------- CreateFile ----------
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint CREATE_ALWAYS = 2;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        public const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
        public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        public const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;

        // ---------- SetupAPI ----------
        public const int DIGCF_PRESENT = 0x00000002;
        public const int DIGCF_DEVICEINTERFACE = 0x00000010;
        public const int SPDRP_DEVICEDESC = 0x00000000;
        public const int SPDRP_FRIENDLYNAME = 0x0000000C;

        public static readonly Guid GUID_DEVINTERFACE_USB_HOST_CONTROLLER =
            new Guid("3abf6f2d-71c4-462a-8a92-1e6861e6af27");
        public static readonly Guid GUID_DEVICE_BATTERY =
            new Guid("72631e54-78a4-11d0-bcf7-00aa00b7b32a");

        // ---------- USB IOCTL (usbioctl.h) ----------
        public const uint IOCTL_USB_GET_ROOT_HUB_NAME = 0x220408;
        public const uint IOCTL_USB_GET_NODE_INFORMATION = 0x220408;
        public const uint IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = 0x220410;
        public const uint IOCTL_USB_GET_NODE_CONNECTION_NAME = 0x220414;
        public const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x220448;
        public const uint IOCTL_USB_GET_HUB_CAPABILITIES_EX = 0x220450;
        public const uint IOCTL_USB_GET_HUB_INFORMATION_EX = 0x220454;
        public const uint IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES = 0x220458;
        public const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 = 0x22045C;

        // ---------- USB ディスクリプタ種別 ----------
        public const byte USB_STRING_DESCRIPTOR_TYPE = 0x03;
        public const byte USB_BOS_DESCRIPTOR_TYPE = 0x0F;
        public const byte USB_DEVICE_CAPABILITY_DESCRIPTOR_TYPE = 0x10;

        // ---------- ストレージ ----------
        public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        // ---------- バッテリー (batclass.h) ----------
        public const uint IOCTL_BATTERY_QUERY_TAG = 0x00294040;
        public const uint IOCTL_BATTERY_QUERY_INFORMATION = 0x00294044;
        public const uint IOCTL_BATTERY_QUERY_STATUS = 0x0029404C;

        public const uint BATTERY_POWER_ON_LINE = 0x00000001;
        public const uint BATTERY_DISCHARGING = 0x00000002;
        public const uint BATTERY_CHARGING = 0x00000004;
        public const uint BATTERY_CRITICAL = 0x00000008;
        public const int BATTERY_UNKNOWN_RATE = unchecked((int)0x80000000);
        public const uint BATTERY_UNKNOWN_CAPACITY = 0xFFFFFFFF;
        public const uint BATTERY_UNKNOWN_VOLTAGE = 0xFFFFFFFF;

        // ---------- 構造体 ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        // ---------- P/Invoke ----------
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode,
            byte[] lpInBuffer, int nInBufferSize,
            byte[] lpOutBuffer, int nOutBufferSize,
            out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteFile(SafeFileHandle hFile, IntPtr lpBuffer,
            int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadFile(SafeFileHandle hFile, IntPtr lpBuffer,
            int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
            ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetailSize(IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr detail, int detailSize,
            out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr detail, int detailSize,
            out int requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "SetupDiGetDeviceRegistryPropertyW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData, int property, out int propertyRegDataType,
            byte[] propertyBuffer, int propertyBufferSize, out int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        // ---------- ユーティリティ ----------

        /// <summary>バイト配列中の UTF-16 文字列を、NUL または終端まで取り出す。</summary>
        public static string ReadUnicodeZ(byte[] buffer, int offset, int limit)
        {
            if (buffer == null || offset >= buffer.Length) return null;
            int end = Math.Min(limit, buffer.Length);
            var sb = new StringBuilder();
            for (int i = offset; i + 1 < end; i += 2)
            {
                char c = (char)(buffer[i] | (buffer[i + 1] << 8));
                if (c == '\0') break;
                sb.Append(c);
            }
            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}
