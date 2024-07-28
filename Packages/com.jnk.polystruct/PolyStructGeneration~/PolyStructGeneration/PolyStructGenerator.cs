using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Jnk.PolyStructGeneration;

[Generator]
public class PolyStructGenerator : IIncrementalGenerator
{
    private record struct PolymorphicStructInput(INamedTypeSymbol Interface, ImmutableArray<INamedTypeSymbol> Structs);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol> interfaceTypesProvider = context.SyntaxProvider
            .CreateSyntaxProvider(IsPotentialInterfaceSyntax, GetSemanticDeclarationForInterfaceTarget)
            .Where(static x => x is not null)!;

        IncrementalValueProvider<ImmutableArray<INamedTypeSymbol>> structTypesProvider = context.SyntaxProvider
            .CreateSyntaxProvider(IsPotentialStructSyntax, GetSemanticDeclarationForStructTarget)
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect();

        IncrementalValuesProvider<PolymorphicStructInput> generatorInputsProvider
            = interfaceTypesProvider.Combine(structTypesProvider).Select(FilterStructsThatImplementInterface);

        IncrementalValueProvider<(Compilation compilation, ImmutableArray<PolymorphicStructInput> inputs)> compilationAndInputs
            = context.CompilationProvider.Combine(generatorInputsProvider.Collect());

