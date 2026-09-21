using System.Collections.Generic;
using Nova.Abstractions.Data.Models;

namespace Nova.Abstractions.Services
{
    public interface IRecipeService
    {
        event System.EventHandler RecipeChanged;

        IReadOnlyList<string> GetGantryModules();
        GantryModuleDefinition GetGantryModuleDefinition(string moduleName);
        IReadOnlyDictionary<string, double> GetAxisSpeedLimits();

        IReadOnlyList<GantryPositionRecord> LoadGantryRecipe(string moduleName);
        DynamicTableDocument LoadGantryRecipeDynamic(string moduleName);
        IReadOnlyList<RobotPointRecord> LoadRobotRecipe();
        DynamicTableDocument LoadRobotRecipeDynamic();
        IReadOnlyList<LightChannelRecord> LoadLightRecipe();
        DynamicTableDocument LoadLightRecipeDynamic();

        RecipeSaveResult SaveGantryRecipe(string moduleName, IReadOnlyList<GantryPositionRecord> rows);
        RecipeSaveResult SaveGantryRecipeDynamic(string moduleName, DynamicTableDocument document);
        RecipeSaveResult SaveRobotRecipe(IReadOnlyList<RobotPointRecord> rows);
        RecipeSaveResult SaveRobotRecipeDynamic(DynamicTableDocument document);
        RecipeSaveResult SaveLightRecipe(IReadOnlyList<LightChannelRecord> rows);
        RecipeSaveResult SaveLightRecipeDynamic(DynamicTableDocument document);
    }
}
