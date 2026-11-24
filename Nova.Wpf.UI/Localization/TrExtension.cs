using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Nova.Wpf.UI.Localization
{
    /// <summary>
    /// Markup extension for localized text:
    ///
    /// <code>
    ///   &lt;TextBlock Text="{loc:Tr Settings_ShiftSettings}"/&gt;
    ///   &lt;Button Content="{loc:Tr Common_Save}"/&gt;
    ///   &lt;TextBlock Text="{loc:Tr Alarm_ActiveCount, StringFormat='({0})'}"/&gt;
    /// </code>
    ///
    /// It produces a one-way binding onto <see cref="LocalizationSource"/> rather than a
    /// plain string, which is the whole point: a plain string would be resolved once when
    /// the view loads and would still be in the old language after a switch. Because this
    /// is a binding, the text follows the language for as long as the element lives.
    ///
    /// Works anywhere a binding works, including inside a <c>Style</c> setter or a
    /// <c>DataTemplate</c>.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class TrExtension : MarkupExtension
    {
        public TrExtension()
        {
        }

        public TrExtension(string key)
        {
            Key = key;
        }

        /// <summary>
        /// Resource key, matching a <c>&lt;data name="..."&gt;</c> entry in Strings.resx.
        ///
        /// Keys are word characters and underscores by convention (<c>Settings_StartTime</c>).
        /// That is not decoration: the key becomes a WPF binding path of the form
        /// <c>[Key]</c>, and a key containing a comma, bracket or parenthesis would be
        /// parsed as binding syntax rather than as part of the name.
        /// </summary>
        [ConstructorArgument("key")]
        public string Key { get; set; }

        /// <summary>
        /// Optional format applied to the resolved string, for the occasional caption that
        /// wraps a translated word in punctuation. Prefer a placeholder inside the resource
        /// itself where the whole phrase is translatable.
        /// </summary>
        public string StringFormat { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return string.Empty;

            var binding = new Binding("[" + Key + "]")
            {
                Source = LocalizationSource.Instance,
                Mode = BindingMode.OneWay,
                StringFormat = StringFormat
            };

            // Delegating to the binding's own ProvideValue is what makes this usable in
            // every context: against a dependency property it yields a live
            // BindingExpression, and inside a Setter or template it yields the Binding
            // itself, which is what those expect.
            return binding.ProvideValue(serviceProvider);
        }
    }
}
