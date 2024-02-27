namespace rcopy2;

record FlagedArg(bool Flag, string Arg);

static class Options
{
    public static (string, IEnumerable<FlagedArg>) Get(string name,
        IEnumerable<FlagedArg> args, string? shortcut = null)
    {
        IEnumerable<FlagedArg> SelectFlag()
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

        (string, IEnumerable<FlagedArg>) GetOthers()
        {
            var qryFlaged = SelectFlag()
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

        return GetOthers();
    }

    public static (bool, IEnumerable<FlagedArg>) Flag(string name,
        IEnumerable<FlagedArg> args)
    {
        IEnumerable<FlagedArg> SelectFlag()
        {
            var itrThe = args.GetEnumerator();
            while (itrThe.MoveNext())
            {
                var current = itrThe.Current;
                if (current.Arg == name)
                {
                    yield return new FlagedArg(true, itrThe.Current.Arg);
                }
                else
                {
                    yield return current;
                }
            }
        }

        (bool, IEnumerable<FlagedArg>) GetOthers()
        {
            var qryFlaged = SelectFlag()
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
                return (true, qryNotFound);
            }
            return (false, qryNotFound);
        }

        return GetOthers();
    }
}
