using System;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Services;
using Nova.Hive;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Asks MES whether the machine's files are still the approved ones every time HIVE
    /// switches to Running (S3.6, flow steps 10 to 13).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>true - production runs (also for "This station has not enabled hash key check").</item>
    /// <item>false - the files changed, the key count differs or MES does not know the
    /// terminal: HIVE goes to downtime with MES's own message as error_detail, and
    /// <see cref="HiveService.ReportFault"/> asks the PLC to stop feeding.</item>
    /// <item>no usable answer - downtime "HIVE/MES upload unsuccessful" (X02) unless
    /// <c>HiveHashCheckOnMesError</c> is "ignore"; the machine log has the reason either way.</item>
    /// <item>no MES address configured - no check, written to the machine log.</item>
    /// </list>
    /// A check that finds a failure leaves HIVE in downtime, so the next switch to Running
    /// is the next check.
    /// </remarks>
    public sealed class HashKeyGuard : IDisposable
    {
        /// <summary>Slide 33: Traceability Error, "Failed to receive information from MES" (X02) on the IPC.</summary>
        public const string MesNoAnswerCode = "X02IPOO-01";

        private readonly HiveService _hive;
        private readonly IHashKeyService _hashes;
        private readonly IMesClient _mes;
        private readonly bool _downtimeOnMesError;
        private int _checking;

        public HashKeyGuard(HiveService hive, IHashKeyService hashes, IMesClient mes, bool downtimeOnMesError)
        {
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            _hashes = hashes ?? throw new ArgumentNullException(nameof(hashes));
            _mes = mes ?? throw new ArgumentNullException(nameof(mes));
            _downtimeOnMesError = downtimeOnMesError;
        }

        public void Start()
        {
            _hive.StateChanged += OnStateChanged;
        }

        public void Dispose()
        {
            _hive.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(object sender, int state)
        {
            if (state != HiveStateMachine.Running)
                return;

            // Never on the thread that changed the state: MES can take seconds.
            Task.Run(() => CheckAsync());
        }

        /// <summary>One check; returns true when production may go on.</summary>
        public async Task<bool> CheckAsync()
        {
            if (!_mes.IsConfigured)
            {
                Log(LogType.Info, "MES is not configured: the hash key check on Running was not made.");
                return true;
            }

            if (Interlocked.Exchange(ref _checking, 1) == 1)
                return true;

            try
            {
                MesResult result = await _hashes.CheckWithMesAsync().ConfigureAwait(false);

                if (result.Reached && result.Ok)
                {
                    Log(LogType.Info, "Hash key check with MES passed: " + result.Message);
                    return true;
                }

                if (result.Reached)
                {
                    // Result:false - "NG;The Hash key alteration of this station has been detected..."
                    _hive.ReportFault(HiveErrorCodes.Others("Hash key check with MES failed: " + result.Message));
                    return false;
                }

                Log(LogType.Error, "[Error/Alert] The hash key check with MES got no usable answer: " + result.Message);
                if (_downtimeOnMesError)
                {
                    _hive.ReportFault(new HiveError
                    {
                        Code = MesNoAnswerCode,
                        Message = "Traceability Error",
                        Detail = "Failed to receive information from MES: the hash key check was not answered (" + result.Message + ")",
                        Severity = "error",
                        StopsMachine = true,
                    });
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log(LogType.Error, "The hash key check with MES failed: " + ex.Message);
                return true;
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        }

        private static void Log(LogType type, string message)
        {
            Logger.WriteLog(type, "Hive", "HashCheck", message, string.Empty);
        }
    }
}
