using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Tsunagu.Native
{
    internal sealed class DeviceInterfaceInfo
    {
        public string DevicePath;
        public string Description;
    }

    /// <summary>SetupAPI で指定 GUID のデバイスインターフェースを列挙する。</summary>
    internal static class DeviceInterfaceEnumerator
    {
        public static List<DeviceInterfaceInfo> Enumerate(Guid classGuid)
        {
            var result = new List<DeviceInterfaceInfo>();
            IntPtr set = Win32.SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero,
                Win32.DIGCF_PRESENT | Win32.DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;

            try
            {
                int interfaceDataSize = Marshal.SizeOf(typeof(Win32.SP_DEVICE_INTERFACE_DATA));
                int devInfoSize = Marshal.SizeOf(typeof(Win32.SP_DEVINFO_DATA));

                for (int index = 0; ; index++)
                {
                    var did = new Win32.SP_DEVICE_INTERFACE_DATA { cbSize = interfaceDataSize };
                    if (!Win32.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref classGuid, index, ref did))
                        break;

                    int required;
                    Win32.SetupDiGetDeviceInterfaceDetailSize(set, ref did, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required <= 4) continue;

                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        // 64bit では cbSize に 8、32bit では 6 を入れる（Win32 の仕様）
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                        var devInfo = new Win32.SP_DEVINFO_DATA { cbSize = devInfoSize };
                        int written;
                        if (!Win32.SetupDiGetDeviceInterfaceDetail(set, ref did, detail, required, out written, ref devInfo))
                            continue;

                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (string.IsNullOrEmpty(path)) continue;

                        result.Add(new DeviceInterfaceInfo
                        {
                            DevicePath = path,
                            Description = GetProperty(set, ref devInfo, Win32.SPDRP_FRIENDLYNAME)
                                          ?? GetProperty(set, ref devInfo, Win32.SPDRP_DEVICEDESC)
                        });
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                Win32.SetupDiDestroyDeviceInfoList(set);
            }

            return result;
        }

        private static string GetProperty(IntPtr set, ref Win32.SP_DEVINFO_DATA devInfo, int property)
        {
            var buffer = new byte[1024];
            int type, required;
            if (!Win32.SetupDiGetDeviceRegistryProperty(set, ref devInfo, property, out type,
                    buffer, buffer.Length, out required))
                return null;

            string s = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s.Substring(0, nul);
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
    }
}
