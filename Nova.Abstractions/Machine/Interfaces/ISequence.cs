using System.Threading.Tasks;

namespace Nova.Abstractions.Machine.Interfaces
{
    public interface ISequence
    {
        string Name { get; }
        
        /// <summary>
        /// Current step the sequence is executing.
        /// </summary>
        int CurrentStep { get; }

        /// <summary>
        /// Executes the sequence continuously until the context's cancellation token is triggered.
        /// </summary>
        Task ExecuteAsync(ISequenceContext context);
        
        /// <summary>
        /// Resets the sequence to its initial state.
        /// </summary>
        void Reset();
    }
}
