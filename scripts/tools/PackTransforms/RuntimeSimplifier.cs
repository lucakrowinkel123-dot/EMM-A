using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;using Microsoft.CodeAnalysis.CSharp.Syntax;
static class RuntimeSimplifier{    public static string Simplify(string source,bool removeFlightDiagnostics)
    {
        var remove=new RemoveInstrumentation(removeFlightDiagnostics);var changed=remove.Visit(CSharpSyntaxTree.ParseText(source).GetRoot());
        var runtime=changed.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m=>m.Identifier.ValueText=="UpdateRuntimeMetrics");
        var body=(BlockSyntax)SyntaxFactory.ParseStatement("{double ms=Runtime.LastRunTimeMs;moduleRuntimeSum+=ms;moduleRuntimeCount++;moduleRuntimePeak=Math.Max(moduleRuntimePeak,ms);runtimePeakLastMinuteMs=moduleRuntimePeak;runtimePeakWindowElapsedSeconds+=secondsPerTick;if(runtimePeakWindowElapsedSeconds>=60){ScriptLog(\"PERF-MODULE: \"+moduleRole+\" avg=\"+(moduleRuntimeSum/moduleRuntimeCount).ToString(\"0.000\")+\" ms; peak=\"+moduleRuntimePeak.ToString(\"0.000\")+\" ms; samples=\"+moduleRuntimeCount);moduleRuntimeSum=moduleRuntimePeak=0;moduleRuntimeCount=0;runtimePeakWindowElapsedSeconds=0;}}");
        changed=changed.ReplaceNode(runtime,runtime.WithBody(body));
        var program=changed.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c=>c.Identifier.ValueText=="Program");
        var fields=CSharpSyntaxTree.ParseText("class X{double moduleRuntimeSum,moduleRuntimePeak;long moduleRuntimeCount;}").GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First().Members;
        return changed.ReplaceNode(program,program.AddMembers(fields.ToArray())).ToFullString();
    }
}