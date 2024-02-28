using System.Xml.Linq;

namespace rcopy2;

record FlagedArg(bool Flag, string Arg);

static class Options
{
    static IEnumerable<FlagedArg> SelectWithFlag(IEnumerable<FlagedArg> args,
        string name, string? shortcut)
    {
        var itrThe = args.GetEnumerator();
        while (itrThe.MoveNext())
        {
            var current = itrThe.Current;
            if ((current.Arg == name) || (shortcut?.Equals(current.Arg) ?? false))
            {
                if (itrThe.MoveNext())
                {
                    yield return new FlagedArg(true, itrThe.Current.Arg);
                }
                else
                {
                    throw new ArgumentException(
                        $"Missing value to {name}");
                }
            }
            else
            {
                yield return current;
            }
        }
    }

    public static (string, IEnumerable<FlagedArg>) Parse(string name,
        IEnumerable<FlagedArg> args, string? shortcut = null)
    {
        var qryFlaged = SelectWithFlag(args, name, shortcut)
            .GroupBy((it) => it.Flag)
            .ToDictionary((grp) => grp.Key,
            elementSelector: (grp) => grp.AsEnumerable());

        IEnumerable<FlagedArg>? qryNotFound;
        if (false == qryFlaged.TryGetValue(false, out qryNotFound))
        {
            qryNotFound = Array.Empty<FlagedArg>();
        }

        if (qryFlaged.TryGetValue(true, out var qryFound))
        {
            var foundThe = qryFound.ToArray();
            if (foundThe.Length > 1)
            {
                throw new ArgumentException(
                    $"Too many value ('{foundThe[0].Arg}','{foundThe[1].Arg}') to {name}");
            }
            return (foundThe[0].Arg, qryNotFound);
        }
        return (string.Empty, qryNotFound);
    }

    public static (string[], IEnumerable<FlagedArg>) ParseForStrings(
        string name, IEnumerable<FlagedArg> args, string? shortcut = null)
    {
        var qryFlaged = SelectWithFlag(args, name, shortcut)
            .GroupBy((it) => it.Flag)
            .ToDictionary((grp) => grp.Key,
            elementSelector: (grp) => grp.AsEnumerable());

        IEnumerable<FlagedArg>? qryNotFound;
        if (false == qryFlaged.TryGetValue(false, out qryNotFound))
        {
            qryNotFound = Array.Empty<FlagedArg>();
        }

        if (qryFlaged.TryGetValue(true, out var qryFound))
        {
            return (qryFound.Select((it) => it.Arg).ToArray(),
                qryNotFound);
        }
        return (Array.Empty<string>(), qryNotFound);
    }
}
