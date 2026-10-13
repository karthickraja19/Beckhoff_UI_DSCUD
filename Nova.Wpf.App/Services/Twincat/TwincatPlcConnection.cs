using System;
using Nova.Alarm;
using Teal.Library.Log;
using Teal.Library.Twincat;
using TwinCAT.Ads;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// The one ADS connection to the PLC runtime, shared by every TwinCAT device adapter.
    /// Wraps the Beckhoff library's <see cref="TwincatCard"/> and serialises access to it:
    /// the monitor thread polls status while command buttons write from the thread pool,
    /// and each library call is a state check plus a read or write that should not
    /// interleave with another. The alarm and HIVE readers use it too, through
    /// <see cref="IPlcConnection"/>, so the HMI has one ADS connection to the PLC.
    /// </summary>
    public sealed class TwincatPlcConnection : IPlcConnection, IDisposable
    {
        private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);

        private DateTime _nextConnectAttemptUtc;

        /// <param name="card">
        /// The card Master.Configuration builds from Data\CardsConfiguration.json: the PLC's
        /// AMS Net ID and port, and the GVL and array the steppers live in.
        /// </param>
        public TwincatPlcConnection(TwincatCard card)
        {
            Card = card ?? throw new ArgumentNullException(nameof(card));
            AmsNetId = card.ConnectionId;
            Port = card.PortNo;
        }

        public TwincatCard Card { get; }

        /// <summary>The library's ADS client. It is created once with the card and reconnected in place, so notification handlers attached to it survive a reconnect.</summary>
        public AdsClient Client => Card.Client;

        /// <summary>Held around every library call made through this connection.</summary>
        public object SyncRoot { get; } = new object();

        public string AmsNetId { get; }

        public int Port { get; }

        public string Address => AmsNetId + ":" + Port;

        /// <summary>
        /// Why the last <see cref="Connect"/> failed, or null after a success. The library
        /// logs only "Exception Error" for a failed connect, so the reason is captured here.
        /// </summary>
        public string LastError { get; private set; }

        /// <summary>Opens the ADS connection. True when the PLC answers and is in Run.</summary>
        public bool Connect()
        {
            lock (SyncRoot)
            {
                _nextConnectAttemptUtc = DateTime.UtcNow + ReconnectInterval;
                if (Card.ConnectCard())
                {
                    LastError = null;
                    return true;
                }

                LastError = DescribeFailure();
                Logger.WriteLog(LogType.Error, "PLC", Address, "ADS connect to " + Address + " failed: " + LastError, LastError);
                return false;
            }
        }

        private string DescribeFailure()
        {
            try
            {
                var state = Card.Client.ReadState();
                return "the PLC is in " + state.AdsState + ", not Run";
            }
            catch (Exception ex)
            {
                while (ex.InnerException != null)
                    ex = ex.InnerException;
                return ex.Message;
            }
        }

        /// <summary>
        /// True when the PLC answers and is in Run. If the ADS client never connected (no
        /// router, bad Net ID at startup) it retries every few seconds, so the steppers come
        /// alive once the runtime does without restarting the HMI.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (SyncRoot)
                {
                    if (!Card.Client.IsConnected && DateTime.UtcNow >= _nextConnectAttemptUtc)
                    {
                        _nextConnectAttemptUtc = DateTime.UtcNow + ReconnectInterval;
                        Card.ConnectCard();
                    }

                    return Card.IsConnected;
                }
            }
        }

        /// <summary>
        /// True when the PLC declares the symbol. Uses the ADS client directly because the
        /// library's Read swallows a missing symbol and returns the default value.
        /// </summary>
        public bool SymbolExists(string symbolPath)
        {
            lock (SyncRoot)
            {
                try
                {
                    Card.Client.ReadValue(symbolPath);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The size in bytes the PLC declares the symbol with, or null when it does not declare it.</summary>
        public int? SymbolSize(string symbolPath)
        {
            lock (SyncRoot)
            {
                try
                {
                    return Card.Client.ReadSymbol(symbolPath).ByteSize;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Writes one PLC variable by name - for the few HMI-owned values the PLC reads
        /// (e.g. the picture check's dispensing control). Throws when the write fails.
        /// </summary>
        public void WriteSymbol(string symbolPath, object value)
        {
            lock (SyncRoot)
            {
                Card.Client.WriteValue(symbolPath, value);
            }
        }

        /// <summary>
        /// Writes a whole PLC variable - an array or struct - as its raw bytes, in one ADS
        /// write. <paramref name="data"/> is laid out as the PLC lays the variable out, and is
        /// as long as it (<see cref="SymbolSize"/>). Throws when the write fails.
        /// </summary>
        public void WriteBytes(string symbolPath, byte[] data)
        {
            lock (SyncRoot)
            {
                uint handle = Card.Client.CreateVariableHandle(symbolPath);
                try
                {
                    Card.Client.Write(handle, new ReadOnlyMemory<byte>(data));
                }
                finally
                {
                    Card.Client.DeleteVariableHandle(handle);
                }
            }
        }

        public void Dispose()
        {
            lock (SyncRoot)
            {
                try
                {
                    Card.Client.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("ADS client dispose failed: " + ex.Message);
                }
            }
        }
    }
}
