using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;

namespace EMMA3.Client
{
    internal sealed class CarrierProduction : IDisposable
    {
        private const double BatchCycles = 100;
        private readonly Dictionary<string, ProductionRecipe> recipes = new Dictionary<string, ProductionRecipe>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ProductionRecipe>> variants = new Dictionary<string, List<ProductionRecipe>>(StringComparer.Ordinal);
        private readonly Dictionary<string, ProductionRecipe> sources = new Dictionary<string, ProductionRecipe>(StringComparer.Ordinal);
        private readonly Dictionary<long, MyProductionBlock> machines = new Dictionary<long, MyProductionBlock>();
        private readonly Dictionary<long, Pending> pending = new Dictionary<long, Pending>();
        private readonly HashSet<string> mineable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string recipeSignature = "";
        private string selectionSignature = "";
        private string catalogSnapshot;
        private string catalogSelected;
        private ItemCatalog catalog;
        private int nextUpdate;
        private int nextItem;
        public OreSelection NeededOres { get; private set; } = new OreSelection();
        public readonly Dictionary<string, double> RefineryRequired = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, double> RefineryDeficit = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public string Status { get; private set; } = "Off";
        public string Error { get; private set; } = "";
        private sealed class Pending
        {
            public MyProductionBlock Machine;
            public string Blueprint;
            public double SimulationTick;
            public bool Confirmed;
            public double Cycles;
            public ProductionRecipe Recipe;
        }

