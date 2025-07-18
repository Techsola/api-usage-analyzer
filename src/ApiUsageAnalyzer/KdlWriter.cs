using System.Buffers;
using System.Globalization;

namespace ApiUsageAnalyzer;

public sealed class KdlWriter(TextWriter writer)
{
    private static readonly NumberFormatInfo DefaultNumberFormat = new()
    {
        NumberGroupSeparator = "_",
        NumberGroupSizes = [3],
    };

    private static readonly string indentation = "  ";
    private int nodeDepth;
    private bool atLineStart = true;
    private bool insideNode;
    private bool singleLineMode;
    private bool isAfterProperty;

    public SingleLineModeScope EnterSingleLineMode()
    {
        singleLineMode = true;
        return new SingleLineModeScope(this);
    }

    public readonly struct SingleLineModeScope(KdlWriter writer) : IDisposable
    {
        public void Dispose() => writer.singleLineMode = false;
    }

    public void StartNode(string name)
    {
        CheckIncompleteProperty();

        if (insideNode)
        {
            writer.Write(" {");

            if (!singleLineMode)
                writer.WriteLine();
        }
        else
        {
            if (!atLineStart)
                writer.Write(';');
        }

        if (singleLineMode)
        {
            writer.Write(' ');
        }
        else
        {
            for (var i = 0; i < nodeDepth; i++)
                writer.Write(indentation);
        }

        writer.Write(name);
        insideNode = true;
        atLineStart = false;

        nodeDepth++;
    }

    public void EndNode()
    {
        CheckIncompleteProperty();

        nodeDepth--;

        if (insideNode)
        {
            if (!singleLineMode)
            {
                writer.WriteLine();
                atLineStart = true;
            }
            insideNode = false;
        }
        else if (!atLineStart)
        {
            writer.Write(" }");
            if (!singleLineMode)
            {
                writer.WriteLine();
                atLineStart = true;
            }
        }
        else
        {
            for (var i = 0; i < nodeDepth; i++)
                writer.Write(indentation);

            writer.WriteLine('}');
            atLineStart = true;
        }
    }

    public void WritePropertyName(string name, bool forceQuotes = false)
    {
        CheckIncompleteProperty();
        WriteStringValue(name, forceQuotes);
        writer.Write('=');
        isAfterProperty = true;
    }

    private void CheckIncompleteProperty()
    {
        if (isAfterProperty)
            throw new InvalidOperationException("A value must be written next because a property has been started.");
    }

    private void StartValue()
    {
        if (isAfterProperty)
            isAfterProperty = false;
        else
            writer.Write(' ');
    }

    public void WriteStringValue(string value, bool forceQuotes = false)
    {
        StartValue();

        if (!forceQuotes && IsValidIdentifierString(value))
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');

        var remainingValue = value.AsSpan();

        while (remainingValue.IndexOfAny(CharactersRequiringEscaping) is (not -1) and var indexRequiringEscaping)
        {
            writer.Write(remainingValue[..indexRequiringEscaping]);
            
            var charToEscape = remainingValue[indexRequiringEscaping];
            remainingValue = remainingValue[(indexRequiringEscaping + 1)..];

            switch (charToEscape)
            {
                case '"':
                    writer.Write("\\\"");
                    break;
                case '\\':
                    writer.Write("\\\\");
                    break;
                case '\r':
                    writer.Write("\\r");
                    break;
                case '\n':
                    writer.Write("\\n");
                    break;
                case '\b':
                    writer.Write("\\b");
                    break;
                case '\f':
                    writer.Write("\\f");
                    break;
                default:
                    if (char.IsSurrogate(charToEscape))
                        throw new NotImplementedException("TODO: write the actual code point formed by the pair, which will take more than 4 hex digits");
                    
                    writer.Write("\\u");

                    // If the next character is a hex digit, the only way to avoid confusion is to write the maximum number of hex digits.
                    var format = !remainingValue.IsEmpty && char.IsAsciiHexDigit(remainingValue[0]) ? "x6" : "x";
                    writer.Write(((ushort)charToEscape).ToString(format, CultureInfo.InvariantCulture));
                    break;
            }
        }

        writer.Write(remainingValue);
        writer.Write('"');
    }

    public void WriteNumberValue(int value)
    {
        StartValue();
        writer.Write(value.ToString("N0", DefaultNumberFormat));
    }

    // https://kdl.dev/spec/#newline
    private static readonly char[] NewLineChars = [.. "\r\n\u0085\v\f\u2028\u2029"];

    // https://kdl.dev/spec/#disallowed-literal-code-points
    private static readonly char[] DisallowedLiteralCodePoints = [
        .. char.Range('\0', '\u0008'), .. char.Range('\u000e', '\u001f'),
        '\u007f',
        .. char.Range('\ud800', '\udfff'),
        .. char.Range('\u200e', '\u200f'), .. char.Range('\u202a', '\u202e'), .. char.Range('\u2066', '\u2069'),
        '\ufeff'];

    // https://kdl.dev/spec/#name-quoted-string
    private static readonly SearchValues<char> CharactersRequiringEscaping = SearchValues.Create(['"', '\\', .. NewLineChars, .. DisallowedLiteralCodePoints]);

    private static readonly SearchValues<char> InvalidIdentifierStringChars = SearchValues.Create([
        // https://kdl.dev/spec/#non-identifier-characters
        .. "(){}[]/\\\"#;=",
        .. "\t \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u202F\u205F\u3000", // https://kdl.dev/spec/#whitespace
        .. NewLineChars,
        .. DisallowedLiteralCodePoints,
    ]);

    private static bool IsValidIdentifierString(string value)
    {
        return
            value is not []
                // https://kdl.dev/spec/#name-non-initial-characters
                and not [>= '0' and <= '9', ..]
                and not ['-' or '+' or '.', >= '0' and <= '9', ..]
                and not ['-' or '+', '.', >= '0' and <= '9', ..]
            && !value.ContainsAny(InvalidIdentifierStringChars);
    }

    public void WriteLine()
    {
        if (!atLineStart)
            throw new NotSupportedException($"{nameof(WriteLine)} is currently only supported when there is not yet anything else on the line.");

        writer.WriteLine();
    }
}
