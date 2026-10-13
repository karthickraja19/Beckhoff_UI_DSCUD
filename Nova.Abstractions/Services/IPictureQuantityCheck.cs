using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>One line of Pic_Upload_Record: how many pictures a unit's zip held.</summary>
    public sealed class PictureUploadRecord
    {
        public DateTime Time { get; set; }

        public string UnitSn { get; set; }

        public int Quantity { get; set; }

        public double? Lsl { get; set; }

        public double? Usl { get; set; }

        /// <summary>Match_Result: LSL &lt;= quantity &lt;= USL.</summary>
        public bool Match { get; set; }
    }

    /// <summary>
    /// Audio Picture Upload Quantity Check (T5, X7): every unit's picture count is
    /// recorded against the Pic_Upload_quantity spec limits, a short count puts the
    /// machine down and stops dispensing, and only a two-step verified user can let the
    /// machine dispense again.
    /// </summary>
    public interface IPictureQuantityCheck
    {
        /// <summary>
        /// The "dispensing control" value: true (1) lets the SCUD dispense, false (0) after
        /// a unit had the wrong number of pictures. A HIVE state change does not change it.
        /// </summary>
        bool DispensingEnabled { get; }

        /// <summary>Who set the value last, and when.</summary>
        string LastChange { get; }

        /// <summary>The limits a unit is checked against, e.g. "6 to 6 pictures".</summary>
        string LimitsText { get; }

        /// <summary>Where Pic_Upload_Record is written.</summary>
        string RecordFolder { get; }

        /// <summary>Sets the value back to 1 once the image problem is solved (two-step verified).</summary>
        bool EnableDispensing(string operatorName, string approverName, out string error);

        /// <summary>Records one unit. A count outside the limits disables dispensing and puts the machine down.</summary>
        PictureUploadRecord ReportUnit(string unitSn, int pictureQuantity);

        /// <summary>The newest records first.</summary>
        IReadOnlyList<PictureUploadRecord> Recent(int max);

        /// <summary>Raised when the value or the records change. May be raised on any thread.</summary>
        event EventHandler Changed;
    }
}
