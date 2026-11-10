using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
sealed class RemoveInstrumentation : CSharpSyntaxRewriter
{
    bool removeFlight; readonly HashSet<string> methods = new(StringComparer.Ordinal) {
        
        "LogPreviousCarrierSpikeTrace", "LogPreviousMinerSpikeTrace", "LogPreviousTickTrace",
        "BeginRuntimeProfile", "MarkRuntimeProfileSection", "BeginRuntimeDetail", "EndRuntimeDetail",
        "CaptureRuntimeProfilePeak", "LogRuntimeProfileWindow",
        
        "BeginDockingFinalApproachProfile", "MarkDockingFinalApproachProfile", "FinishDockingFinalApproachProfile"
    };
    public List<string> Methods = new();
    public int Calls, Fields, Assignments;
    public RemoveInstrumentation(bool flight) { removeFlight = flight; if(flight) {methods.Add("ShouldWriteFlightDiagnostics");methods.Add("AppendFlightDiagnostics");methods.Add("GetFlightDiagnosticContext");} }
    bool LoggingField(string name) => name.StartsWith("runtimeProfile") || name.StartsWith("runtimeDetail")
        || name.StartsWith("_runtimeProfile") || name.StartsWith("dockingFinalProfile") || removeFlight && name.StartsWith("flightDiag")
        
        || name is "carrierRuntimeWorkMask" or "minerRuntimeWorkMask" ;
    bool LoggingCall(ExpressionSyntax expression) => expression is InvocationExpressionSyntax call
        && call.Expression is IdentifierNameSyntax name && methods.Contains(name.Identifier.ValueText);
    static bool Empty(StatementSyntax statement) => statement is EmptyStatementSyntax || statement is BlockSyntax block && block.Statements.Count == 0;
    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (methods.Contains(node.Identifier.ValueText)) { Methods.Add(node.Identifier.ValueText); return null; }
        return base.VisitMethodDeclaration(node);
    }
    public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var remaining = node.Declaration.Variables.Where(v => !LoggingField(v.Identifier.ValueText)).ToArray();
        Fields += node.Declaration.Variables.Count - remaining.Length;
        return remaining.Length == 0 ? null : base.VisitFieldDeclaration(node.WithDeclaration(node.Declaration.WithVariables(SyntaxFactory.SeparatedList(remaining))));
    }
    public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (LoggingCall(node.Expression)) { Calls++; return SyntaxFactory.EmptyStatement(); }
        if (node.Expression is AssignmentExpressionSyntax assignment
            && (assignment.Left is IdentifierNameSyntax name && LoggingField(name.Identifier.ValueText) || LoggingCall(assignment.Right)))
        { Assignments++; return SyntaxFactory.EmptyStatement(); }
        if (node.Expression is PostfixUnaryExpressionSyntax unary && unary.Operand is IdentifierNameSyntax field && LoggingField(field.Identifier.ValueText))
        { Assignments++; return SyntaxFactory.EmptyStatement(); }
        return base.VisitExpressionStatement(node);
    }
    public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var remaining = node.Declaration.Variables.Where(v => v.Initializer == null || !LoggingCall(v.Initializer.Value)).ToArray();
        Calls += node.Declaration.Variables.Count - remaining.Length;
        return remaining.Length == 0 ? SyntaxFactory.EmptyStatement() : base.VisitLocalDeclarationStatement(node.WithDeclaration(node.Declaration.WithVariables(SyntaxFactory.SeparatedList(remaining))));
    }
    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var result = (BlockSyntax)base.VisitBlock(node);
        return result.WithStatements(SyntaxFactory.List(result.Statements.Where(s => !Empty(s))));
    }
    public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
    {
        var result = (IfStatementSyntax)base.VisitIfStatement(node);
        return Empty(result.Statement) && (result.Else == null || Empty(result.Else.Statement)) ? SyntaxFactory.EmptyStatement() : result;
    }
    public override SyntaxNode VisitTryStatement(TryStatementSyntax node)
    {
        var result = (TryStatementSyntax)base.VisitTryStatement(node);
        if (result.Finally != null && result.Finally.Block.Statements.Count == 0)
            return result.Catches.Count == 0 ? result.Block : result.WithFinally(null);
        return result;
    }
}

