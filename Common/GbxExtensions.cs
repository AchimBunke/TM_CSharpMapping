using GBX.NET;
using GBX.NET.LZO;
using GBX.NET.ZLib;

namespace TM_GenericMapping.Common;

public static class GbxExtensions
{
    public static void Setup()
    {
        Gbx.LZO = new MiniLZO();
        Gbx.ZLib = new ZLib();
    }
}