        context.RegisterSourceOutput(compilationAndInputs, static (spc, x) => Execute(spc, x.compilation, x.inputs));
    }

    private static bool IsPotentialInterfaceSyntax(SyntaxNode syntaxNode, CancellationToken cancellationToken)
    {
        return syntaxNode is InterfaceDeclarationSyntax { AttributeLists.Count: > 0 };
    }

    private static INamedTypeSymbol? GetSemanticDeclarationForInterfaceTarget(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;

        foreach (AttributeListSyntax attributeListSyntax in interfaceDeclaration.AttributeLists)
        {
            foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
            {
                if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                    continue;

                INamedTypeSymbol attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                string fullName = attributeContainingTypeSymbol.ToDisplayString();

                if (fullName == "Jnk.PolyStruct.PolyStructAttribute")
                    return context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration) as INamedTypeSymbol;
            }
        }

        return null;
    }

    private static bool IsPotentialStructSyntax(SyntaxNode syntaxNode, CancellationToken cancellationToken)
    {
        return syntaxNode is StructDeclarationSyntax { BaseList.Types.Count: > 0 };
    }

    private static INamedTypeSymbol? GetSemanticDeclarationForStructTarget(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var structDeclaration = (StructDeclarationSyntax)context.Node;
        var @struct = context.SemanticModel.GetDeclaredSymbol(structDeclaration);

        if (@struct is not INamedTypeSymbol namedTypeSymbol)
            return null;

        if (namedTypeSymbol.AllInterfaces.Length > 0)
            return namedTypeSymbol;

        return null;
    }

    private static PolymorphicStructInput FilterStructsThatImplementInterface((INamedTypeSymbol interfaceType, ImmutableArray<INamedTypeSymbol> structTypes) input, CancellationToken cancellationToken)
    {
        ImmutableHashSet<INamedTypeSymbol>.Builder implementingStructs = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (INamedTypeSymbol structType in input.structTypes)
        {
            if (structType.AllInterfaces.Contains(input.interfaceType))
                implementingStructs.Add(structType);
        }

        return new PolymorphicStructInput(input.interfaceType, implementingStructs.ToImmutableArray());
    }

    private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<PolymorphicStructInput> inputs)
    {
        if (inputs.IsDefaultOrEmpty)
            return;

        foreach (PolymorphicStructInput input in inputs)
        {
            (string structName, string sourceCode) = GenerateSourceCode(input);
            context.AddSource($"{structName}.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
        }
    }

    readonly record struct StructInfo(INamedTypeSymbol Type, string QualifiedTypeName, string ParameterName, string PrivateFieldName, string EnumName)
    {
        public INamedTypeSymbol Type { get; } = Type;
        public string QualifiedTypeName { get; } = QualifiedTypeName;
        public string ParameterName { get; } = ParameterName;
        public string PrivateFieldName { get; } = PrivateFieldName;
        public string EnumName { get; } = EnumName;
    }

    private static (string structName, string sourceCode) GenerateSourceCode(PolymorphicStructInput input)
    {
        var cb = new CodeBuilder();

        string interfaceName = input.Interface.Name;
        string generatedStructName = interfaceName[0] == 'I' ? interfaceName.Substring(1) : $"Impl{interfaceName}";
        string generatedStructParameterName = $"{char.ToLower(generatedStructName[0])}{generatedStructName.Substring(1)}";

        ImmutableArray<StructInfo> structs = input.Structs
            .Select(x => new StructInfo(x, x.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), GetParameterName(x), GetPrivateFieldName(x), x.Name))
            .OrderBy(x => x.EnumName, StringComparer.Ordinal)
            .ToImmutableArray();

        cb.Line["using System;"].End();
        cb.Line["using System.Runtime.InteropServices;"].End();
        cb.AppendLine();

        IDisposable? nameSpaceScope = null;
        if (input.Interface.ContainingNamespace is { IsGlobalNamespace: false })
        {
            cb.Line["namespace "][input.Interface.ContainingNamespace.ToDisplayString()].End();
            nameSpaceScope = cb.BlockScope();
        }

        cb.Line["[StructLayout(LayoutKind.Explicit)]"].End();
        cb.Line["public partial struct "][generatedStructName][" : "][input.Interface.Name].End();
        cb.OpenBlock();

        cb.Line["public enum Type"].End();
        using (cb.BlockScope())
        {
            cb.Line["Uninitialized = 0,"].End();
            cb.Line["Empty = 1,"].End();

            foreach (StructInfo @struct in structs)
                cb.Line[@struct.EnumName][','].End();
        }
        cb.AppendLine();

        cb.Line["[FieldOffset(0)]"].End();
        cb.Line["private Type _type;"].End();
        cb.AppendLine();

        foreach (StructInfo @struct in structs)
        {
            cb.Line["[FieldOffset(8)]"].End();
            cb.Line["private "][@struct.QualifiedTypeName][' '][@struct.PrivateFieldName][';'].End();
            cb.AppendLine();
        }

        cb.Line["public readonly Type CurrentType => _type;"].End();
        cb.AppendLine();

        cb.Line["public readonly "][interfaceName][" Unwrapped"].End();
        using (cb.BlockScope())
        {
            cb.Line["get"].End();
            using (cb.BlockScope())
            {
                cb.Line["return _type switch"].End();
                cb.OpenBlock();

                cb.Line["Type.Uninitialized => throw new InvalidOperationException(\"Struct has not been initialized.\"),"].End();
                cb.Line["Type.Empty => null,"].End();

                foreach (StructInfo @struct in structs)
                {
                    cb.Line["Type."][@struct.EnumName][" => "][@struct.PrivateFieldName][','].End();
                }

                cb.Line["_ => throw new ArgumentOutOfRangeException(),"].End();

                cb.ReduceIndent();
                cb.Line["};"].End();
            }
        }
        cb.AppendLine();

        cb.Line["public static readonly "][generatedStructName][" Empty = new() { _type = Type.Empty };"].End();
        cb.AppendLine();

        foreach (StructInfo @struct in structs)
        {
            cb.Line["public "][generatedStructName]['('][@struct.QualifiedTypeName][' '][@struct.ParameterName][") : this()"].End();
            using (cb.BlockScope())
            {
                cb.Line["_type = Type."][@struct.EnumName][';'].End();
                cb.Line[@struct.PrivateFieldName][" = "][@struct.ParameterName][';'].End();
            }

            cb.AppendLine();
        }

        foreach (ISymbol interfaceMember in input.Interface.GetMembers())
        {
            switch (interfaceMember.Kind)
            {
                case SymbolKind.Property when interfaceMember is IPropertySymbol property:
                    AppendPropertyImplementation(cb, property, structs);
                    break;
                case SymbolKind.Method when interfaceMember is IMethodSymbol { MethodKind: MethodKind.Ordinary } method:
                    AppendMethodImplementation(cb, method, structs);
                    break;
                default:
                    continue;
            }

            cb.AppendLine();
        }

        cb.Line["public readonly bool TypeEquals(in "][generatedStructName][" other) => _type == other._type;"].End();
        cb.AppendLine();

        foreach (StructInfo @struct in structs)
        {
            cb.Line["public static Type TypeFor(in "][@struct.QualifiedTypeName][' '][@struct.ParameterName][") => Type."][@struct.EnumName][';'].End();
            cb.AppendLine();
        }

        cb.Line["public static string GetSizeInformation()"].End();
        using (cb.BlockScope())
        {
            cb.Line["var sb = new global::System.Text.StringBuilder();"].End();
            cb.Line["sb.Append(\""][generatedStructName][" = \").AppendLine(Marshal.SizeOf<"][generatedStructName][">().ToString());"].End();
            foreach (StructInfo @struct in structs)
            {
                cb.Line["sb.Append(\""][@struct.EnumName][" = \").AppendLine(Marshal.SizeOf<"][@struct.QualifiedTypeName][">().ToString());"].End();
            }
            cb.Line["return sb.ToString();"].End();
        }
        cb.AppendLine();

        // TODO: Only generate in unsafe assemblies
        // cb.Line["public unsafe static string GetUnsafeSizeInformation()"].End();
        // using (cb.BlockScope())
        // {
        //     cb.Line["var sb = new global::System.Text.StringBuilder();"].End();
        //     cb.Line["sb.Append(\""][generatedStructName][" = \").Append(Marshal.SizeOf<"][generatedStructName][">().ToString()).Append(\" / \").AppendLine(sizeof("][generatedStructName][").ToString());"].End();
        //     foreach (StructInfo @struct in structs)
        //     {
        //         cb.Line["sb.Append(\""][@struct.EnumName][" = \").Append(Marshal.SizeOf<"][@struct.QualifiedTypeName][">().ToString()).Append(\" / \").AppendLine(sizeof("][@struct.QualifiedTypeName][").ToString());"].End();
        //     }
        //     cb.Line["return sb.ToString();"].End();
        // }
        // cb.AppendLine();

        foreach (StructInfo @struct in structs)
        {
            cb.Line["public static implicit operator "][generatedStructName]['('][@struct.QualifiedTypeName][' '][@struct.ParameterName][") => new("][@struct.ParameterName][");"].End();
            cb.AppendLine();
            cb.Line["public static explicit operator "][@struct.QualifiedTypeName]['('][generatedStructName][' '][generatedStructParameterName][") => "][generatedStructParameterName]["._type == Type."][@struct.EnumName][" ? "][generatedStructParameterName]['.'][@struct.PrivateFieldName][" : throw new InvalidCastException();"].End();
            cb.AppendLine();
        }

        cb.CloseBlock();
        nameSpaceScope?.Dispose();

        return (generatedStructName, cb.ToString());
    }

    private static string GetParameterName(INamedTypeSymbol symbol)
    {
        return $"{char.ToLower(symbol.Name[0])}{symbol.Name.Substring(1)}";
    }

    private static string GetPrivateFieldName(INamedTypeSymbol symbol)
    {
        return $"_{char.ToLower(symbol.Name[0])}{symbol.Name.Substring(1)}";
    }

    private static void AppendMethodImplementation(CodeBuilder cb, IMethodSymbol method, ImmutableArray<StructInfo> structs)
    {
        cb.Line["public "][method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)][' '].AppendMethodDefinitionString(method).End();
        cb.OpenBlock();

        cb.Line["switch (_type)"].End();
        cb.OpenBlock();

        cb.Line["case Type.Uninitialized:"].End();
        cb.Line.Indent["throw new InvalidOperationException(\"Struct has not been initialized.\");"].End();
        cb.Line["case Type.Empty:"].End();
        cb.Line.Indent[method.ReturnsVoid ? "break;" : "return default;"].End();

        foreach (StructInfo @struct in structs)
        {
            cb.Line["case Type."][@struct.EnumName][':'].End();
            cb.IncreaseIndent();

            if (method.ReturnsVoid)
            {
                cb.Line[@struct.PrivateFieldName]['.'].AppendMethodCallString(method)[';'].End();
                cb.Line["break;"].End();
            }
            else
            {
                cb.Line["return "][@struct.PrivateFieldName]['.'].AppendMethodCallString(method)[';'].End();
            }

            cb.ReduceIndent();
        }

        cb.Line["default:"].End();
        cb.Line.Indent["throw new ArgumentOutOfRangeException();"].End();

        cb.CloseBlock();
        cb.CloseBlock();
    }

    private static void AppendPropertyImplementation(CodeBuilder cb, IPropertySymbol property, ImmutableArray<StructInfo> structs)
    {
        cb.Line["public "][property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)][' '][property.Name].End();
        cb.OpenBlock();

        if (!property.IsWriteOnly)
        {
            cb.Line["get"].End();
            cb.OpenBlock();

            cb.Line["return _type switch"].End();
            cb.OpenBlock();

            cb.Line["Type.Uninitialized => throw new InvalidOperationException(\"Struct has not been initialized.\"),"].End();
            cb.Line["Type.Empty => default,"].End();

            foreach (StructInfo @struct in structs)
                cb.Line["Type."][@struct.EnumName][" => "][@struct.PrivateFieldName]['.'][property.Name][','].End();

            cb.Line["_ => throw new ArgumentOutOfRangeException()"].End();

            cb.ReduceIndent();
            cb.Line["};"].End();

            cb.CloseBlock();
        }

        if (!property.IsReadOnly)
        {
            cb.Line["set"].End();
            cb.OpenBlock();

            cb.Line["switch (_type)"].End();
            using (cb.BlockScope())
            {
                cb.Line["case Type.Uninitialized:"].End();
                cb.Line.Indent["throw new InvalidOperationException(\"Struct has not been initialized.\");"].End();
                cb.Line["case Type.Empty:"].End();
                cb.Line.Indent["break"].End();

                foreach (StructInfo @struct in structs)
                {
                    cb.Line["case Type."][@struct.EnumName][':'].End();
                    cb.Line.Indent[@struct.PrivateFieldName][" = value;"].End();
                }
            }

            cb.CloseBlock();
        }

        cb.CloseBlock();
    }
}