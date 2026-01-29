namespace TM_GenericMapping.Common
{
    public static class ExceptionUtils
    {
        public static void Ensure(bool val, Func<Exception> exceptionFactory)
        {
            if (!val)
                throw exceptionFactory();
        }
        public static void Ensure<T>(bool val) where T : Exception, new()
        {
            if (!val)
                throw new T();
        }
    }
}
