using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// A tab that exists in the indicator bar but whose page has not been built yet -
    /// currently Alarm, Config, Data, Vision and Setting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All six buttons are present because the bar is specified as a whole: Audio SW
    /// DFM v5.4 slide 42 fixes the button count, their order, their equal size and the
    /// fixed gap between them. Dropping five of them until their pages exist would
    /// mean re-measuring the bar later against a spec that already dimensions it.
    /// </para>
    /// <para>
    /// So the buttons navigate, they energize correctly, and Config and Setting still
    /// disable before login per slide 39 - the page behind them simply says what it
    /// will hold and which slide specifies it. That is deliberately not a blank panel:
    /// an operator or reviewer opening the tab should be able to tell "not built yet"
    /// from "built and broken".
    /// </para>
    /// </remarks>
    public sealed class AudioPlaceholderViewModel : AudioPageViewModel
    {
        private readonly AppPageNames _pageName;

        public override AppPageNames PageName => _pageName;

        /// <summary>Tab name as it appears in the DFM.</summary>
        public string Title { get; }

        /// <summary>One line on what the page will contain.</summary>
        public string Summary { get; }

        /// <summary>Which DFM slides specify it, so the reference is on screen.</summary>
        public string SpecReference { get; }

        public AudioPlaceholderViewModel(
            IAppSession session,
            AppPageNames pageName,
            string title,
            string summary,
            string specReference)
            : base(session)
        {
            _pageName = pageName;
            Title = title;
            Summary = summary;
            SpecReference = specReference;
        }

        /// <summary>
        /// The five pages that are not built yet, with what each one owes the spec.
        /// Kept together so the list is obviously a to-do list rather than five
        /// scattered stubs.
        /// </summary>
        public static AudioPlaceholderViewModel Alarm(IAppSession session)
        {
            return new AudioPlaceholderViewModel(
                session,
                AppPageNames.Alarm,
                "Alarm",
                "Start/end time filter with Query and Save, the Top 10 downtime breakdown "
                + "as a table and pie, an alarm-duration histogram bucketed in five-minute "
                + "steps, and the alarm history table.",
                "DFM v5.4 slide 56");
        }

        public static AudioPlaceholderViewModel Config(IAppSession session)
        {
            return new AudioPlaceholderViewModel(
                session,
                AppPageNames.Config,
                "Config",
                "Machine configuration across two thirds of the width, with the Spec Limits "
                + "table and its Edit/Save buttons in the remaining third. View-only at "
                + "Level 1, non-critical edits at Level 2, full edit at Level 3.",
                "DFM v5.4 slide 58");
        }

        public static AudioPlaceholderViewModel Data(IAppSession session)
        {
            return new AudioPlaceholderViewModel(
                session,
                AppPageNames.Data,
                "Data",
                "I/O and Tossing views, each switchable between Hourly and Daily, with the "
                + "charts and unit table across four fifths of the width and the summary "
                + "column plus Export in the last fifth.",
                "DFM v5.4 slides 59 and 60");
        }

        public static AudioPlaceholderViewModel Vision(IAppSession session)
        {
            return new AudioPlaceholderViewModel(
                session,
                AppPageNames.Vision,
                "Vision",
                "A 3x2 grid of CCD captures for the unit currently in process; clicking one "
                + "magnifies it and lists the capture information beside it.",
                "DFM v5.4 slide 61");
        }

        public static AudioPlaceholderViewModel Setting(IAppSession session)
        {
            return new AudioPlaceholderViewModel(
                session,
                AppPageNames.Settings,
                "Setting",
                "Site, product type, line, station, machine number and language - all chosen "
                + "from menus, never typed - beside the resolved hardware configuration, "
                + "with the hardware-setting dialog behind Change Config.",
                "DFM v5.4 slides 62 to 64");
        }
    }
}
