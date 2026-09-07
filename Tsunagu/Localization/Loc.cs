using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Tsunagu.Localization
{
    public sealed class LanguageOption
    {
        public string Code { get; set; }     // ja, en, zh-Hans …
        public string Name { get; set; }     // その言語自身での表記
        public override string ToString() => Name;
    }

    /// <summary>
    /// 文字列の一元管理。XAML からは {loc:T key} で、コードからは Loc.T("key", args) で引く。
    /// 言語を切り替えると Item[] の変更通知が飛び、画面の文言がその場で入れ替わる。
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc Instance { get; } = new Loc();

        /// <summary>言語が切り替わったとき。動的に作った文言はここで作り直す。</summary>
        public static event EventHandler LanguageChanged;

        private const string FallbackCode = "en";

        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, string> _current;
        private static Dictionary<string, string> _fallback;

        public static List<LanguageOption> Available { get; } = new List<LanguageOption>();
        public static string CurrentCode { get; private set; }

        private Loc() { }

        // ------------------------------------------------------------------
        // 起動時の読み込み
        // ------------------------------------------------------------------

        public static void Initialize()
        {
            LoadEmbedded();
            LoadExternal();

            Available.Clear();
            foreach (var pair in Tables.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                string name;
                if (!pair.Value.TryGetValue("_name", out name)) name = pair.Key;
                Available.Add(new LanguageOption { Code = pair.Key, Name = name });
            }

            Tables.TryGetValue(FallbackCode, out _fallback);
            SetLanguage(LoadSavedCode() ?? DetectSystemCode(), false);
        }

        /// <summary>アセンブリに埋め込んだ .lang を読む。</summary>
        private static void LoadEmbedded()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith(".lang", StringComparison.OrdinalIgnoreCase)) continue;

                string code = Path.GetFileNameWithoutExtension(name);
                int dot = code.LastIndexOf('.');
                if (dot >= 0) code = code.Substring(dot + 1);

                using (var stream = asm.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    Merge(code, Parse(reader.ReadToEnd()));
            }
        }

        /// <summary>実行ファイルの隣の lang フォルダーからも読む。再ビルドなしで言語を足せる。</summary>
        private static void LoadExternal()
        {
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "lang");
                if (!Directory.Exists(dir)) return;

                foreach (var file in Directory.GetFiles(dir, "*.lang"))
                    Merge(Path.GetFileNameWithoutExtension(file),
                        Parse(File.ReadAllText(file, Encoding.UTF8)));
            }
            catch
            {
                // 外部ファイルが壊れていても起動は妨げない
            }
        }

        private static void Merge(string code, Dictionary<string, string> table)
        {
            if (string.IsNullOrEmpty(code) || table.Count == 0) return;

            Dictionary<string, string> existing;
            if (Tables.TryGetValue(code, out existing))
            {
                foreach (var kv in table) existing[kv.Key] = kv.Value;   // 外部ファイルが優先
            }
            else
            {
                Tables[code] = table;
            }
        }

        private static Dictionary<string, string> Parse(string text)
        {
            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim('\r', ' ', '\t');
                if (line.Length == 0 || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                if (key.Length > 0) table[key] = value;
            }
            return table;
        }

        // ------------------------------------------------------------------
        // 言語の決定
        // ------------------------------------------------------------------

        /// <summary>OS の表示言語から、いちばん近いものを選ぶ。</summary>
        private static string DetectSystemCode()
        {
            var culture = CultureInfo.CurrentUICulture;

            for (var c = culture; c != null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
            {
                if (Tables.ContainsKey(c.Name)) return c.Name;

                // zh-CN → zh-Hans、zh-TW → zh-Hant のような対応づけ
                if (c.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    string script = c.Name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0
                                    || c.Name.EndsWith("TW", StringComparison.OrdinalIgnoreCase)
                                    || c.Name.EndsWith("HK", StringComparison.OrdinalIgnoreCase)
                        ? "zh-Hant" : "zh-Hans";
                    if (Tables.ContainsKey(script)) return script;
                }

                if (c.Parent == c) break;
            }

            string twoLetter = culture.TwoLetterISOLanguageName;
            if (Tables.ContainsKey(twoLetter)) return twoLetter;

            return Tables.ContainsKey(FallbackCode) ? FallbackCode : Tables.Keys.FirstOrDefault();
        }

        public static void SetLanguage(string code) => SetLanguage(code, true);

        private static void SetLanguage(string code, bool persist)
        {
            if (string.IsNullOrEmpty(code) || !Tables.ContainsKey(code))
                code = Tables.ContainsKey(FallbackCode) ? FallbackCode : Tables.Keys.FirstOrDefault();
            if (code == null) return;
            if (CurrentCode == code) return;

            CurrentCode = code;
            _current = Tables[code];

            if (persist) SaveCode(code);

            Instance.OnPropertyChanged("Item[]");
            LanguageChanged?.Invoke(Instance, EventArgs.Empty);
        }

        // ------------------------------------------------------------------
        // 引き当て
        // ------------------------------------------------------------------

        /// <summary>XAML バインディング用のインデクサー。</summary>
        public string this[string key] => Get(key);

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            string value;
            if (_current != null && _current.TryGetValue(key, out value)) return value;
            if (_fallback != null && _fallback.TryGetValue(key, out value)) return value;
            return key;   // 訳が抜けていればキーがそのまま出るので、欠落に気づける
        }

        public static string T(string key, params object[] args)
        {
            string format = Get(key);
            if (args == null || args.Length == 0) return format;
            try { return string.Format(format, args); }
            catch (FormatException) { return format; }
        }

        // ------------------------------------------------------------------
        // 選択の保存
        // ------------------------------------------------------------------

        private static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Tsunagu", "language.txt");

        private static string LoadSavedCode()
        {
            try
            {
                return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null;
            }
            catch { return null; }
        }

        private static void SaveCode(string code)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, code);
            }
            catch { }
        }

        // ------------------------------------------------------------------

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
