using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ApiUsageAnalyzer.Utils;

internal static class SyntaxUtils
{
    public static ImmutableArray<Location> GetBaseTypeOrInterfaceLocations(Compilation compilation, INamedTypeSymbol symbol, ITypeSymbol baseTypeOrInterface)
    {
        var builder = ImmutableArray.CreateBuilder<Location>();

        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            var typeDeclaration = (TypeDeclarationSyntax)syntaxReference.GetSyntax();
            if (typeDeclaration.BaseList is null)
                continue;

            var sm = compilation.GetSemanticModel(typeDeclaration.SyntaxTree);

            foreach (var type in typeDeclaration.BaseList.Types)
            {
                if (SymbolEqualityComparer.Default.Equals(sm.GetTypeInfo(type.Type).Type, baseTypeOrInterface))
                {
                    builder.Add(type.Type.GetLocation());
                }
            }
        }

        return builder.DrainToImmutable();
    }

    public static ImmutableArray<Location> SelectLocations<TSource, TResult>(this ImmutableArray<SyntaxReference> syntaxReferences, Func<TSource, TResult> selector)
        where TSource : SyntaxNode
        where TResult : SyntaxNode
    {
        var builder = ImmutableArray.CreateBuilder<Location>(syntaxReferences.Length);

        foreach (var syntaxReference in syntaxReferences)
        {
            var sourceSyntax = (TSource)syntaxReference.GetSyntax();
            builder.Add(selector(sourceSyntax).GetLocation());
        }

        return builder.DrainToImmutable();
    }

    public static Location Unify(Location first, Location second)
    {
        if (first.SourceTree != second.SourceTree)
            throw new ArgumentException("Syntax nodes must belong to the same syntax tree.");

        return first.SourceTree!.GetLocation(Unify(first.SourceSpan, second.SourceSpan));
    }

    public static TextSpan Unify(TextSpan first, TextSpan second)
    {
        return TextSpan.FromBounds(
            Math.Min(first.Start, second.Start),
            Math.Max(first.End, second.End));
    }
}
