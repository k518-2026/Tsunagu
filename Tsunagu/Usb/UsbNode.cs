using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace Tsunagu.Usb
{
    public enum VerdictLevel
    {
        None,
        Good,
        Caution,
        Fault
    }

    public enum NodeKind
    {
        Controller,
        Hub,
        Device,
        EmptyPort
    }

    /// <summary>USB ツリー上の 1 ノード（コントローラー／ハブ／機器／空きポート）。</summary>
    public sealed class UsbNode
    {
        public NodeKind Kind { get; set; }

        /// <summary>表示名（機器名やコントローラー名）。</summary>
        public string Title { get; set; }

        /// <summary>いま出ているリンク速度と、その世代名。</summary>
        public string LinkText { get; set; }

        /// <summary>機器そのものが対応している速度。</summary>
        public string CapabilityText { get; set; }

        /// <summary>つながっているポート側の情報（コネクタ形状・対応規格）。</summary>
        public string PortText { get; set; }

        /// <summary>VID/PID と規格申告値。</summary>
        public string IdText { get; set; }

        /// <summary>コントローラーやハブ用の補足行。</summary>
        public string Subtitle { get; set; }

        /// <summary>ケーブル観点の判定文。</summary>
        public string Verdict { get; set; }

        public VerdictLevel Level { get; set; } = VerdictLevel.None;

        /// <summary>変化検知用のキー。</summary>
        public string Key { get; set; }

        public ObservableCollection<UsbNode> Children { get; } = new ObservableCollection<UsbNode>();

        public Brush VerdictBrush
        {
            get
            {
                switch (Level)
                {
                    case VerdictLevel.Good: return Palette.Good;
                    case VerdictLevel.Caution: return Palette.Caution;
                    case VerdictLevel.Fault: return Palette.Fault;
                    default: return Palette.Muted;
                }
            }
        }

        public IEnumerable<UsbNode> Flatten()
        {
            yield return this;
            foreach (var child in Children)
                foreach (var n in child.Flatten())
                    yield return n;
        }
    }

    internal static class Palette
    {
        public static readonly Brush Good = Freeze(Color.FromRgb(0x0F, 0x76, 0x5C));
        public static readonly Brush Caution = Freeze(Color.FromRgb(0xA7, 0x5B, 0x00));
        public static readonly Brush Fault = Freeze(Color.FromRgb(0xB3, 0x2D, 0x2D));
        public static readonly Brush Muted = Freeze(Color.FromRgb(0x6B, 0x71, 0x7A));

        private static Brush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
