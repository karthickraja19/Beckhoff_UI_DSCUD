using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>Registry of OPT lighting controllers supplied by the composition root.</summary>
    public sealed class LightService : ILightService
    {
        private readonly List<ILightController> _controllers;
        private readonly List<LightRecipe> _recipes;

        public LightService(IEnumerable<ILightController> controllers, IEnumerable<LightRecipe> recipes = null)
        {
            _controllers = controllers.ToList();
            _recipes = recipes == null ? new List<LightRecipe>() : recipes.ToList();
        }

        public IReadOnlyList<ILightController> GetControllers() => _controllers;

        public IReadOnlyList<LightRecipe> GetRecipes() => _recipes;
    }
}