        public void Update(BlockContext blocks, CarrierResourceTelemetry resources, BridgeDocument bridge, int frame, bool force = false)
        {
            string snapshot = bridge.Get("itemsCatalog");
            string selected = bridge.Get("enabledItems");
            string signature = selected + "\n" + bridge.Get("selectionMode") + bridge.Get("autonomousMining") + bridge.Get("allowMovement");
            bool catalogChanged = catalog == null || snapshot != catalogSnapshot;
            if (!force && !catalogChanged && signature == selectionSignature && frame < nextUpdate)
                return;
            selectionSignature = signature;
            nextUpdate = frame + 300;
            NeededOres = new OreSelection();
            RefineryRequired.Clear();
            RefineryDeficit.Clear();
            Error = "";
            if (catalogChanged)
            {
                catalogSnapshot = snapshot;
                catalogSelected = null;
                catalog = ItemCatalog.FromSnapshot(snapshot);
            }
            if (selected != catalogSelected)
            {
                catalogSelected = selected;
                catalog.ApplySelection(selected);
            }
            if (!bridge.Get("selectionMode").Equals("Items", StringComparison.OrdinalIgnoreCase) || !bridge.GetBool("autonomousMining"))
            {
                Status = "Off; existing queued work is retained";
                return;
            }
            if (catalog.Error.Length > 0)
            {
                Error = catalog.Error;
                Status = Error;
                return;
            }
            var enabled = catalog.Entries.FindAll(x => x.Enabled);
            if (enabled.Count == 0)
            {
                Status = "Select Y items on the carrier LCD";
                return;
            }
            RefreshRecipes(resources.CarrierBlocks);
            foreach (var pair in variants)
                recipes[pair.Key] = AvailableVariant(pair.Key, true) ?? pair.Value[0];
            foreach (string item in sources.Keys.ToArray())
                sources[item] = recipes[sources[item].Id];
            var stock = new Dictionary<string, double>(resources.Stock, StringComparer.Ordinal);
            var physical = new Dictionary<string, double>(resources.Stock, StringComparer.Ordinal);
            var orders = new List<ProductionWork>();
            var queued = new Dictionary<string, double>(StringComparer.Ordinal);
            var required = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in enabled)
            {
                ProductionRecipe root;
                if (!recipes.TryGetValue(entry.Blueprint, out root) || root.Refinery)
                {
                    Error = "No assembler recipe: " + entry.Name;
                    Status = Error;
                    return;
                }
                CollectRequired(root, required);
            }
            foreach (var machine in machines.Values)
            {
                foreach (var job in machine.Queue)
                {
                    if (!(machine is MyAssembler))
                        continue; // Refining is represented by its input ore stock, not counted twice.
                    string id = job.Blueprint.Id.ToString();
                    ProductionRecipe recipe;
                    recipe = RecipeFor(machine.EntityId, id);
                    if (recipe == null)
                        continue;
                    double cycles = (double)job.Amount;
                    AccountQueued(recipe, cycles, required, queued, physical, stock, orders);
                }
            }
            // A sent request is real committed work even before its replicated queue arrives.
            foreach (var request in pending.Values)
                if (!request.Confirmed)
                    AccountQueued(request.Recipe, request.Cycles, required, queued, physical, stock, orders);
            for (int i = 0; i < enabled.Count; i++)
            {
                var entry = enabled[(i + nextItem) % enabled.Count];
                double count;
                queued.TryGetValue(entry.Blueprint, out count);
                var recipe = AvailableVariant(entry.Blueprint, true);
                if (count < BatchCycles && recipe != null)
                    orders.Add(new ProductionWork { Recipe = recipe, Cycles = Math.Min(BatchCycles - count, OutputFits(recipe)) });
            }
            var demand = new ProductionDemand(stock, sources);
            foreach (var order in orders)
                demand.Add(order);
            if (demand.Error.Length > 0)
            {
                Error = demand.Error;
                Status = Error;
                return;
            }
            Copy(demand.RefineryRequired, RefineryRequired);
            Copy(demand.RefineryDeficit, RefineryDeficit);
            foreach (var ore in demand.Ores)
            {
                if (!mineable.Contains("MyObjectBuilder_Ore/" + ore.Key))
                {
                    NeededOres = new OreSelection();
                    Error = "Unmineable ingredient: " + ore.Key + "; supply it";
                    Status = Error;
                    return;
                }
                NeededOres.Targets.Add(OreSelection.Normalize(ore.Key));
                NeededOres.Mineable.Add(OreSelection.Normalize(ore.Key));
            }
            bool writer = IsProductionWriter(blocks, resources.CarrierBlocks);
            int added = 0;
            var overdue = pending.Values.FirstOrDefault(x => !x.Confirmed && EmmaTiming.GameSimulationTime.Ticks - x.SimulationTick >= 1800);
            string pendingProblem = overdue == null ? "" : "Awaiting queue reply: " + overdue.Machine.CustomName;
            foreach (var work in demand.Work)
            {
                var recipe = AvailableVariant(work.Recipe.Id, true);
                if (recipe == null)
                    continue;
                MyProductionBlock machine = machines[recipe.Machine];
                Pending request;
                if (pending.TryGetValue(machine.EntityId, out request))
                {
                    if (!request.Confirmed)
                    {
                        if (EmmaTiming.GameSimulationTime.Ticks - request.SimulationTick >= 1800)
                            pendingProblem = "Awaiting queue reply: " + machine.CustomName;
                        continue;
                    }
                    machine.QueueChanged -= OnQueueChanged;
                    pending.Remove(machine.EntityId);
                }
                if (!writer || !bridge.GetBool("allowMovement"))
                    continue;
                double cycles = ProductionDemand.AffordableCycles(recipe, Math.Min(work.Cycles, OutputFits(recipe)), physical);
                MyBlueprintDefinitionBase blueprint = MyDefinitionManager.Static.GetBlueprintDefinition(MyDefinitionId.Parse(recipe.Id));
                if (cycles <= 0 || blueprint == null || !machine.CanUseBlueprint(blueprint))
                    continue;
                // Register before the request: single-player may acknowledge synchronously.
                pending[machine.EntityId] = new Pending { Machine = machine, Blueprint = recipe.Id, SimulationTick = EmmaTiming.GameSimulationTime.Ticks, Cycles = cycles, Recipe = recipe };
                machine.QueueChanged += OnQueueChanged;
                machine.AddQueueItemRequest(blueprint, (MyFixedPoint)cycles);
                foreach (var input in recipe.Inputs)
                    Deduct(physical, input.Key, input.Value * cycles);
                added++;
            }
            nextItem = (nextItem + 1) % enabled.Count;
            Status = pendingProblem.Length > 0 ? pendingProblem
                : !bridge.GetBool("allowMovement") ? "Preview: movement Off; no assembler requests"
                : !writer ? "Production controlled by another Carrier seat"
                : NeededOres.Targets.Count > 0 ? "Needs ore: " + string.Join(", ", NeededOres.Targets)
                : added > 0 ? "Queued " + added + " production batch(es)"
                : "Waiting for machines, conveyors, or output room";
        }

