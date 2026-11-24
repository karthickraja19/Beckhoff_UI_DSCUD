using System.Collections.Generic;
using System.IO;
using System.Text;
using Nova.Abstractions.Hardware;

namespace Nova.Core.Config
{
    /// <summary>
    /// Reads the digital I/O list from Data\Config.csv: a header row, then
    /// <c>_,Type,CardNo,ExCardNo,Id,Name[,Ferrule[,Enabled]]</c> - the layout V3's
    /// DIDO.LoadConfig parsed. Only the columns the I/O screens show are kept.
    /// </summary>
    public static class IoConfigCsvLoader
    {
        public static void Load(string path, List<IOPointSnapshot> inputs, List<IOPointSnapshot> outputs)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);

            for (int i = 1; i < lines.Length; i++)
            {
                var columns = lines[i].Split(',');
                if (columns.Length < 6)
                    continue;

                short id;
                if (!short.TryParse(columns[4], out id))
                    continue;

                var point = new IOPointSnapshot
                {
                    Bit = id,
                    Name = columns[5],
                    Address = columns.Length >= 7 ? columns[6] : string.Empty,
                };

                string type = columns[1].ToUpperInvariant();
                if (type == "INPUT")
                    inputs.Add(point);
                else if (type == "OUTPUT")
                    outputs.Add(point);
            }
        }
    }
}
