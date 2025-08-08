using ApiUsageAnalyzer.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace ApiUsageAnalyzer;

public static class FindAllReferences
{
    public static void FindReferences(
        Func<IAssemblySymbol, bool> referenceFilter,
        Action<(ISymbol Definition, ISymbol ReferencingSymbol, FileLinePositionSpan Location)> referenceFound,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        new DeclarationSymbolVisitor(referenceFilter, referenceFound, compilation, cancellationToken)
            .Visit(compilation.Assembly);
    }

    private sealed class DeclarationSymbolVisitor(
        Func<IAssemblySymbol, bool> isDependency,
        Action<(ISymbol Definition, ISymbol ReferencingSymbol, FileLinePositionSpan Location)> resultSink,
        Compilation compilation,
        CancellationToken cancellationToken) : SymbolVisitor
    {
        private readonly Func<IAssemblySymbol, bool> isDependency = isDependency;
        private readonly Action<(ISymbol Definition, ISymbol ReferencingSymbol, FileLinePositionSpan Location)> resultSink = resultSink;
        private readonly CancellationToken cancellationToken = cancellationToken;

        public override void DefaultVisit(ISymbol symbol)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var attribute in symbol.GetAttributes())
                VisitReference(attribute.AttributeClass, symbol, [attribute.ApplicationSyntaxReference!]);
        }

        public override void VisitAssembly(IAssemblySymbol symbol)
        {
            base.VisitAssembly(symbol);
            foreach (var module in symbol.Modules)
                VisitModule(module);
        }

        public override void VisitModule(IModuleSymbol symbol)
        {
            base.VisitModule(symbol);
            VisitNamespace(symbol.GlobalNamespace);
        }

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            base.VisitNamespace(symbol);

            foreach (var member in symbol.GetMembers())
                Visit(member);
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            base.VisitNamedType(symbol);

            VisitReference(symbol.BaseType, symbol, () => SyntaxUtils.GetBaseTypeOrInterfaceLocations(compilation, symbol, symbol.BaseType!));

            foreach (var interfaceType in symbol.Interfaces)
                VisitReference(interfaceType, symbol, () => SyntaxUtils.GetBaseTypeOrInterfaceLocations(compilation, symbol, interfaceType));

            foreach (var typeParameter in symbol.TypeParameters)
                VisitTypeParameter(typeParameter);

            foreach (var member in symbol.GetMembers())
                Visit(member);
        }

        public override void VisitTypeParameter(ITypeParameterSymbol symbol)
        {
            base.VisitTypeParameter(symbol);

            foreach (var constraint in symbol.ConstraintTypes)
                VisitReference(constraint, symbol, symbol.DeclaringSyntaxReferences);
        }

        public override void VisitField(IFieldSymbol symbol)
        {
            if (symbol.IsImplicitlyDeclared) return;

            base.VisitField(symbol);

            VisitReference(symbol.Type, symbol, () => symbol.DeclaringSyntaxReferences.SelectLocations((VariableDeclaratorSyntax s) => ((VariableDeclarationSyntax)s.Parent!).Type));

            foreach (var declaration in symbol.DeclaringSyntaxReferences)
            {
                switch (declaration.GetSyntax())
                {
                    case VariableDeclaratorSyntax variableDeclarator:
                        VisitOperation(variableDeclarator.Initializer?.Value, symbol);
                        break;
                    case EnumMemberDeclarationSyntax enumMemberDeclaration:
                        VisitOperation(enumMemberDeclaration.EqualsValue?.Value, symbol);
                        break;
                    case var other:
                        throw new NotImplementedException(other.GetType().Name);
                }
            }
        }

        public override void VisitProperty(IPropertySymbol symbol)
        {
            base.VisitProperty(symbol);

            VisitReference(symbol.Type, symbol, symbol.DeclaringSyntaxReferences);

            if (symbol.GetMethod is not null)
                VisitMethod(symbol.GetMethod);

            if (symbol.SetMethod is not null)
                VisitMethod(symbol.SetMethod);

            foreach (var parameter in symbol.Parameters)
                VisitParameter(parameter);

            foreach (var declaration in symbol.DeclaringSyntaxReferences)
            {
                switch (declaration.GetSyntax(cancellationToken))
                {
                    case PropertyDeclarationSyntax propertyDeclaration:
                        VisitOperation(propertyDeclaration.Initializer?.Value, symbol);
                        break;
                    case IndexerDeclarationSyntax:
                        break;
                    case ParameterSyntax:
                        // Declared by a record primary constructor. The parameter will be visited when visiting the constructor.
                        break;
                    case var other:
                        throw new NotImplementedException(other.GetType().Name);
                }
            }
        }

        public override void VisitEvent(IEventSymbol symbol)
        {
            base.VisitEvent(symbol);

            VisitReference(symbol.Type, symbol, () => symbol.DeclaringSyntaxReferences.SelectLocations((SyntaxNode s) =>
                s.FirstAncestorOrSelf<VariableDeclarationSyntax>()?.Type
                ?? s.FirstAncestorOrSelf<EventDeclarationSyntax>()!.Type));

            if (symbol.AddMethod is not null)
                VisitMethod(symbol.AddMethod);

            if (symbol.RemoveMethod is not null)
                VisitMethod(symbol.RemoveMethod);

            if (symbol.RaiseMethod is not null)
                VisitMethod(symbol.RaiseMethod);
        }

        public override void VisitMethod(IMethodSymbol symbol)
        {
            if (symbol.IsImplicitlyDeclared) return;

            base.VisitMethod(symbol);

            var isAccessor = symbol.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove;
            if (!isAccessor)
            {
                VisitReference(symbol.ReturnType, symbol, symbol.DeclaringSyntaxReferences);

                foreach (var typeParameter in symbol.TypeParameters)
                    VisitTypeParameter(typeParameter);

                foreach (var parameter in symbol.Parameters)
                    VisitParameter(parameter);
            }

            foreach (var declaration in symbol.DeclaringSyntaxReferences)
            {
                switch (declaration.GetSyntax(cancellationToken))
                {
                    case BaseMethodDeclarationSyntax methodDeclaration:
                        VisitOperation(methodDeclaration.Body, symbol);
                        VisitOperation(methodDeclaration.ExpressionBody?.Expression, symbol);
                        break;
                    case AccessorDeclarationSyntax accessorDeclaration:
                        VisitOperation(accessorDeclaration.Body, symbol);
                        VisitOperation(accessorDeclaration.ExpressionBody?.Expression, symbol);
                        break;
                    case ArrowExpressionClauseSyntax arrowExpressionClause:
                        // This is the getter of an expression-bodied property.
                        VisitOperation(arrowExpressionClause.Expression, symbol);
                        break;
                    case ExpressionSyntax or StatementSyntax or QueryClauseSyntax:
                        // This is a lambda/local function/anonymous method/LINQ clause. We were called by the operation walker and it will already visit the body.
                        break;
                    case TypeDeclarationSyntax:
                        // This is a primary constructor.
                        break;
                    case ParameterSyntax:
                        // This is a property accessor of a primary parameter.
                        break;
                    case DelegateDeclarationSyntax:
                        break;
                    case CompilationUnitSyntax compilationUnit:
                        VisitOperation(compilationUnit, symbol);
                        break;
                    case var other:
                        throw new NotImplementedException(other.GetType().Name);
                }
            }
        }

        public override void VisitParameter(IParameterSymbol symbol)
        {
            base.VisitParameter(symbol);

            VisitReference(symbol.Type, symbol, () => symbol.DeclaringSyntaxReferences.SelectLocations((ParameterSyntax s) => (SyntaxNode?)s.Type ?? s));

            foreach (var declaration in symbol.DeclaringSyntaxReferences)
            {
                switch (declaration.GetSyntax(cancellationToken))
                {
                    case ParameterSyntax parameterDeclaration:
                        VisitOperation(parameterDeclaration.Default?.Value, symbol);
                        break;
                    case var other:
                        throw new NotImplementedException(other.GetType().Name);
                }
            }
        }

        private void VisitReference(ISymbol? referencedSymbol, ISymbol referencingSymbol, ImmutableArray<SyntaxReference> syntaxReferences)
        {
            VisitReference(referencedSymbol, referencingSymbol, () => ImmutableArray.CreateRange(syntaxReferences, r => r.SyntaxTree.GetLocation(r.Span)));
        }

        private void VisitReference(ISymbol? referencedSymbol, ISymbol referencingSymbol, Func<ImmutableArray<Location>> getLocations)
        {
            new SymbolReferenceVisitor(this, referencingSymbol, getLocations).Visit(referencedSymbol);
        }

        private sealed class SymbolReferenceVisitor(
            DeclarationSymbolVisitor declarationVisitor,
            ISymbol referencingSymbol,
            Func<ImmutableArray<Location>> getLocations) : SymbolVisitor
        {
            public override void DefaultVisit(ISymbol symbol)
            {
                declarationVisitor.cancellationToken.ThrowIfCancellationRequested();

                if (symbol.ContainingAssembly is { } assembly && declarationVisitor.isDependency(assembly))
                {
                    var locations = getLocations();
                    if (locations is [])
                        throw new ArgumentException("At least one location must be provided.", nameof(getLocations));

                    if (locations.Any(r => r is null))
                        throw new ArgumentException("All locations must be non-null.", nameof(getLocations));

                    foreach (var location in locations)
                        declarationVisitor.resultSink((symbol.OriginalDefinition, referencingSymbol, location.GetMappedLineSpan()));
                }
            }

            public override void VisitMethod(IMethodSymbol symbol)
            {
                base.VisitMethod(symbol);

                foreach (var typeArgument in symbol.TypeArguments)
                    Visit(typeArgument);
            }

            public override void VisitNamedType(INamedTypeSymbol symbol)
            {
                base.VisitNamedType(symbol);

                foreach (var typeArgument in symbol.TypeArguments)
                    Visit(typeArgument);
            }

            public override void VisitArrayType(IArrayTypeSymbol symbol)
            {
                base.VisitArrayType(symbol);
                Visit(symbol.ElementType);
            }

            public override void VisitPointerType(IPointerTypeSymbol symbol)
            {
                base.VisitPointerType(symbol);
                Visit(symbol.PointedAtType);
            }

            public override void VisitFunctionPointerType(IFunctionPointerTypeSymbol symbol)
            {
                base.VisitFunctionPointerType(symbol);
                VisitMethod(symbol.Signature);
            }
        }

        private void VisitOperation(SyntaxNode? node, ISymbol declaringSymbol)
        {
            if (node is null) return;

            var semanticModel = compilation.GetSemanticModel(node.SyntaxTree);
            new ReferenceDependencyOperationWalker(this, declaringSymbol).Visit(semanticModel.GetOperation(node, cancellationToken));
        }

        private sealed class ReferenceDependencyOperationWalker(DeclarationSymbolVisitor declarationVisitor, ISymbol declaringSymbol) : OperationWalker
        {
            public override void DefaultVisit(IOperation operation)
            {
                declarationVisitor.cancellationToken.ThrowIfCancellationRequested();
                base.DefaultVisit(operation);
            }

            public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation)
            {
                base.VisitAnonymousFunction(operation);
                declarationVisitor.VisitMethod(operation.Symbol);
            }

            public override void VisitArrayCreation(IArrayCreationOperation operation)
            {
                base.VisitArrayCreation(operation);

                if (operation.Syntax is ImplicitArrayCreationExpressionSyntax) return;

                declarationVisitor.VisitReference(operation.Type, declaringSymbol, () => [operation.Syntax switch
                {
                    ArrayCreationExpressionSyntax arrayCreation => arrayCreation.Type.GetLocation(),
                    _ => operation.Syntax.GetLocation(),
                }]);
            }

            public override void VisitAttribute(IAttributeOperation operation)
            {
                base.VisitAttribute(operation);
                declarationVisitor.VisitReference(operation.Type, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitBinaryOperator(IBinaryOperation operation)
            {
                base.VisitBinaryOperator(operation);

                declarationVisitor.VisitReference(operation.OperatorMethod, declaringSymbol, () => [operation.Syntax switch
                {
                    BinaryExpressionSyntax binaryExpression => binaryExpression.OperatorToken.GetLocation(),
                    _ => operation.Syntax.GetLocation(),
                }]);
            }

            public override void VisitCatchClause(ICatchClauseOperation operation)
            {
                base.VisitCatchClause(operation);
                declarationVisitor.VisitReference(operation.ExceptionType, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitConversion(IConversionOperation operation)
            {
                base.VisitConversion(operation);

                if (operation.IsImplicit) return;

                declarationVisitor.VisitReference(operation.Type, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitDeclarationExpression(IDeclarationExpressionOperation operation)
            {
                base.VisitDeclarationExpression(operation);

                if (operation.Syntax is DeclarationExpressionSyntax { Type.IsVar: true })
                {
                    // The usage of this type is really coming through some other API.
                    return;
                }

                declarationVisitor.VisitReference(operation.Type, declaringSymbol, () => [operation.Syntax switch
                {
                    DeclarationExpressionSyntax declarationExpression => declarationExpression.Type.GetLocation(),
                    _ => operation.Syntax.GetLocation(),
                }]);
            }

            public override void VisitDelegateCreation(IDelegateCreationOperation operation)
            {
                base.VisitDelegateCreation(operation);

                if (operation.IsImplicit) return;

                declarationVisitor.VisitReference(operation.Type, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitEventReference(IEventReferenceOperation operation)
            {
                base.VisitEventReference(operation);

                if (operation.Syntax is MemberAccessExpressionSyntax memberAccess)
                    VisitIfExplicitTypeName(operation.SemanticModel!, memberAccess.Expression);

                declarationVisitor.VisitReference(operation.Event, declaringSymbol, () => [operation.Syntax switch
                {
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.GetLocation(),
                    _ => operation.Syntax.GetLocation(),
                }]);
            }

            public override void VisitFieldReference(IFieldReferenceOperation operation)
            {
                base.VisitFieldReference(operation);

                if (operation.Syntax is MemberAccessExpressionSyntax memberAccess)
                    VisitIfExplicitTypeName(operation.SemanticModel!, memberAccess.Expression);

                declarationVisitor.VisitReference(operation.Field, declaringSymbol, () => [operation.Syntax switch
                {
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.GetLocation(),
                    _ => operation.Syntax.GetLocation(),
                }]);
            }

            public override void VisitInvocation(IInvocationOperation operation)
            {
                base.VisitInvocation(operation);

                if (operation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess })
                    VisitIfExplicitTypeName(operation.SemanticModel!, memberAccess.Expression);

                declarationVisitor.VisitReference(operation.TargetMethod, declaringSymbol, () => [operation.Syntax switch
                {
                    InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess } => memberAccess.Name.GetLocation(),
                    InvocationExpressionSyntax invocation => invocation.Expression.GetLocation(),
                    _ => operation.Syntax.GetLocation(),
                }]);
            }

            private void VisitIfExplicitTypeName(SemanticModel semanticModel, SyntaxNode expression)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(expression, declarationVisitor.cancellationToken);
                if (symbolInfo.Symbol is ITypeSymbol type)
                    declarationVisitor.VisitReference(type, declaringSymbol, () => [expression.GetLocation()]);
            }

            public override void VisitIsType(IIsTypeOperation operation)
            {
                base.VisitIsType(operation);
                declarationVisitor.VisitReference(operation.TypeOperand, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitLocalFunction(ILocalFunctionOperation operation)
            {
                base.VisitLocalFunction(operation);
                declarationVisitor.VisitMethod(operation.Symbol);
            }

            public override void VisitMethodReference(IMethodReferenceOperation operation)
            {
                base.VisitMethodReference(operation);
                declarationVisitor.VisitReference(operation.Method, declaringSymbol, () => [operation.Syntax switch
                {
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.GetLocation(),
                    _ => operation.Syntax.GetLocation(),
                }]);
            }

            public override void VisitObjectCreation(IObjectCreationOperation operation)
            {
                base.VisitObjectCreation(operation);

                if (operation.Syntax is ObjectCreationExpressionSyntax objectCreation)
                    VisitIfExplicitTypeName(operation.SemanticModel!, objectCreation.Type);

                declarationVisitor.VisitReference(operation.Constructor, declaringSymbol, () => [operation.Syntax switch
                {
                    ObjectCreationExpressionSyntax objectCreation => SyntaxUtils.Unify(objectCreation.NewKeyword.GetLocation(), objectCreation.Type.GetLocation()),
                    _ => operation.Syntax.GetLocation(),
                }]);
            }

            public override void VisitPropertyReference(IPropertyReferenceOperation operation)
            {
                base.VisitPropertyReference(operation);

                if (operation.Syntax is MemberAccessExpressionSyntax memberAccess)
                    VisitIfExplicitTypeName(operation.SemanticModel!, memberAccess.Expression);

                switch (operation.Parent)
                {
                    case IAssignmentOperation assignment when operation == assignment.Target:
                        declarationVisitor.VisitReference(operation.Property.SetMethod, declaringSymbol, GetLocations);
                        return;
                    case INameOfOperation:
                        declarationVisitor.VisitReference(operation.Property, declaringSymbol, GetLocations);
                        return;
                    default:
                        declarationVisitor.VisitReference(operation.Property.GetMethod, declaringSymbol, GetLocations);
                        return;
                }

                ImmutableArray<Location> GetLocations() => [operation.Syntax switch
                {
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.GetLocation(),
                    _ => operation.Syntax.GetLocation(),
                }];
            }

            public override void VisitSizeOf(ISizeOfOperation operation)
            {
                base.VisitSizeOf(operation);
                declarationVisitor.VisitReference(operation.TypeOperand, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitTypeOf(ITypeOfOperation operation)
            {
                base.VisitTypeOf(operation);
                declarationVisitor.VisitReference(operation.TypeOperand, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitTypePattern(ITypePatternOperation operation)
            {
                base.VisitTypePattern(operation);
                declarationVisitor.VisitReference(operation.MatchedType, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitUnaryOperator(IUnaryOperation operation)
            {
                base.VisitUnaryOperator(operation);
                declarationVisitor.VisitReference(operation.OperatorMethod, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitUsing(IUsingOperation operation)
            {
                base.VisitUsing(operation);
                HandleUsingVariables(operation.Locals, operation.IsAsynchronous, () => [operation.Syntax.GetLocation()]);
            }

            public override void VisitUsingDeclaration(IUsingDeclarationOperation operation)
            {
                base.VisitUsingDeclaration(operation);
                HandleUsingVariables(operation.DeclarationGroup.GetDeclaredVariables(), operation.IsAsynchronous, () => [operation.Syntax.GetLocation()]);
            }

            private void HandleUsingVariables(ImmutableArray<ILocalSymbol> usingVariables, bool isAsyncUsing, Func<ImmutableArray<Location>> getLocations)
            {
                foreach (var local in usingVariables)
                {
                    // Even though this is not technically correct (a public dispose method is only needed for ref
                    // structs, since everything else is cast to IDisposable), in real life we don't want to suggest
                    // that the public Dispose is unused simply because it could have been an explicit implementation.
                    // That's not a desirable endpoint.
                    
                    for (var currentType = local.Type; currentType is not null; currentType = currentType.BaseType)
                    {
                        var disposeMethod = local.Type.GetMembers(isAsyncUsing ? "DisposeAsync" : "Dispose").OfType<IMethodSymbol>()
                            .SingleOrDefault(m => m is { IsStatic: false, Parameters: [], DeclaredAccessibility: Accessibility.Public });

                        if (disposeMethod is not null)
                        {
                            declarationVisitor.VisitReference(disposeMethod, declaringSymbol, getLocations);
                            break;
                        }
                    }
                }
            }

            public override void VisitVariableDeclaration(IVariableDeclarationOperation operation)
            {
                base.VisitVariableDeclaration(operation);

                if (operation.Language == LanguageNames.CSharp)
                {
                    var typeSyntax = ((VariableDeclarationSyntax)operation.Syntax).Type;

                    if (typeSyntax.IsVar)
                    {
                        // The usage of this type is really coming through some other API.
                        return;
                    }

                    declarationVisitor.VisitReference(operation.Declarators[0].Symbol.Type, declaringSymbol, () => [typeSyntax.GetLocation()]);
                }
            }

            public override void VisitVariableDeclarator(IVariableDeclaratorOperation operation)
            {
                base.VisitVariableDeclarator(operation);

                if (operation.Language == LanguageNames.CSharp && operation.Parent is IVariableDeclarationOperation)
                {
                    // Handled at the parent level since in C#, that is where the type is declared except in catches.
                    return;
                }

                declarationVisitor.VisitReference(operation.Symbol.Type, declaringSymbol, () => [operation.Syntax.GetLocation()]);
            }
        }
    }
}
