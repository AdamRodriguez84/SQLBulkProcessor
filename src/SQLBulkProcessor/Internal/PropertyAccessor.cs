using System.Linq.Expressions;
using System.Reflection;

namespace SQLBulkProcessor.Internal;

internal static class PropertyAccessor
{
    public static Func<object, object?> CreateGetter(IPropertyBase property)
    {
        var member = (MemberInfo?)property.PropertyInfo ?? property.FieldInfo
            ?? throw new InvalidOperationException($"Cannot read '{property.Name}' on '{property.DeclaringType.DisplayName()}'.");

        return CompileGetter(member);
    }

    public static Action<object, object?>? CreateSetter(IPropertyBase property)
    {
        var propertyInfo = property.PropertyInfo;
        if (propertyInfo?.SetMethod is not null)
            return CompileSetter(propertyInfo);

        var field = property.FieldInfo;
        if (field is not null && !field.IsInitOnly)
            return CompileSetter(field);

        return null;
    }

    private static Func<object, object?> CompileGetter(MemberInfo member)
    {
        var entity = Expression.Parameter(typeof(object), "entity");
        var declaringType = member.DeclaringType
            ?? throw new InvalidOperationException($"Member '{member.Name}' has no declaring type.");
        var typed = Expression.Convert(entity, declaringType);
        Expression body = member switch
        {
            PropertyInfo property => Expression.Property(typed, property),
            FieldInfo field => Expression.Field(typed, field),
            _ => throw new InvalidOperationException($"Unsupported member '{member.Name}'.")
        };

        if (body.Type.IsValueType)
            body = Expression.Convert(body, typeof(object));

        return Expression.Lambda<Func<object, object?>>(body, entity).Compile();
    }

    private static Action<object, object?> CompileSetter(MemberInfo member)
    {
        var entity = Expression.Parameter(typeof(object), "entity");
        var value = Expression.Parameter(typeof(object), "value");
        var declaringType = member.DeclaringType
            ?? throw new InvalidOperationException($"Member '{member.Name}' has no declaring type.");
        var typed = Expression.Convert(entity, declaringType);

        Expression target = member switch
        {
            PropertyInfo property => Expression.Property(typed, property),
            FieldInfo field => Expression.Field(typed, field),
            _ => throw new InvalidOperationException($"Unsupported member '{member.Name}'.")
        };

        var converted = Expression.Convert(value, target.Type);
        var assign = Expression.Assign(target, converted);
        return Expression.Lambda<Action<object, object?>>(assign, entity, value).Compile();
    }
}
