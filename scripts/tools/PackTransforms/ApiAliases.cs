using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class ApiAliases
{
    public static void Run(string input, string output)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(input)).GetRoot();
        var constants = new Dictionary<string, string> {
            {"Vector3D.Zero", "PackedZeroVector"},
            {"MyShipConnectorStatus.Connected", "PackedConnected"},
            {"StringComparison.OrdinalIgnoreCase", "PackedIgnoreCase"},
            {"StringComparison.Ordinal", "PackedExactCase"},
            {"System.StringComparison.OrdinalIgnoreCase", "PackedIgnoreCase"},
            {"System.StringComparison.Ordinal", "PackedExactCase"},
            {"System.Globalization.CultureInfo.InvariantCulture", "PackedInvariantNumbers"},
            {"CultureInfo.InvariantCulture", "PackedInvariantNumbers"}
        };
        var calls = new Dictionary<string, (string Name, int Arity)> {
            {"Vector3D.Normalize", ("PackedNormalize", 1)},
            {"Vector3D.Dot", ("PackedDot", 2)},
            {"string.IsNullOrWhiteSpace", ("PackedWhiteSpace", 1)}
        };
        var reserved = constants.Values.Concat(calls.Values.Select(v => v.Name)).ToHashSet();
        if (root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Any(v => reserved.Contains(v.Identifier.ValueText))
            || root.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(v => reserved.Contains(v.Identifier.ValueText)))
            throw new InvalidOperationException("API aliases already present or helper name collision.");
        // Only the zero vector (whose default struct value is zero) and enum consts
        // may be used before the generated fields' initialization order reaches them.
        var nodes = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Where(n =>
            constants.ContainsKey(n.ToString()) && (n.ToString() == "Vector3D.Zero" || n.ToString() == "MyShipConnectorStatus.Connected"
                || !n.Ancestors().Any(a => a is FieldDeclarationSyntax))).ToArray();
        root = root.ReplaceNodes(nodes, (n, _) => SyntaxFactory.IdentifierName(constants[n.ToString()]).WithTriviaFrom(n));
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(n =>
            calls.TryGetValue(n.Expression.ToString(), out var call) && n.ArgumentList.Arguments.Count == call.Arity
            && n.ArgumentList.Arguments.All(a => a.RefKindKeyword.IsKind(SyntaxKind.None))).ToArray();
        root = root.ReplaceNodes(invocations, (n, rewritten) => rewritten.WithExpression(SyntaxFactory.IdentifierName(calls[n.Expression.ToString()].Name)));
        string helpers = """
static readonly Vector3D PackedZeroVector=Vector3D.Zero;
const MyShipConnectorStatus PackedConnected=MyShipConnectorStatus.Connected;
const StringComparison PackedIgnoreCase=StringComparison.OrdinalIgnoreCase,PackedExactCase=StringComparison.Ordinal;
static readonly System.Globalization.CultureInfo PackedInvariantNumbers=System.Globalization.CultureInfo.InvariantCulture;
static Vector3D PackedNormalize(Vector3D value){return Vector3D.Normalize(value);}
static double PackedDot(Vector3D left,Vector3D right){return Vector3D.Dot(left,right);}
static bool PackedWhiteSpace(string value){return string.IsNullOrWhiteSpace(value);}
""";
        var program = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c => c.Identifier.ValueText == "Program");
        var members = CSharpSyntaxTree.ParseText("class X{" + helpers + "}").GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First().Members;
        File.WriteAllText(output, root.ReplaceNode(program, program.AddMembers(members.ToArray())).ToFullString());
        Console.WriteLine("Aliased " + nodes.Length + " constants and " + invocations.Length + " API calls");
    }
}
