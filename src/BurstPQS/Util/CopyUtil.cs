using System.Linq.Expressions;
using System.Reflection;

namespace BurstPQS.Util;

public static class CloneUtil
{
    public static void MemberwiseCopyTo<T>(T src, T dst)
        where T : class => CloneImpl<T>.CloneFunc(src, dst);

    static class CloneImpl<T>
        where T : class
    {
        internal delegate void CloneDelegate(T src, T dst);
        internal static readonly CloneDelegate CloneFunc = BuildMemberwiseCloneDelegate();

        private static CloneDelegate BuildMemberwiseCloneDelegate()
        {
            var fields = typeof(T).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            var src = Expression.Parameter(typeof(T), "src");
            var dst = Expression.Parameter(typeof(T), "dst");
            var stmts = new Expression[fields.Length];

            for (int i = 0; i < fields.Length; ++i)
            {
                stmts[i] = Expression.Assign(
                    Expression.Field(dst, fields[i]),
                    Expression.Field(src, fields[i])
                );
            }

            var block = Expression.Block(stmts);
            var lambda = Expression.Lambda<CloneDelegate>(block, src, dst);

            return lambda.Compile();
        }
    }
}
