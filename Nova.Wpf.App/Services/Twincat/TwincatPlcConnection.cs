using System;
using Teal.Library.Contracts.Models;
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
    /// interleave with another.
    /// </summary>
    public sealed class TwincatPlcConnection : IDisposable
    {
        private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);

        private DateTime _nextConnectAttemptUtc;

        public TwincatPlcConnection(string amsNetId, int port, string stepperGvl, string stepperArray)
        {
            AmsNetId = amsNetId;
            Port = port;
            Card = new TwincatCard(new CardProperties
            {
                ConnectionId = amsNetId,
                PortNo = port,
                DBName = stepperGvl,
                StepperDUTName = stepperArray,
            });
        }

        public TwincatCard Card { get; }

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

        /// <summary>Symbol of one stepper's ST_STEPPER, built the way the library builds it.</summary>
        public string StepperInstance(int id)
        {
            return Card.DBName + "." + Card.StepperDUTName + "[" + id + "]";
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
