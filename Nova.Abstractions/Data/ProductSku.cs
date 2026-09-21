namespace Nova.Abstractions.Data
{
    /// <summary>
    /// Product type selected on the Settings page, which colours the station ID in the
    /// indicator bar. Audio SW DFM v5.4 slide 42: "Products with multiple SKUs have
    /// colored background on station ID to indicate SKU"; slide 62 lists the options as
    /// M-SKU, E-SKU, Normal(E/M) and Common.
    /// </summary>
    public enum ProductSku
    {
        /// <summary>
        /// Single-SKU product. No fill - the station ID shows as the grey #CCCCCC
        /// watermark (slide 40, "STATION ID  Text R 204 G 204 B 204").
        /// </summary>
        Common = 0,

        /// <summary>Normal(E/M). Fill R 57 G 130 B 81.</summary>
        Normal = 1,

        /// <summary>M-SKU. Fill R 121 G 132 B 231.</summary>
        M = 2,

        /// <summary>E-SKU. Fill R 246 G 174 B 66.</summary>
        E = 3
    }
}
