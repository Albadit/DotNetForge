using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotNetForge.Analyzers;

/// <summary>
/// The code-size guardrails (.docs/guides/testing.md#guardrails): a file with more than 500 lines of code (DNF1001) and
/// a statement nested more than 4 deep in its method (DNF1002). Suggestions by default (.editorconfig can raise them):
/// they mark code growing past what is easy to review without failing the build on code that already is, and have no
/// automatic fix. .editorconfig sets the limits (dnf_max_file_lines,
/// dnf_max_depth) and can switch a rule off for a folder (dotnet_diagnostic.DNF1001.severity = none). Generated code is
/// skipped. Cyclomatic complexity is .NET's own CA1502 (CodeMetricsConfig.txt).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeSizeAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor FileTooLong = new(
        "DNF1001",
        "File too long",
        "This file has {0} lines of code, more than {1}: split it by responsibility",
        "Maintainability",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Lines that hold code count; blank lines and comments don't. It points at the first line past the limit.");

    public static readonly DiagnosticDescriptor NestedTooDeep = new(
        "DNF1002",
        "Nested too deep",
        "This statement is nested {0} deep, more than {1}: return early or move the inner part into a method",
        "Maintainability",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "if, switch, try, for, foreach, while and do nest; an else-if stays at its if's depth. A method, local function or lambda starts again at 0.");

    private const int DefaultMaxLines = 500;
    private const int DefaultMaxDepth = 4;

    private static readonly SyntaxKind[] Nesting =
    {
        SyntaxKind.IfStatement, SyntaxKind.SwitchStatement, SyntaxKind.TryStatement, SyntaxKind.ForStatement,
        SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement, SyntaxKind.WhileStatement, SyntaxKind.DoStatement,
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(FileTooLong, NestedTooDeep);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(CheckLength);
        context.RegisterSyntaxNodeAction(CheckDepth, Nesting);
    }

    private static void CheckLength(SyntaxTreeAnalysisContext context)
    {
        var max = Limit(context.Options, context.Tree, "dnf_max_file_lines", DefaultMaxLines);
        var text = context.Tree.GetText(context.CancellationToken);
        var hasCode = new bool[text.Lines.Count];
        var count = 0;
        SyntaxToken? firstPast = null;
        foreach (var token in context.Tree.GetRoot(context.CancellationToken).DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.EndOfFileToken))
            {
                continue;
            }

            // A token can span lines (a verbatim or raw string): each of them holds code.
            var lines = text.Lines.GetLinePositionSpan(token.Span);
            for (var line = lines.Start.Line; line <= lines.End.Line; line++)
            {
                if (hasCode[line])
                {
                    continue;
                }

                hasCode[line] = true;
                if (++count == max + 1)
                {
                    firstPast = token;
                }
            }
        }

        if (firstPast is { } past)
        {
            context.ReportDiagnostic(Diagnostic.Create(FileTooLong, past.GetLocation(), count, max));
        }
    }

    private static void CheckDepth(SyntaxNodeAnalysisContext context)
    {
        var statement = context.Node;
        if (IsElseIf(statement))
        {
            return; // counted with its if
        }

        var max = Limit(context.Options, statement.SyntaxTree, "dnf_max_depth", DefaultMaxDepth);
        var depth = 1;
        for (var node = statement.Parent; node is not null && !StartsAgain(node); node = node.Parent)
        {
            if (Array.IndexOf(Nesting, node.Kind()) >= 0 && !IsElseIf(node))
            {
                depth++;
            }
        }

        // Only the first level past the limit: what is nested deeper sits inside it.
        if (depth == max + 1)
        {
            var location = statement.GetFirstToken().GetLocation();
            context.ReportDiagnostic(Diagnostic.Create(NestedTooDeep, location, depth, max));
        }
    }

    private static bool IsElseIf(SyntaxNode node) => node is IfStatementSyntax && node.Parent is ElseClauseSyntax;

    private static bool StartsAgain(SyntaxNode node) =>
        node is MemberDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or AccessorDeclarationSyntax;

    private static int Limit(AnalyzerOptions options, SyntaxTree tree, string key, int fallback) =>
        options.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue(key, out var value)
            && int.TryParse(value, out var limit) && limit > 0
            ? limit
            : fallback;
}
