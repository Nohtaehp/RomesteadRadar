using System;
using System.IO;
using System.Text.RegularExpressions;
using Candide;
using Shared;

namespace RomesteadRadar
{
    /// <summary>
    /// 插件 UI 的中英文切换。
    /// 中文（zh_CN / zh_TW / 其它 zh 变体）显示中文；其它语言一律回退英文。
    /// </summary>
    internal static class Loc
    {
        private static bool? _startupChinese;

        internal static bool IsChinese
        {
            get
            {
                string code = Globals.LanguageCode;
                if (!string.IsNullOrEmpty(code))
                    return code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                return SharedGlobals.ChineseVersion;
            }
        }

        /// <summary>
        /// 插件加载时游戏语言还没初始化，读取游戏设置文件判断一次，供 Mod Settings Menu 标签使用。
        /// </summary>
        internal static bool StartupChinese
        {
            get
            {
                if (!_startupChinese.HasValue)
                    _startupChinese = DetectStartupChinese();
                return _startupChinese.Value;
            }
        }

        internal static string T(string chinese, string english)
        {
            return IsChinese ? chinese : english;
        }

        internal static string TS(string chinese, string english)
        {
            return StartupChinese ? chinese : english;
        }

        private static bool DetectStartupChinese()
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Romestead");
                if (!Directory.Exists(root))
                    return false;

                foreach (string file in Directory.GetFiles(root, "localization.json", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    Match match = Regex.Match(
                        text,
                        "\"LanguageCode\"\\s*:\\s*\"([^\"]+)\"",
                        RegexOptions.IgnoreCase);
                    if (!match.Success)
                        continue;

                    string code = match.Groups[1].Value;
                    return code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // 读取失败时回退英文。
            }
            return false;
        }
    }
}
