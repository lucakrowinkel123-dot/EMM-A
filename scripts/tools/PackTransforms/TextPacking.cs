using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;using Microsoft.CodeAnalysis.CSharp.Syntax;
static class TextPacking{    public static string PackText(string source)
    {
        var packer=new TextPacker();var changed=packer.Visit(CSharpSyntaxTree.ParseText(source).GetRoot());
        var program=changed.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c=>c.Identifier.ValueText=="Program");
        var members=CSharpSyntaxTree.ParseText("class X{"+packer.Helpers()+"}").GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First().Members;
        return changed.ReplaceNode(program,program.AddMembers(members.ToArray())).ToFullString();
    }
    class TextPacker : CSharpSyntaxRewriter
    {
        public List<string> Values=new();Dictionary<string,int> indexes=new();
        public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if(!node.IsKind(SyntaxKind.StringLiteralExpression))return base.VisitLiteralExpression(node);
            string value=node.Token.ValueText;
            if(value.Length<12 || value.Any(c=>c>127) || node.Ancestors().Any(a=> a is ParameterSyntax || a is SwitchLabelSyntax || a is AttributeSyntax || a is ConstantPatternSyntax || a is FieldDeclarationSyntax f && f.Modifiers.Any(SyntaxKind.ConstKeyword) || a is LocalDeclarationStatementSyntax l && l.Modifiers.Any(SyntaxKind.ConstKeyword)))return base.VisitLiteralExpression(node);
            if(!indexes.TryGetValue(value,out int index)){index=Values.Count;indexes[value]=index;Values.Add(value);}
            return SyntaxFactory.ParseExpression("DecodeText("+index+")").WithTriviaFrom(node);
        }
        public string Helpers()
        {
            string Encode(string s){var b=new System.Text.StringBuilder();b.Append((char)(0x4000+s.Length%2));for(int i=0;i<s.Length;i+=2)b.Append((char)(0x4000+s[i]*128+(i+1<s.Length?s[i+1]:0)));return b.ToString();}
            foreach(string s in Values){string e=Encode(s);var a=new char[(e.Length-1)*2-(e[0]&1)];for(int i=0;i<a.Length;i++)a[i]=(char)(((e[i/2+1]-16384)>>(i%2==0?7:0))&127);if(new string(a)!=s)throw new Exception("Text roundtrip failed");}
            return "static readonly string[] PackedText={"+string.Join(",",Values.Select(s=>SymbolDisplay.FormatLiteral(Encode(s),true)))+"};static readonly string[] DecodedText=new string["+Values.Count+"];static string DecodeText(int n){if(DecodedText[n]!=null)return DecodedText[n];string s=PackedText[n];char[] a=new char[(s.Length-1)*2-(s[0]&1)];for(int i=0;i<a.Length;i++)a[i]=(char)(((s[i/2+1]-16384)>>(i%2==0?7:0))&127);return DecodedText[n]=new string(a);}";
        }
    }
}