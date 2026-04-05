namespace TM_GenericMapping.Common;

public static class Time
{
    public static ulong Millis(float seconds) => (ulong)(seconds * 1000);
    public static float Seconds(ulong millis) => (float)millis / 1000f;
    public static float Seconds(long millis) => (float)millis / 1000f;
}
