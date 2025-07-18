using System.Collections;
using Microsoft.VisualStudio.Services.Common;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ApiUsageAnalyzer;

internal static partial class Extensions
{
    [GeneratedRegex(@"^VS(?<code>\d+):", RegexOptions.IgnoreCase)]
    private static partial Regex VssMessageErrorCode();

    public static int? GetVssErrorCode(this VssException exception)
    {
        return VssMessageErrorCode().Match(exception.Message) is { Success: true } match 
            && int.TryParse(match.Groups["code"].Value, out var code) 
                ? code
                : null;
    }

    public static IEnumerable<XElement> ElementsWithLocalName(this XContainer container, string localName)
    {
        return container.Elements().Where(element => element.Name.LocalName == localName);
    }

    public static IEnumerable<T> NotNull<T>(this IEnumerable<T?> source) where T : class
    {
        return source.Where(item => item is not null)!;
    }

    extension(char)
    {
        public static CharRangeEnumerable Range(char start, char end) => new(start, end);
    }

    public readonly struct CharRangeEnumerable(char start, char end) : IEnumerable<char>
    {
        public CharRangeEnumerator GetEnumerator() => new(start, end);

        IEnumerator<char> IEnumerable<char>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct CharRangeEnumerator : IEnumerator<char>
        {
            private readonly char start;
            private readonly char end;
            private char? current;

            public CharRangeEnumerator(char start, char end)
            {
                if (end < start)
                    throw new ArgumentException("The end character must be greater than or equal to the start character.");

                this.start = start;
                this.end = end;
            }

            public char Current => current!.Value;

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (current is null)
                {
                    current = start;
                    return true;
                }

                if (current.Value == end)
                    return false;

                current = (char)(current.Value + 1);
                return true;
            }

            public void Reset() => current = null;

            public void Dispose() { }
        }
    }

    public static void Mirror<T>(this TaskCompletionSource<T> taskCompletionSource, Task<T> task)
    {
        task.ContinueWith(task =>
        {
            if (task.IsFaulted)
                taskCompletionSource.SetException(task.Exception!.InnerExceptions);
            else if (task.IsCanceled)
                taskCompletionSource.SetCanceled();
            else
                taskCompletionSource.SetResult(task.Result);
        });
    }

    public static (List<T> Matched, List<T> Unmatched) Partition<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        var matched = new List<T>();
        var unmatched = new List<T>();

        foreach (var item in source)
            (predicate(item) ? matched : unmatched).Add(item);

        return (matched, unmatched);
    }
}
