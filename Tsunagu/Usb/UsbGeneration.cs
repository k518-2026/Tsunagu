using System;
using Tsunagu.Localization;

namespace Tsunagu.Usb
{
    /// <summary>
    /// リンク速度から USB の呼び名を組み立てる。
    /// 同じ実体に 3 通りの名前があるため、まとめて出さないと箱の表記と突き合わせられない。
    /// 規格名そのもの（USB 3.2 Gen 2x1 など）は世界共通なので訳さない。
    /// </summary>
    public sealed class GenerationName
    {
        public string Current;      // 現行の正式名
        public string Legacy;       // 旧称。ないときは null
        public string Marketing;    // USB-IF の表示名
        public double TotalGbps;

        public string OneLine
        {
            get
            {
                string s = Current;
                if (TotalGbps >= 1) s += Loc.T("genSpeedParen", FormatSpeed(TotalGbps));
                if (!string.IsNullOrEmpty(Legacy)) s += Loc.T("genLegacyPart", Legacy);
                if (!string.IsNullOrEmpty(Marketing)) s += Loc.T("genMarketingPart", Marketing);
                return s;
            }
        }

        public static string FormatSpeed(double gbps)
        {
            if (gbps >= 1) return gbps.ToString("0.#") + "Gbps";
            return (gbps * 1000).ToString("0.#") + "Mbps";
        }
    }

    public static class UsbGeneration
    {
        /// <summary>レーンあたりの速度とレーン数から呼び名を決める。</summary>
        public static GenerationName Describe(double laneGbps, int lanes)
        {
            if (lanes < 1) lanes = 1;

            if (laneGbps < 1)
            {
                if (laneGbps >= 0.4)
                    return new GenerationName { Current = Loc.Get("genUsb2"), Marketing = "USB 480Mbps", TotalGbps = 0.48 };
                if (laneGbps >= 0.01)
                    return new GenerationName { Current = Loc.Get("genUsb11"), Marketing = "USB 12Mbps", TotalGbps = 0.012 };
                return new GenerationName { Current = Loc.Get("genUsb10"), Marketing = "USB 1.5Mbps", TotalGbps = 0.0015 };
            }

            double total = laneGbps * lanes;

            if (Near(laneGbps, 5) && lanes == 1)
                return new GenerationName
                {
                    Current = "USB 3.2 Gen 1x1",
                    Legacy = Loc.Get("helpLegacyGen1"),
                    Marketing = "USB 5Gbps",
                    TotalGbps = 5
                };

            if (Near(laneGbps, 5) && lanes >= 2)
                return new GenerationName
                {
                    Current = "USB 3.2 Gen 1x2",
                    Legacy = null,   // USB 3.2 で新設されたため旧称はない
                    Marketing = "USB 10Gbps",
                    TotalGbps = 10
                };

            if (Near(laneGbps, 10) && lanes == 1)
                return new GenerationName
                {
                    Current = "USB 3.2 Gen 2x1",
                    Legacy = Loc.Get("helpLegacyGen2"),
                    Marketing = "USB 10Gbps",
                    TotalGbps = 10
                };

            if (Near(laneGbps, 10) && lanes >= 2)
                return new GenerationName
                {
                    Current = "USB 3.2 Gen 2x2",
                    Legacy = null,
                    Marketing = "USB 20Gbps",
                    TotalGbps = 20
                };

            if (Near(laneGbps, 20))
                return new GenerationName
                {
                    Current = lanes >= 2 ? "USB4 Gen 3x2" : "USB4 Gen 3x1",
                    Marketing = lanes >= 2 ? "USB 40Gbps" : "USB 20Gbps",
                    TotalGbps = total
                };

            return new GenerationName
            {
                Current = Loc.T("genPerLane", FormatSpeedOf(laneGbps), lanes),
                TotalGbps = total
            };
        }

        private static string FormatSpeedOf(double gbps) => GenerationName.FormatSpeed(gbps);

        private static bool Near(double a, double b) => Math.Abs(a - b) < 0.6;
    }
}
