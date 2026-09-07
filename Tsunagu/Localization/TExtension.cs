using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Tsunagu.Localization
{
    /// <summary>
    /// XAML で {loc:T scanHint} と書くための拡張。
    /// 中身は Loc.Instance へのバインディングなので、言語を切り替えると自動で追従する。
    /// </summary>
    public sealed class TExtension : MarkupExtension
    {
        public string Key { get; set; }

        public TExtension() { }

        public TExtension(string key) { Key = key; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key)) return string.Empty;

            var binding = new Binding($"[{Key}]")
            {
                Source = Loc.Instance,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
