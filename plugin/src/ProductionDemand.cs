using System;
using System.Collections.Generic;

namespace EMMA3.Client
{
    // Pure quantity accounting; no game objects are accessed during this pass.
    internal sealed class ProductionRecipe
    {
        public string Id;
        public long Machine;
        public bool Refinery;
        public bool Atomic;
        public readonly Dictionary<string, double> Inputs = new Dictionary<string, double>(StringComparer.Ordinal);
        public readonly Dictionary<string, double> Outputs = new Dictionary<string, double>(StringComparer.Ordinal);
    }

    internal sealed class ProductionWork
    {
        public ProductionRecipe Recipe;
        public double Cycles;
        public bool AlreadyQueued;
    }

    internal sealed class ProductionDemand
    {
        public readonly Dictionary<string, double> Ores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, double> RefineryRequired = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, double> RefineryDeficit = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public readonly List<ProductionWork> Work = new List<ProductionWork>();
        public string Error = "";
        private readonly Dictionary<string, double> available;
        private readonly Dictionary<string, ProductionRecipe> recipes;
        private readonly HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
        private int visits;

        public ProductionDemand(Dictionary<string, double> stock, Dictionary<string, ProductionRecipe> sources)
        {
            available = new Dictionary<string, double>(stock, StringComparer.Ordinal);
            recipes = sources;
        }

        public void Add(ProductionWork order)
        {
            if (Error.Length > 0)
                return;
            RequireInputs(order.Recipe, order.Cycles, 0);
            if (!order.AlreadyQueued && !order.Recipe.Refinery)
                Work.Add(order);
        }

        private void RequireInputs(ProductionRecipe recipe, double cycles, int depth)
        {
            if (depth > 24 || ++visits > 4096 || !FinitePositive(cycles) || cycles > 1e9 || !visiting.Add(recipe.Id))
            {
                Error = "cyclic/excessive production chain: " + recipe.Id;
                return;
            }
            foreach (var input in recipe.Inputs)
            {
                Require(input.Key, input.Value * cycles, depth + 1);
                if (Error.Length > 0)
                    break;
            }
            visiting.Remove(recipe.Id);
        }

        private void Require(string item, double amount, int depth)
        {
            ProductionRecipe source;
            double sourceOutput = 0;
            bool hasSource = recipes.TryGetValue(item, out source)
                && source.Outputs.TryGetValue(item, out sourceOutput) && FinitePositive(sourceOutput);
            if (hasSource && source.Refinery)
                RecordRefineryOre(source, amount / sourceOutput, RefineryRequired);
            double have;
            available.TryGetValue(item, out have);
            double use = Math.Min(Math.Max(0, have), amount);
            available[item] = have - use;
            double missing = amount - use;
            if (missing <= 0.000001)
                return;
            const string orePrefix = "MyObjectBuilder_Ore/";
            if (item.StartsWith(orePrefix, StringComparison.Ordinal))
            {
                string ore = item.Substring(orePrefix.Length);
                double prior;
                Ores.TryGetValue(ore, out prior);
                Ores[ore] = prior + missing;
                return;
            }
            if (!hasSource)
            {
                Error = "No onboard recipe: " + item;
                return;
            }
            double cycles = missing / sourceOutput;
            if (source.Atomic)
                cycles = Math.Ceiling(cycles);
            if (source.Refinery)
                RecordRefineryOre(source, cycles, RefineryDeficit);
            RequireInputs(source, cycles, depth);
            if (Error.Length > 0)
                return;
            if (!source.Refinery)
                Work.Add(new ProductionWork { Recipe = source, Cycles = cycles });
            foreach (var result in source.Outputs)
            {
                available.TryGetValue(result.Key, out have);
                available[result.Key] = have + result.Value * cycles;
            }
            available[item] -= missing;
        }

        private static void RecordRefineryOre(ProductionRecipe recipe, double cycles, Dictionary<string, double> result)
        {
            const string orePrefix = "MyObjectBuilder_Ore/";
            foreach (var input in recipe.Inputs)
            {
                if (!input.Key.StartsWith(orePrefix, StringComparison.Ordinal))
                    continue;
                string ore = input.Key.Substring(orePrefix.Length);
                double prior;
                result.TryGetValue(ore, out prior);
                result[ore] = prior + input.Value * cycles;
            }
        }

        public static bool FinitePositive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static double AffordableCycles(ProductionRecipe recipe, double wanted, Dictionary<string, double> stock)
        {
            double result = Math.Min(100, wanted);
            foreach (var input in recipe.Inputs)
            {
                double amount;
                stock.TryGetValue(input.Key, out amount);
                result = Math.Min(result, Math.Max(0, amount) / input.Value);
            }
            return recipe.Atomic ? Math.Floor(result + 1e-8) : result;
        }
    }
}
