using System.Threading.Tasks;
using Nova.Abstractions.Hardware.Models;

namespace Nova.Abstractions.Hardware.Interfaces
{
    public interface IRobotController
    {
        bool IsConnected { get; }
        
        /// <summary>
        /// Connects to the robot controller via the underlying protocol.
        /// </summary>
        Task<bool> ConnectAsync(string ipAddress, int port);
        
        /// <summary>
        /// Disconnects from the robot controller.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Sets the operational mode of the robot (e.g., Auto, Calibration).
        /// </summary>
        Task<bool> SetModeAsync(RobotMode mode);

        /// <summary>
        /// Executes a specific pre-programmed sequence on the robot.
        /// </summary>
        Task<RobotResponse> ExecuteSequenceAsync(string sequenceName, params object[] parameters);

        /// <summary>
        /// Sends a raw string command to the robot and waits for a response. Useful for generic or custom commands.
        /// </summary>
        Task<string> SendRawCommandAsync(string command, int timeoutMs = 10000);
    }
}
