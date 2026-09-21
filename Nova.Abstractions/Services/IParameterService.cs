using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// A generic service for retrieving configuration parameters for any hardware 
    /// component, actuator, or software module in the machine.
    /// </summary>
    public interface IParameterService
    {
        /// <summary>
        /// Retrieves all parameters for a specific component as a Dictionary.
        /// Useful when a hardware vendor needs to extract multiple unknown parameters (e.g. Velocity, Jerk, ControlWord).
        /// </summary>
        /// <param name="componentId">The unique string identifier of the component (e.g. "Axis_X", "Vacuum_Pick").</param>
        /// <returns>A dictionary of parameter key-value pairs.</returns>
        Dictionary<string, object> GetParameters(string componentId);

        /// <summary>
        /// Retrieves a single specific parameter for a component, with a safe fallback default value.
        /// </summary>
        /// <typeparam name="T">The expected type of the parameter.</typeparam>
        /// <param name="componentId">The unique string identifier of the component.</param>
        /// <param name="parameterName">The exact name of the parameter to fetch.</param>
        /// <param name="defaultValue">The value to return if the parameter doesn't exist.</param>
        /// <returns>The parameter value.</returns>
        T GetParameter<T>(string componentId, string parameterName, T defaultValue = default);

        /// <summary>
        /// Sets a volatile runtime override for a parameter. This is stored in high-speed RAM
        /// and is NOT persisted to disk/DB. Useful for mid-sequence dynamic computations (e.g. dynamic velocity).
        /// </summary>
        /// <param name="componentId">The unique string identifier of the component.</param>
        /// <param name="parameterName">The exact name of the parameter to override.</param>
        /// <param name="value">The computed value.</param>
        void SetRuntimeOverride(string componentId, string parameterName, object value);

        /// <summary>
        /// Retrieves a runtime override if it exists, otherwise falls back to a provided default.
        /// </summary>
        /// <typeparam name="T">The expected type of the parameter.</typeparam>
        /// <param name="componentId">The unique string identifier of the component.</param>
        /// <param name="parameterName">The exact name of the parameter.</param>
        /// <param name="fallback">The value to return if the override doesn't exist.</param>
        /// <returns>The parameter value.</returns>
        T GetRuntimeOverride<T>(string componentId, string parameterName, T fallback = default);
    }
}
