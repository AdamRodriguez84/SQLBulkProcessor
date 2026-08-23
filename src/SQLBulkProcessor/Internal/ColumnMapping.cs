using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SQLBulkProcessor.Internal;

internal sealed class ColumnMapping
{
    private readonly Func<object, object?> _getter;
    private readonly Action<object, object?>? _setter;
    private readonly ValueConverter? _converter;

    public ColumnMapping(
        string propertyName,
        string propertyPath,
        string columnName,
        string storeType,
        Type clrType,
        bool isNullable,
        bool isKey,
        bool isIdentity,
        bool isComputed,
        bool isRowVersion,
        bool isPeriod,
        ValueConverter? converter,
        Func<object, object?> getter,
        Action<object, object?>? setter)
    {
        PropertyName = propertyName;
        PropertyPath = propertyPath;
        ColumnName = columnName;
        StoreType = storeType;
        ClrType = clrType;
        IsNullable = isNullable;
        IsKey = isKey;
        IsIdentity = isIdentity;
        IsComputed = isComputed;
        IsRowVersion = isRowVersion;
        IsPeriod = isPeriod;
        _converter = converter;
        _getter = getter;
        _setter = setter;
        ProviderClrType = converter?.ProviderClrType ?? Nullable.GetUnderlyingType(clrType) ?? clrType;
    }

    public string PropertyName { get; }
    public string PropertyPath { get; }
    public string ColumnName { get; }
    public string StoreType { get; }
    public Type ClrType { get; }
    public Type ProviderClrType { get; }
    public bool IsNullable { get; }
    public bool IsKey { get; }
    public bool IsIdentity { get; }
    public bool IsComputed { get; }
    public bool IsRowVersion { get; }
    public bool IsPeriod { get; }
    public bool CanSet => _setter is not null;

    public bool IsStoreGenerated => IsIdentity || IsComputed || IsRowVersion || IsPeriod;

    public object? GetValue(object entity)
    {
        var raw = _getter(entity);
        if (_converter is not null)
            raw = _converter.ConvertToProvider(raw);

        if (raw is Enum enumValue)
            return Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()), CultureInfo.InvariantCulture);

        return raw;
    }

    public void SetValue(object entity, object? value)
    {
        if (_setter is null)
            throw new InvalidOperationException($"Property '{PropertyPath}' is not settable.");

        if (value is null || value is DBNull)
        {
            _setter(entity, null);
            return;
        }

        var target = Nullable.GetUnderlyingType(ClrType) ?? ClrType;
        if (target.IsEnum && value is not Enum)
            value = Enum.ToObject(target, value);

        if (value is not null && value.GetType() != target && value is IConvertible)
            value = Convert.ChangeType(value, target, CultureInfo.InvariantCulture);

        if (_converter is not null)
            value = _converter.ConvertFromProvider(value);

        _setter(entity, value);
    }

    public bool Matches(string name)
        => ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase)
           || PropertyName.Equals(name, StringComparison.OrdinalIgnoreCase)
           || PropertyPath.Equals(name, StringComparison.OrdinalIgnoreCase);
}
