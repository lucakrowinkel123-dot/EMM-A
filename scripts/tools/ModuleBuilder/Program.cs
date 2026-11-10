using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

// Offline only. Emit explicit role sources from reviewed entry points/adapters and
// the existing navigation/mission library. Never transmit generated code at runtime.
if (args.Length != 2) throw new Exception("Usage: ModuleBuilder <scripts directory> <CarrierCommander|CarrierPilot|MinerCommander|MinerPilot>");
string root = Path.GetFullPath(args[0]), role = args[1];
string ship = role.StartsWith("Carrier") ? "Carrier" : "Miner";
var manifest = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(Path.Combine(root, "modules.json")));
var baseline = manifest[ship].SelectMany(f => ReadMembers(File.ReadAllText(Path.Combine(root, f)))).ToList();
if (role.EndsWith("Inventory", StringComparison.Ordinal))
{
    Directory.CreateDirectory(Path.Combine(root, "generated"));
    File.WriteAllText(Path.Combine(root, "generated", ship + ".library.json"), JsonSerializer.Serialize(baseline.Select(m => new { name = Name(m), source = m.ToFullString() }), new JsonSerializerOptions { WriteIndented = true }));
    return;
}
var adapters = Directory.GetFiles(Path.Combine(root, "src", "Modules", role), "*.cs").OrderBy(f => f, StringComparer.Ordinal)
    .SelectMany(f => ReadMembers(File.ReadAllText(f))).ToList();
adapters.AddRange(ReadMembers(File.ReadAllText(Path.Combine(root, "src", "Modules", "OperationJournal.cs"))));
adapters.AddRange(Directory.GetFiles(Path.Combine(root, "src", "Modules", "Shared"), "*.cs").OrderBy(f => f, StringComparer.Ordinal).SelectMany(f => ReadMembers(File.ReadAllText(f))));
var replaced = new HashSet<string>(adapters.Select(Name), StringComparer.Ordinal);
var members = baseline.Where(m => !replaced.Contains(Name(m))).Concat(adapters).ToList();
// Identifier reachability is intentionally conservative: overloads and nested
// helpers stay together. Every emitted result is compiled against game DLLs.
var byName = members.GroupBy(Name).ToDictionary(g => g.Key, g => g.ToList());
var keep = new HashSet<string>(StringComparer.Ordinal);
var pending = new Queue<string>(new[] { "Program", "Main", "Save" });
while (pending.Count > 0)
{
    string name = pending.Dequeue();
    if (!keep.Add(name) || !byName.ContainsKey(name)) continue;
    foreach (var member in byName[name])
    {
        var localNames = member is ClassDeclarationSyntax nested
            ? nested.Members.SelectMany(m => m is FieldDeclarationSyntax f ? f.Declaration.Variables.Select(v => v.Identifier.ValueText) : new[] { Name(m) }).ToHashSet()
            : new HashSet<string>();
        foreach (var id in member.DescendantNodes().OfType<SimpleNameSyntax>())
            if (byName.ContainsKey(id.Identifier.ValueText) && (!localNames.Contains(id.Identifier.ValueText)
                || id.Parent is MemberAccessExpressionSyntax access && access.Expression.ToString() == "uiHost")) pending.Enqueue(id.Identifier.ValueText);
    }
}
var chosen = members.Where(m => keep.Contains(Name(m))).ToList();
string output = "// EMM-A 0.8.56 " + role + " -- generated from scripts/src; edit the modules.\n" + string.Join("\n", chosen.Select(m => m.ToFullString()));
Directory.CreateDirectory(Path.Combine(root, "generated"));
File.WriteAllText(Path.Combine(root, "generated", role + ".cs"), output, new System.Text.UTF8Encoding(false));
File.WriteAllText(Path.Combine(root, "generated", role + ".members.json"), JsonSerializer.Serialize(chosen.Select(m => new { name = Name(m), characters = m.ToFullString().Length }), new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(role + ": " + chosen.Count + "/" + members.Count + " members, " + output.Length + " source characters");

static IEnumerable<MemberDeclarationSyntax> ReadMembers(string source)
{
    var tree = CSharpSyntaxTree.ParseText("class Program {\n" + source + "\n}");
    var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(e => e.ToString())));
    foreach (var member in ((ClassDeclarationSyntax)tree.GetCompilationUnitRoot().Members[0]).Members)
    {
        if (member is FieldDeclarationSyntax field && field.Declaration.Variables.Count > 1)
        {
            foreach (var variable in field.Declaration.Variables)
                yield return field.WithDeclaration(field.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(variable)));
        }
        else yield return member;
    }
}

static string Name(MemberDeclarationSyntax member)
{
    if (member is MethodDeclarationSyntax method) return method.Identifier.ValueText;
    if (member is ConstructorDeclarationSyntax ctor) return ctor.Identifier.ValueText;
    if (member is BaseTypeDeclarationSyntax type) return type.Identifier.ValueText;
    if (member is FieldDeclarationSyntax field) return field.Declaration.Variables[0].Identifier.ValueText;
    if (member is PropertyDeclarationSyntax property) return property.Identifier.ValueText;
    throw new Exception("Unsupported declaration: " + member.Kind());
}
