using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Jnk.PolyStructGeneration;

[Generator]
public class InterfaceDelegatesGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(INamedTypeSymbol structTypeSymbol, IFieldSymbol fieldSymbol)> provider = context.SyntaxProvider.CreateSyntaxProvider(
            (node, _) => node is StructDeclarationSyntax structDeclaration
                         && structDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                         && structDeclaration.BaseList?.Types.Count > 0
                         && structDeclaration.Members.Any(x => x.AttributeLists.Count > 0),
            (ctx, _) =>
            {
                var structDeclaration = (StructDeclarationSyntax)ctx.Node;

                foreach (MemberDeclarationSyntax member in structDeclaration.Members)
                {
                    if (member is not FieldDeclarationSyntax field)
                        continue;

                    foreach (AttributeListSyntax attributeList in member.AttributeLists)
                    {
                        foreach (AttributeSyntax attribute in attributeList.Attributes)
                        {
                            if (ctx.SemanticModel.GetSymbolInfo(attribute).Symbol is not IMethodSymbol method)
                                continue;

                            string attributeTypeName = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            if (attributeTypeName == "global::Jnk.PolyStruct.GenerateInterfaceDelegatesAttribute")
                            {
                                var structTypeSymbol = ctx.SemanticModel.GetDeclaredSymbol(structDeclaration) as INamedTypeSymbol;
                                var fieldSymbol = ctx.SemanticModel.GetDeclaredSymbol(field.Declaration.Variables[0]) as IFieldSymbol;
                                return (structTypeSymbol, fieldSymbol);
                            }
                        }
                    }
                }

                return (default, default);
            }).Where(x => x.structTypeSymbol is not null && x.fieldSymbol is not null)!;

        context.RegisterSourceOutput(provider, (ctx, value) => Execute(ctx, value.structTypeSymbol, value.fieldSymbol));
    }

    public record struct MemberDelegateInfo(ISymbol InterfaceMember, ISymbol TargetMember);

    private static void Execute(SourceProductionContext ctx, ITypeSymbol structSymbol, IFieldSymbol fieldSymbol)
    {
        var cb = new CodeBuilder();

        ImmutableArray<MemberDelegateInfo>.Builder memberDelegatesBuilder = ImmutableArray.CreateBuilder<MemberDelegateInfo>();
        foreach (ISymbol interfaceMember in structSymbol.Interfaces.SelectMany(x => x.GetMembers()))
        {
            if (structSymbol.FindImplementationForInterfaceMember(interfaceMember) is not null)
                continue;

            ISymbol? targetMember = fieldSymbol.Type.GetMembers().FirstOrDefault(member =>
            {
                switch (interfaceMember.Kind, member.Kind)
                {
                    case (SymbolKind.Property, SymbolKind.Property) when interfaceMember is IPropertySymbol interfaceProperty && member is IPropertySymbol targetProperty:
                        return interfaceProperty.Name == targetProperty.Name && SymbolEqualityComparer.Default.Equals(interfaceProperty.Type, targetProperty.Type);
                    case (SymbolKind.Property, SymbolKind.Field) when interfaceMember is IPropertySymbol interfaceProperty && member is IFieldSymbol { DeclaredAccessibility: Accessibility.Public } targetField:
                        return interfaceProperty.Name == targetField.Name && SymbolEqualityComparer.Default.Equals(interfaceProperty.Type, targetField.Type);
                    case (SymbolKind.Method, SymbolKind.Method) when interfaceMember is IMethodSymbol { MethodKind: MethodKind.Ordinary } interfaceMethod && member is IMethodSymbol targetMethod:
                        return interfaceMethod.Name == targetMethod.Name && SymbolEqualityComparer.Default.Equals(interfaceMethod.ReturnType, targetMethod.ReturnType) && interfaceMethod.Parameters.SequenceEqual(targetMethod.Parameters, (a, b) => a.RefKind == b.RefKind && SymbolEqualityComparer.Default.Equals(a.Type, b.Type) && a.Name == b.Name);
                    default:
                        return false;
                }
            });

            if (targetMember is not null)
                memberDelegatesBuilder.Add(new MemberDelegateInfo(interfaceMember, targetMember));
        }
        ImmutableArray<MemberDelegateInfo> memberDelegateInfos = memberDelegatesBuilder.ToImmutable();

        IDisposable? namespaceScope = null;
        if (structSymbol.ContainingNamespace is { IsGlobalNamespace: false })
        {
            cb.Line["namespace "][structSymbol.ContainingNamespace.ToDisplayString()].End();
            namespaceScope = cb.BlockScope();
        }

        cb.Line["public partial struct "][structSymbol.Name].End();
        cb.OpenBlock();

        foreach (MemberDelegateInfo memberDelegate in memberDelegateInfos)
        {
            switch (memberDelegate.InterfaceMember, memberDelegate.TargetMember)
            {
                case (IPropertySymbol interfaceProperty, IPropertySymbol targetProperty):
                    cb.Line["public "][targetProperty.GetMethod?.IsReadOnly ?? false, "readonly "].QualifiedName(interfaceProperty.Type)[' '][interfaceProperty.Name][" => "][fieldSymbol.Name]['.'][targetProperty.Name][';'].End();
                    break;
                case (IPropertySymbol interfaceProperty, IFieldSymbol targetField):
                    cb.Line["public readonly "].QualifiedName(interfaceProperty.Type)[' '][interfaceProperty.Name][" => "][fieldSymbol.Name]['.'][targetField.Name][';'].End();
                    break;
                case (IMethodSymbol interfaceMethod, IMethodSymbol targetMethod):
                    cb.Line["public "][targetMethod.IsReadOnly, "readonly "].QualifiedName(interfaceMethod.ReturnType)[' '].AppendMethodDefinitionString(interfaceMethod)[" => "][fieldSymbol.Name]['.'].AppendMethodCallString(interfaceMethod)[';'].End();
                    break;
            }
        }

        cb.CloseBlock();
        namespaceScope?.Dispose();

        ctx.AddSource($"{structSymbol.Name}.InterfaceDelegates.g.cs", SourceText.From(cb.ToString(), Encoding.UTF8));
    }
}