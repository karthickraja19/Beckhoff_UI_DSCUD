using Nova.Wpf.UI.Localization;

namespace Nova.Hive
{
    /// <summary>
    /// Dual language for text the HIVE dialogs set from code. The procedures and the HIVE
    /// state machine work in English - it is also what is sent to HIVE and MES - so only
    /// what is shown is translated, here, by its English text (see LocalizationSource).
    /// The alarm pop-up's error message and detail are deliberately not passed through
    /// this: alarm text is shown exactly as the description for its code gives it.
    /// </summary>
    internal static class HiveText
    {
        public static string T(string english)
        {
            return string.IsNullOrEmpty(english) ? english : LocalizationSource.Instance.Translate(english);
        }
    }
}
