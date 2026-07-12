using GBX.NET;

namespace TM_GenericMapping.Common;

public static class IdentUtils
{
    public static string Author = "Ach1oto";
    public static int Collection = 26;
    public static Ident Create(string block)
        => new Ident(block, Collection, Author);
}
