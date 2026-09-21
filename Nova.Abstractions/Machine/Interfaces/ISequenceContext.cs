using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Abstractions.Machine.Interfaces
{
    public interface ISequenceContext
    {
        CancellationToken Token { get; }
        
        /// <summary>
        /// Sequences should await this at the start of their loop.
        /// It will block asynchronously if the machine is Paused, and return immediately if Running.
        /// </summary>
        Task WaitIfPausedAsync();
        
        /// <summary>
        /// Used by the orchestrator to signal a pause.
        /// </summary>
        void Pause();
        
        /// <summary>
        /// Used by the orchestrator to signal resume.
        /// </summary>
        void Resume();
    }
}
