using System.Globalization;

namespace HanVGamepad.Desktop.Services
{
    /// <summary>界面语言定义。阿拉伯语从右往左读，渲染时要按 RTL 处理。</summary>
    public static class Languages
    {
        public const string Chinese = "zh";
        public const string English = "en";
        public const string Arabic = "ar";

        public static readonly string[] All = { Chinese, English, Arabic };

        /// <summary>
        /// 系统 UI 语言对应的界面语言。只认中文 / 英文 / 阿拉伯语：
        /// 法、日、德这些没有对应文案，统一回英文，而不是硬套中文。
        /// </summary>
        public static string FromSystem()
        {
            try
            {
                string code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

                if (string.Equals(code, Chinese, StringComparison.OrdinalIgnoreCase))
                {
                    return Chinese;
                }

                if (string.Equals(code, Arabic, StringComparison.OrdinalIgnoreCase))
                {
                    return Arabic;
                }
            }
            catch (Exception)
            {
                // 受限环境拿不到区域设置：回英文，不能因此起不来。
            }

            return English;
        }
    }
}
