using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// A serial OPT lighting controller. Apply writes channel intensities (the V3
    /// Serial_Communication.Write_OPT path); a comma-separated "R,G,B,I" string
    /// mirrors the light-recipe payloads the Manual tab already uses.
    /// </summary>
    public interface ILightController
    {
        string Name { get; }
        bool IsConnected { get; }

        Task<bool> ConnectAsync();
        Task<bool> ApplyAsync(string channelValues);
        void Disconnect();
    }

    /// <summary>
    /// One colour recipe from Data\LightControl_Param.csv: the colour code and its
    /// per-channel intensities, as V3 loads into UIGlobalVariable.lightcontrol_recipe.
    /// </summary>
    public sealed class LightRecipe
    {
        public string Name { get; set; }
        public IReadOnlyList<int> Values { get; set; }

        /// <summary>Channel values as the comma-separated payload ApplyAsync takes.</summary>
        public string ChannelValues =>
            Values == null ? string.Empty : string.Join(", ", Values);
    }

    /// <summary>Registry of OPT lighting controllers (config-driven from the COM-port list).</summary>
    public interface ILightService
    {
        /// <summary>The light objects themselves — V3's cmbCOMPort list.</summary>
        IReadOnlyList<ILightController> GetControllers();

        /// <summary>Colour recipes from LightControl_Param.csv — V3's cmbOPTSend list.</summary>
        IReadOnlyList<LightRecipe> GetRecipes();
    }
}
