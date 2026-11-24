using System.Collections.Generic;
using Nova.Abstractions.Data.Models;

namespace Nova.Abstractions.Services
{
    public interface IRecipeService
    {
        event System.EventHandler RecipeChanged;

        /// <summary>The gantry modules of Data\Gantry.json.</summary>
        IReadOnlyList<string> GetGantryModules();
        GantryModuleDefinition GetGantryModuleDefinition(string moduleName);
        IReadOnlyDictionary<string, double> GetAxisSpeedLimits();

        /// <summary>
        /// A module's positions, as V3 builds them: every position name taught on any of its
        /// servos (Servos.json ConfiguredPositions), in first-seen order, with each servo's move.
        /// </summary>
        IReadOnlyList<GantryPosition> LoadGantryPositions(string moduleName);

        /// <summary>
        /// The same positions as the Gantry tab's table: Position, then per axis
        /// {servo}_Value, {servo}_Speed, {servo}_Acc and {servo}_Dec (see <see cref="GantryRecipeColumns"/>).
        /// </summary>
        DynamicTableDocument LoadGantryRecipeDynamic(string moduleName);
        IReadOnlyList<RobotPointRecord> LoadRobotRecipe();
        DynamicTableDocument LoadRobotRecipeDynamic();

        /// <summary>Writes the table back into the servos' ConfiguredPositions in Servos.json.</summary>
        RecipeSaveResult SaveGantryRecipeDynamic(string moduleName, DynamicTableDocument document);
        RecipeSaveResult SaveRobotRecipe(IReadOnlyList<RobotPointRecord> rows);
        RecipeSaveResult SaveRobotRecipeDynamic(DynamicTableDocument document);

        /// <summary>The CNC channels and their taught positions; empty when there is no CNC recipe.</summary>
        IReadOnlyList<CncChannelRecipe> LoadCncRecipe();
        RecipeSaveResult SaveCncRecipe(IReadOnlyList<CncChannelRecipe> channels);
    }
}
