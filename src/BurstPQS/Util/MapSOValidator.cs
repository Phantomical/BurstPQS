using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using BurstPQS.Map;

namespace BurstPQS.Util;

internal static class MapSOValidator
{
    static readonly Dictionary<Type, Action<PQSMod>> Validators = [];

    internal static void Validate(PQSMod mod)
    {
        if (mod is null)
            return;

        var type = mod.GetType();

        if (!Validators.TryGetValue(type, out var validator))
        {
            validator = BuildValidator(type);
            Validators[type] = validator;
        }

        validator?.Invoke(mod);
    }

    static Action<PQSMod> BuildValidator(Type type)
    {
        var fields = type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );

        var param = Expression.Parameter(typeof(PQSMod), "mod");
        var typedMod = Expression.Variable(type, "typed");
        var body = new List<Expression>
        {
            Expression.Assign(typedMod, Expression.Convert(param, type)),
        };

        var throwMethod = typeof(MapSOValidator).GetMethod(
            nameof(ThrowIfUnsupported),
            BindingFlags.Static | BindingFlags.NonPublic
        );

        foreach (var field in fields)
        {
            if (!typeof(MapSO).IsAssignableFrom(field.FieldType))
                continue;

            body.Add(
                Expression.Call(
                    throwMethod,
                    Expression.Convert(Expression.Field(typedMod, field), typeof(MapSO)),
                    Expression.Constant(field.Name)
                )
            );
        }

        // No MapSO fields found
        if (body.Count == 1)
            return null;

        var lambda = Expression.Lambda<Action<PQSMod>>(Expression.Block([typedMod], body), param);
        return lambda.Compile();
    }

    static void ThrowIfUnsupported(MapSO value, string fieldName)
    {
        if (value.IsNullOrDestroyed())
            return;
        if (BurstMapSO.IsSupported(value))
            return;

        throw new UnsupportedPQSModException(
            $"MapSO field '{fieldName}' has unsupported type {value.GetType().FullName}"
        );
    }
}
