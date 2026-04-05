using GBX.NET;
using GBX.NET.LZO;
using GBX.NET.ZLib;

namespace TM_GenericMapping.Common;

public static class GbxExtensions
{
    /// <summary>
    /// Slightly better compression than MiniLZO
    /// </summary>
    public static void Setup()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
    }

    public static void SetupMiniLZO()
    {
        Gbx.LZO = new MiniLZO();
        Gbx.ZLib = new ZLib();
    }
}
