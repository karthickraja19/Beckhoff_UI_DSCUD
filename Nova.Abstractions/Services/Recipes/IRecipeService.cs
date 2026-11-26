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
        /// A gantry's positions, from its recipe file in the data folder ({Name}.xlsx, or
        /// {Name}.csv): one per row in the file's order, with each servo's move. Throws when
        /// the gantry has no readable file.
        /// </summary>
        IReadOnlyList<GantryPosition> LoadGantryPositions(string moduleName);

        /// <summary>
        /// The same positions as the Gantry tab's table: Position, then per axis
        /// {servo}_Value, {servo}_Speed, {servo}_Acc and {servo}_Dec (see <see cref="GantryRecipeColumns"/>).
        /// </summary>
        DynamicTableDocument LoadGantryRecipeDynamic(string moduleName);
        IReadOnlyList<RobotPointRecord> LoadRobotRecipe();
        DynamicTableDocument LoadRobotRecipeDynamic();

        /// <summary>Writes the table back into the gantry's recipe file ({Name}.xlsx or {Name}.csv).</summary>
        RecipeSaveResult SaveGantryRecipeDynamic(string moduleName, DynamicTableDocument document);
        RecipeSaveResult SaveRobotRecipe(IReadOnlyList<RobotPointRecord> rows);
        RecipeSaveResult SaveRobotRecipeDynamic(DynamicTableDocument document);

        /// <summary>The CNC channels and their taught positions; empty when there is no CNC recipe.</summary>
        IReadOnlyList<CncChannelRecipe> LoadCncRecipe();
        RecipeSaveResult SaveCncRecipe(IReadOnlyList<CncChannelRecipe> channels);
    }
}
