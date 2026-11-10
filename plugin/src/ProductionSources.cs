using System;
using System.Collections.Generic;
using System.Linq;

namespace EMMA3.Client
{
    internal static class ProductionSources
    {
        // Build outward from ores the scanner can actually find. Only earlier layers
        // may feed a recipe, preventing recycling loops from becoming its own source.
        public static Dictionary<string, ProductionRecipe> Build(IEnumerable<ProductionRecipe> recipes, HashSet<string> mineable)
        {
            var result = new Dictionary<string, ProductionRecipe>(StringComparer.Ordinal);
            var all = recipes.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
            for (int depth = 0; depth < 24; depth++)
            {
                var next = new Dictionary<string, ProductionRecipe>(StringComparer.Ordinal);
                foreach (var recipe in all)
                {
                    if (!recipe.Inputs.Keys.All(x => mineable.Contains(x) || result.ContainsKey(x)))
                        continue;
                    foreach (string output in recipe.Outputs.Keys)
                        if (!result.ContainsKey(output) && !mineable.Contains(output) && !next.ContainsKey(output))
                            next[output] = recipe;
                }
                if (next.Count == 0)
                    break;
                foreach (var pair in next)
                    result[pair.Key] = pair.Value;
            }
            return result;
        }
    }
}
