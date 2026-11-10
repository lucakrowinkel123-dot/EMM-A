using System.Text.Json;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
static class EnumAudit
{
    public static void Run(string input,string destination,string gameBin)
    {
        string output=Path.GetDirectoryName(Path.GetFullPath(destination)); Directory.CreateDirectory(output);
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p)).ToList();
        foreach(string path in Directory.GetFiles(gameBin,"*.dll"))try{using var f=File.OpenRead(path);using var pe=new PEReader(f);if(pe.HasMetadata)references.Add(MetadataReference.CreateFromFile(path));}catch{}
        foreach(string role in new[]{Path.GetFileNameWithoutExtension(input)})
        {
            var source=File.ReadAllText(input);
            var tree=CSharpSyntaxTree.ParseText(source);var root=tree.GetRoot();
            var compilation=CSharpCompilation.Create("Audit",new[]{tree},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));var model=compilation.GetSemanticModel(tree);
            var replacements=new Dictionary<ExpressionSyntax,ExpressionSyntax>();
            var audit=new List<object>();var required=new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            INamedTypeSymbol OwnEnum(ExpressionSyntax expression){var t=model.GetTypeInfo(expression).Type as INamedTypeSymbol;return t?.TypeKind==TypeKind.Enum && t.Locations.Any(l=>l.IsInSource)?t:null;}
            void Replace(ExpressionSyntax original,ExpressionSyntax receiver,string kind)
            {
                var type=OwnEnum(receiver);if(type==null||replacements.ContainsKey(original))return;
                required.Add(type);replacements[original]=SyntaxFactory.ParseExpression("EnumText("+receiver.WithoutTrivia()+")").WithTriviaFrom(original);
                audit.Add(new{kind,enumType=type.Name,line=tree.GetLineSpan(original.Span).StartLinePosition.Line+1,original=original.ToString(),replacement=replacements[original].ToString()});
            }
            foreach(var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if(call.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.ValueText=="ToString" && call.ArgumentList.Arguments.Count==0)Replace(call,m.Expression,"ToString");
                var symbol=model.GetSymbolInfo(call).Symbol as IMethodSymbol;
                string name=symbol?.Name??"";
                if(name is "Append" or "AppendLine" or "AppendFormat" or "Format" or "Concat" or "Set" or "WriteLine" or "ToString")
                    foreach(var arg in call.ArgumentList.Arguments)Replace(arg.Expression,arg.Expression,"argument:"+name);
            }
            foreach(var interpolation in root.DescendantNodes().OfType<InterpolationSyntax>())
                if(interpolation.FormatClause==null||interpolation.FormatClause.FormatStringToken.ValueText is "G" or "g")Replace(interpolation.Expression,interpolation.Expression,"interpolation");
            foreach(var binary in root.DescendantNodes().OfType<BinaryExpressionSyntax>().Where(b=>b.IsKind(SyntaxKind.AddExpression)))
                if(model.GetTypeInfo(binary).Type?.SpecialType==SpecialType.System_String){Replace(binary.Left,binary.Left,"concatenation");Replace(binary.Right,binary.Right,"concatenation");}
            string helpers="";
            foreach(var type in required.OrderBy(t=>t.Name))
            {
                var values=type.GetMembers().OfType<IFieldSymbol>().Where(f=>f.HasConstantValue).Select(f=>(name:f.Name,value:Convert.ToInt64(f.ConstantValue))).ToArray();
                if(type.GetAttributes().Any(a=>a.AttributeClass?.Name=="FlagsAttribute") || values.Any(v=>v.value<0)||values.Select(v=>v.value).Distinct().Count()!=values.Length)throw new Exception("Review flags/aliases/negative enum "+type.Name);
                helpers+="static string EnumText("+type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::IngameScript.Program.","")+" value){switch((int)value){"+string.Join("",values.Select(v=>"case "+v.value+":return "+SymbolDisplay.FormatLiteral(v.name,true)+";"))+"default:return ((int)value).ToString();}}\n";
            }
            var changed=root.ReplaceNodes(replacements.Keys,(old,_)=>replacements[old]);
            var program=changed.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c=>c.Identifier.ValueText=="Program");
            var members=CSharpSyntaxTree.ParseText("class X{"+helpers+"}").GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First().Members;
            changed=changed.ReplaceNode(program,program.AddMembers(members.ToArray()));
            string rewritten=System.Text.RegularExpressions.Regex.Replace(changed.ToFullString(),@"(?m)^\s*#region mdk preserve\s*\r?\n|^\s*#endregion\s*\r?\n","");
            var changedTree=CSharpSyntaxTree.ParseText(rewritten);
            var errors=compilation.ReplaceSyntaxTree(tree,changedTree).GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).Select(d=>d.ToString()).ToArray();
            File.WriteAllLines(Path.Combine(output,role+"-enum-compile-errors.txt"),errors);
            if(errors.Length>0)throw new Exception(errors[0]);
            File.WriteAllText(Path.Combine(output,role+"-enum-text-sites.json"),JsonSerializer.Serialize(audit,new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(output,role+"-enum-name-helpers.cs.txt"),helpers);
            if(destination!=null)
            {
                File.WriteAllText(destination,rewritten);
                Console.WriteLine($"{role}: {audit.Count} enum text sites, {required.Count} enum-name helpers, zero compile errors; wrote "+destination);
                continue;
            }
        }
    }
}