        private static void AccountQueued(ProductionRecipe recipe, double cycles, HashSet<string> required,
            Dictionary<string, double> queued, Dictionary<string, double> physical, Dictionary<string, double> stock, List<ProductionWork> orders)
        {
            Add(queued, recipe.Id, cycles);
            foreach (var input in recipe.Inputs)
                Deduct(physical, input.Key, input.Value * cycles);
            if (required.Contains(recipe.Id))
                orders.Add(new ProductionWork { Recipe = recipe, Cycles = cycles, AlreadyQueued = true });
            else
                foreach (var input in recipe.Inputs)
                    Deduct(stock, input.Key, input.Value * cycles);
            foreach (var output in recipe.Outputs)
                Add(stock, output.Key, output.Value * cycles);
        }

        private ProductionRecipe RecipeFor(long machine, string id)
        {
            List<ProductionRecipe> choices;
            return variants.TryGetValue(id, out choices) ? choices.Find(x => x.Machine == machine) : null;
        }

        private ProductionRecipe AvailableVariant(string id, bool idle)
        {
            List<ProductionRecipe> choices;
            if (!variants.TryGetValue(id, out choices))
                return null;
            foreach (var recipe in choices)
            {
                var machine = machines[recipe.Machine];
                Pending request;
                if (recipe.Refinery || (idle && machine.Queue.Any()) || OutputFits(recipe) <= 0
                    || (pending.TryGetValue(machine.EntityId, out request) && !request.Confirmed))
                    continue;
                return recipe;
            }
            return null;
        }

        private double OutputFits(ProductionRecipe recipe)
        {
            var blueprint = MyDefinitionManager.Static.GetBlueprintDefinition(MyDefinitionId.Parse(recipe.Id));
            if (blueprint == null)
                return 0;
            double fits = (double)machines[recipe.Machine].OutputInventory.ComputeAmountThatFits(blueprint);
            return recipe.Atomic ? Math.Floor(fits) : fits;
        }

        private void CollectRequired(ProductionRecipe recipe, HashSet<string> result)
        {
            if (!result.Add(recipe.Id) || result.Count > 4096)
                return;
            foreach (string item in recipe.Inputs.Keys)
            {
                ProductionRecipe source;
                if (sources.TryGetValue(item, out source))
                    CollectRequired(source, result);
            }
        }

