using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Feed of the machine's running log — the same stream V3's HomeScreen showed in
    /// its Running Log list, drained from Teal.Library.Log.Logger's queue that every
    /// Logger.WriteLog call across Master.Configuration, Master.Monitor and the
    /// sequences writes into.
    ///
    /// <see cref="LogsAppended"/> is raised on the UI thread so ViewModels can update
    /// bound collections directly.
    /// </summary>
    public interface IRunningLogService
    {
        /// <summary>Newest-first batch of lines drained since the last tick.</summary>
        event EventHandler<IReadOnlyList<string>> LogsAppended;

        void Start();
        void Stop();
    }
}
