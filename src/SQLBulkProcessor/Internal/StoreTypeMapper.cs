using Microsoft.EntityFrameworkCore.Metadata;

namespace SQLBulkProcessor.Internal;

internal static class StoreTypeMapper
{
    public static string GetStoreType(IProperty property, StoreObjectIdentifier storeObject)
    {
        var configured = property.GetColumnType(storeObject) ?? property.GetColumnType();
        if (!string.IsNullOrWhiteSpace(configured) && !IsIdentityDecorated(configured))
            return StripIdentity(configured);

        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (type.IsEnum)
            type = Enum.GetUnderlyingType(type);

        if (type == typeof(string))
        {
            var max = property.GetMaxLength();
            return max is null or < 0 ? "nvarchar(max)" : $"nvarchar({max})";
        }

        if (type == typeof(byte[]))
        {
            var max = property.GetMaxLength();
            return max is null or < 0 ? "varbinary(max)" : $"varbinary({max})";
        }

        if (type == typeof(decimal) || type == typeof(decimal?))
        {
            var precision = property.GetPrecision() ?? 18;
            var scale = property.GetScale() ?? 2;
            return $"decimal({precision},{scale})";
        }

        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "bigint";
        if (type == typeof(short)) return "smallint";
        if (type == typeof(byte)) return "tinyint";
        if (type == typeof(bool)) return "bit";
        if (type == typeof(Guid)) return "uniqueidentifier";
        if (type == typeof(DateTime)) return "datetime2";
        if (type == typeof(DateTimeOffset)) return "datetimeoffset";
        if (type == typeof(TimeSpan)) return "time";
        if (type == typeof(double)) return "float";
        if (type == typeof(float)) return "real";
        if (type == typeof(DateOnly)) return "date";
        if (type == typeof(TimeOnly)) return "time";

        throw new NotSupportedException(
            $"No SQL store type mapping for '{type.FullName}' on property '{property.Name}'. Configure HasColumnType in the model.");
    }

    private static bool IsIdentityDecorated(string storeType)
        => storeType.Contains("identity", StringComparison.OrdinalIgnoreCase);

    private static string StripIdentity(string storeType)
    {
        var index = storeType.IndexOf("identity", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return storeType;

        return storeType[..index].Trim();
    }
}
