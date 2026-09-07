using System;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Tsunagu.Localization;

namespace Tsunagu
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 言語ファイルを読み、保存された選択（なければ OS の表示言語）を適用する
            Loc.Initialize();

            // WPF 内部の日付・数値の既定書式も同じ言語に合わせる
            try
            {
                var culture = CultureInfo.GetCultureInfo(Loc.CurrentCode);
                FrameworkElement.LanguageProperty.OverrideMetadata(
                    typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
            }
            catch (Exception)
            {
                // 対応する CultureInfo がなくても起動は妨げない
            }

            base.OnStartup(e);
        }
    }
}