        private void RefreshRecipes(List<IMyTerminalBlock> blocks)
        {
            var found = new List<MyProductionBlock>();
            var signature = new System.Text.StringBuilder();
            foreach (var block in blocks)
            {
                var machine = block as MyProductionBlock;
                if (machine == null || !machine.Enabled || !machine.IsFunctional
                    || !block.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId))
                    continue;
                var assembler = machine as MyAssembler;
                if (assembler != null && (assembler.DisassembleEnabled || assembler.RepeatEnabled
                    || ((Sandbox.ModAPI.Ingame.IMyAssembler)assembler).CooperativeMode))
                    continue;
                if (assembler == null && !(machine is MyRefinery))
                    continue;
                found.Add(machine);
            }
            found.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
            foreach (var machine in found)
            {
                signature.Append(machine.EntityId).Append(':').Append(machine.BlockDefinition.Id).Append(':');
                var assembler = machine as MyAssembler;
                signature.Append(assembler != null ? assembler.GetEfficiencyMultiplierForBlueprint(null).ToString("R", CultureInfo.InvariantCulture)
                    : machine.UpgradeValues["Effectiveness"].ToString("R", CultureInfo.InvariantCulture));
                signature.Append(';');
            }
            if (signature.ToString() == recipeSignature)
                return;
            recipeSignature = signature.ToString();
            recipes.Clear();
            variants.Clear();
            sources.Clear();
            machines.Clear();
            foreach (var machine in found)
            {
                machines[machine.EntityId] = machine;
                var definition = machine.BlockDefinition as MyProductionBlockDefinition;
                if (definition == null)
                    continue;
                foreach (var group in definition.BlueprintClasses)
                    foreach (var blueprint in group)
                    {
                        // Composite menu entries are not directly executable production recipes.
                        if (blueprint is MyCompositeBlueprintDefinition || blueprint.Prerequisites == null || blueprint.Results == null
                            || blueprint.Prerequisites.Length == 0 || blueprint.Results.Length == 0)
                            continue;
                        string id = blueprint.Id.ToString();
                        var assembler = machine as MyAssembler;
                        var recipe = new ProductionRecipe { Id = id, Machine = machine.EntityId, Refinery = assembler == null, Atomic = blueprint.Atomic };
                        double efficiency = assembler == null ? 1 : assembler.GetEfficiencyMultiplierForBlueprint(blueprint);
                        double yield = assembler == null ? ((MyRefineryDefinition)definition).MaterialEfficiency * machine.UpgradeValues["Effectiveness"] : 1;
                        if (!ProductionDemand.FinitePositive(efficiency) || !ProductionDemand.FinitePositive(yield))
                            continue;
                        foreach (var input in blueprint.Prerequisites)
                            Add(recipe.Inputs, input.Id.ToString(), (double)input.Amount / efficiency);
                        foreach (var output in blueprint.Results)
                            Add(recipe.Outputs, output.Id.ToString(), (double)output.Amount * yield);
                        if (recipe.Inputs.Values.Any(v => !ProductionDemand.FinitePositive(v)) || recipe.Outputs.Values.Any(v => !ProductionDemand.FinitePositive(v)))
                            continue;
                        List<ProductionRecipe> choices;
                        if (!variants.TryGetValue(id, out choices))
                        {
                            variants[id] = choices = new List<ProductionRecipe>();
                            recipes[id] = recipe;
                        }
                        if (!choices.Any(x => x.Machine == machine.EntityId))
                            choices.Add(recipe);
                    }
            }
            mineable.Clear();
            foreach (var material in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
                if (material.CanBeHarvested && material.IsRare && !string.IsNullOrWhiteSpace(material.MinedOre)
                    && !material.MinedOre.Equals("Stone", StringComparison.OrdinalIgnoreCase)
                    && !material.MinedOre.Equals("Rock", StringComparison.OrdinalIgnoreCase)
                    && (material.MaterialTypeName ?? "").IndexOf("Stone", StringComparison.OrdinalIgnoreCase) < 0)
                    mineable.Add("MyObjectBuilder_Ore/" + material.MinedOre);
            foreach (var source in ProductionSources.Build(recipes.Values, mineable))
                sources[source.Key] = source.Value;
        }

        private static bool IsProductionWriter(BlockContext context, List<IMyTerminalBlock> blocks)
        {
            long seat = context.OccupiedController?.EntityId ?? 0;
            if (seat == 0)
                return false;
            foreach (var block in blocks)
            {
                var controller = block as Sandbox.ModAPI.Ingame.IMyShipController;
                if (controller != null && controller.IsUnderControl && controller.EntityId < seat)
                    return false;
            }
            return true;
        }

        private void OnQueueChanged(MyProductionBlock block)
        {
            Pending request;
            if (pending.TryGetValue(block.EntityId, out request) && block.Queue.Any(x => x.Blueprint.Id.ToString() == request.Blueprint))
                request.Confirmed = true;
        }

        private static void Add(Dictionary<string, double> values, string key, double amount)
        {
            double old;
            values.TryGetValue(key, out old);
            values[key] = old + amount;
        }

        private static void Deduct(Dictionary<string, double> values, string key, double amount)
        {
            double old;
            values.TryGetValue(key, out old);
            values[key] = Math.Max(0, old - amount);
        }

        private static void Copy(Dictionary<string, double> source, Dictionary<string, double> destination)
        {
            foreach (var value in source)
                destination[value.Key] = value.Value;
        }

        public void Dispose()
        {
            foreach (var request in pending.Values)
                request.Machine.QueueChanged -= OnQueueChanged;
            pending.Clear();
        }
    }
}
