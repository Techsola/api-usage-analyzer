using Microsoft.CodeAnalysis;

namespace ApiUsageAnalyzer;

public static class PublicApiSurface
{
    public static void GetDeclaredPublicApis(
        Action<(ISymbol Definition, FileLinePositionSpan Location)> apiFound, 
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var visitor = new Visitor(apiFound, cancellationToken);
        visitor.Visit(compilation.Assembly.GlobalNamespace);
    }

    private sealed class Visitor(
        Action<(ISymbol Definition, FileLinePositionSpan Location)> apiFound,
        CancellationToken cancellationToken) : SymbolVisitor
    {
        public override void DefaultVisit(ISymbol symbol)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            base.VisitNamespace(symbol);

            foreach (var member in symbol.GetNamespaceMembers())
                VisitNamespace(member);

            foreach (var member in symbol.GetTypeMembers())
            {
                if (member.DeclaredAccessibility == Accessibility.Public)
                {
                    Report(member);
                    VisitNamedType(member);
                }
            }
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            base.VisitNamedType(symbol);

            if (symbol.TypeKind is not TypeKind.Delegate)
            {
                var members = symbol.GetMembers();
                var membersToVisit = members.ToList();

                foreach (var @event in members.OfType<IEventSymbol>())
                {
                    if (@event.AddMethod is not null)
                        membersToVisit.Remove(@event.AddMethod);
                    if (@event.RemoveMethod is not null)
                        membersToVisit.Remove(@event.RemoveMethod);
                }

                foreach (var member in membersToVisit)
                {
                    if (symbol.TypeKind == TypeKind.Enum && member is IMethodSymbol { MethodKind: MethodKind.Constructor, Parameters: [], IsStatic: false })
                    {
                        // Skip enum default constructors
                        continue;
                    }

                    if (member.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
                    {
                        Report(member);
                        Visit(member);
                    }
                }
            }
        }
        
        private void Report(ISymbol symbol)
        {
            var syntaxReferences = symbol.DeclaringSyntaxReferences;
            if (syntaxReferences is [])
                syntaxReferences = symbol.ContainingSymbol.DeclaringSyntaxReferences;

            if (syntaxReferences is [])
                throw new NotImplementedException();

            foreach (var syntaxReference in syntaxReferences)
            {
                var location = syntaxReference.GetSyntax(cancellationToken).GetLocation();
                apiFound((symbol, location.GetLineSpan()));
            }
        }
    }
}
