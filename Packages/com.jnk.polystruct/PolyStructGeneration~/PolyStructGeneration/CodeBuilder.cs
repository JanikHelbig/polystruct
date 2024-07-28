using System;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Jnk.PolyStructGeneration;

public class CodeBuilder
{
    public const string Indent = "    ";

    private readonly StringBuilder _sb = new();
    private readonly Action _closeBlockScope;
    private int _indent = 0;

    public LineBuilder Line => new(this);

    public bool IsNewLine => _sb.Length <= 0 || _sb[_sb.Length - 1] is '\n';

    public CodeBuilder()
    {
        _closeBlockScope = CloseBlock;
    }

    public CodeBuilder Append(string value)
    {
        _sb.Append(value);
        return this;
    }

    public CodeBuilder Append(char value)
    {
        _sb.Append(value);
        return this;
    }

    public void AppendLine(string value)
    {
        _sb.AppendLine(value);
    }

    public void AppendLine()
    {
        _sb.AppendLine();
    }

    public CodeBuilder AppendIndent()
    {
        for (var i = 0; i < _indent; i++)
            _sb.Append(Indent);

        return this;
    }

    public void IncreaseIndent() => _indent++;

    public void ReduceIndent() => _indent--;

    public void OpenBlock()
    {
        AppendIndent().Append('{').AppendLine();
        IncreaseIndent();
    }

    public void CloseBlock()
    {
        ReduceIndent();
        AppendIndent().Append('}').AppendLine();
    }

    public IDisposable BlockScope()
    {
        OpenBlock();
        return new DisposableCallback(_closeBlockScope);
    }

    public override string ToString() => _sb.ToString();
}

public readonly struct LineBuilder
{
    private readonly CodeBuilder _cb;

    public CodeBuilder CodeBuilder => _cb;

    public LineBuilder(CodeBuilder cb)
    {
        _cb = cb;
        _cb.AppendIndent();
    }

    public LineBuilder Indent
    {
        get
        {
            _cb.Append(CodeBuilder.Indent);
            return this;
        }
    }

    public LineBuilder this[string value]
    {
        get
        {
            _cb.Append(value);
            return this;
        }
    }

    public LineBuilder this[char value]
    {
        get
        {
            _cb.Append(value);
            return this;
        }
    }

    public LineBuilder this[bool condition, string value]
    {
        get
        {
            if (condition)
                _cb.Append(value);
            return this;
        }
    }

    public static LineBuilder operator | (LineBuilder builder, string value)
    {
        return builder[value];
    }

    public static LineBuilder operator | (LineBuilder builder, char value)
    {
        return builder[value];
    }

    public void End() => _cb.AppendLine();

    public void Continue() { }
}

public static class LineBuilderExtensions
{
    public static LineBuilder QualifiedName(this LineBuilder lineBuilder, ITypeSymbol typeSymbol)
    {
        // return lineBuilder[typeSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]['.'][typeSymbol.Name];
        return lineBuilder[typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)];
    }

    public static LineBuilder AppendMethodDefinitionString(this LineBuilder builder, IMethodSymbol method)
    {
        CodeBuilder cb = builder.CodeBuilder;

        cb.Append(method.Name).Append('(');

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            switch (method.Parameters[i].RefKind)
            {
                case RefKind.None:
                    break;
                case RefKind.Ref:
                    cb.Append("ref ");
                    break;
                case RefKind.Out:
                    cb.Append("out ");
                    break;
                case RefKind.In:
                    cb.Append("in ");
                    break;
            }

            cb.Append(method.Parameters[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(' ').Append(method.Parameters[i].Name);
            if (i < method.Parameters.Length - 1)
                cb.Append(", ");
        }

        cb.Append(')');
        return builder;
    }

    public static LineBuilder AppendMethodCallString(this LineBuilder lineBuilder, IMethodSymbol method)
    {
        var cb = lineBuilder.CodeBuilder;

        cb.Append(method.Name).Append('(');

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            switch (method.Parameters[i].RefKind)
            {
                case RefKind.None:
                    break;
                case RefKind.Ref:
                    cb.Append("ref ");
                    break;
                case RefKind.Out:
                    cb.Append("out ");
                    break;
                case RefKind.In:
                    cb.Append("in ");
                    break;
            }

            cb.Append(method.Parameters[i].Name);
            if (i < method.Parameters.Length - 1)
                cb.Append(", ");
        }

        cb.Append(')');
        return lineBuilder;
    }
}

public class DisposableCallback(Action callback) : IDisposable
{
    public void Dispose() => callback.Invoke();
}