using System;
using System.Collections.Generic;
using System.Text;
using TwinCAT.Ads;

namespace Nova.Alarm
{
    /// <summary>
    /// Reads the PLC's alarm list over ADS: GVL_Alarm.fbAlarmManager.aAlarms, which is an
    /// ARRAY[1..MAX_ALARMS] OF ST_Alarms.
    /// </summary>
    /// <remarks>
    /// The whole array is read in one go as raw bytes, then cut into entries using the
    /// layout below. The layout is ST_Alarms as the PLC compiles it (see alarm.tmc in the
    /// PLC project). If ST_Alarms changes in the PLC, update the numbers below to match.
    /// </remarks>
    public sealed class PlcAlarmReader : IDisposable
    {
        public const string AlarmListSymbol = "GVL_Alarm.fbAlarmManager.aAlarms";

        // ---- ST_Alarms layout: where each field starts inside one entry ----
        // A PLC STRING[n] takes n + 1 bytes: the text plus a closing zero byte.
        private const int EntrySize = 180;
        private const int StationNameOffset = 0;          // STRING[30]
        private const int ComponentNameOffset = 31;       // STRING[30]
        private const int ErrorCodeOffset = 62;           // STRING[30]
        private const int ErrorTypeOffset = 93;           // STRING[30]
        private const int ErrorDescriptionOffset = 124;   // STRING[50]
        private const int LevelOffset = 176;              // INT (byte 175 is padding)
        private const int ModuleNoOffset = 178;           // INT

        // A TwinCAT STRING is one byte per character, Windows-1252.
        private static readonly Encoding PlcText = Encoding.GetEncoding(1252);

        private readonly string _amsNetId;
        private readonly int _port;
        private readonly AdsClient _client = new AdsClient();

        // How many entries the PLC array has. 0 = not read from the PLC yet.
        private int _entryCount;

        public PlcAlarmReader(string amsNetId, int port)
        {
            _amsNetId = amsNetId;
            _port = port;
        }

        /// <summary>
        /// Returns the alarms currently in the PLC's list (empty slots left out).
        /// Throws when the PLC cannot be read.
        /// </summary>
        public List<PlcAlarm> ReadAlarms()
        {
            if (!_client.IsConnected)
                _client.Connect(_amsNetId, _port);

            if (_entryCount == 0)
                _entryCount = ReadEntryCount();

            byte[] data;
            try
            {
                data = (byte[])_client.ReadAny(AlarmListSymbol, typeof(byte[]), new[] { _entryCount * EntrySize });
            }
            catch
            {
                // The PLC program may have been downloaded again; check the array size next time.
                _entryCount = 0;
                throw;
            }

            var alarms = new List<PlcAlarm>();
            for (int i = 0; i < _entryCount; i++)
            {
                PlcAlarm alarm = ReadEntry(data, i * EntrySize);
                if (!alarm.IsEmpty)
                    alarms.Add(alarm);
            }

            return alarms;
        }

        /// <summary>
        /// Number of entries in the PLC array (MAX_ALARMS), worked out from the array's size.
        /// Also catches the common mistake of ST_Alarms being changed in the PLC but not here.
        /// </summary>
        private int ReadEntryCount()
        {
            int arraySize = _client.ReadSymbol(AlarmListSymbol).ByteSize;

            if (arraySize == 0 || arraySize % EntrySize != 0)
            {
                throw new InvalidOperationException(
                    AlarmListSymbol + " is " + arraySize + " bytes, which is not a whole number of "
                    + EntrySize + "-byte ST_Alarms entries. ST_Alarms was changed in the PLC - "
                    + "update the layout in PlcAlarmReader to match.");
            }

            return arraySize / EntrySize;
        }

        private static PlcAlarm ReadEntry(byte[] data, int start)
        {
            return new PlcAlarm
            {
                StationName = ReadString(data, start + StationNameOffset, 31),
                ComponentName = ReadString(data, start + ComponentNameOffset, 31),
                ErrorCode = ReadString(data, start + ErrorCodeOffset, 31),
                ErrorType = ReadString(data, start + ErrorTypeOffset, 31),
                ErrorDescription = ReadString(data, start + ErrorDescriptionOffset, 51),
                Level = BitConverter.ToInt16(data, start + LevelOffset),
                ModuleNo = BitConverter.ToInt16(data, start + ModuleNoOffset),
            };
        }

        /// <summary>Reads a PLC STRING: the text runs up to the first zero byte.</summary>
        private static string ReadString(byte[] data, int start, int size)
        {
            int length = 0;
            while (length < size && data[start + length] != 0)
                length++;

            return PlcText.GetString(data, start, length);
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
