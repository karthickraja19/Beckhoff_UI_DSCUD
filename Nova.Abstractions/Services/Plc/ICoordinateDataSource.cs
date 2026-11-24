using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// One measured coordinate: a point of a unit on one axis (S2.4.4.6 Coordinate Data
    /// Analysis), e.g. point 3 of unit FVMDJ20X6G on X = 140.013 mm.
    /// </summary>
    public sealed class CoordinateRecord
    {
        public DateTime Time { get; set; }

        public string UnitSn { get; set; }

        /// <summary>The point's name, e.g. "3" or "Dispense start".</summary>
        public string Point { get; set; }

        /// <summary>"X", "Y", "Z", "A", ...</summary>
        public string Axis { get; set; }

        public double Value { get; set; }

        public double? Nominal { get; set; }

        public double? Lsl { get; set; }

        public double? Usl { get; set; }
    }

    /// <summary>
    /// The coordinates behind the Data page's Axis tab (S2.4.4.6): the positions the
    /// machine measured for each point of each unit.
    /// </summary>
    public interface ICoordinateDataSource
    {
        /// <summary>Every coordinate measured in the window, oldest first.</summary>
        Task<IReadOnlyList<CoordinateRecord>> QueryAsync(DateTime start, DateTime end);
    }
}
