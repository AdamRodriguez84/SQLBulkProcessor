using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SQLBulkProcessor.Internal;

internal static class EntityMetadataFactory
{
    private static readonly ConcurrentDictionary<CacheKey, EntityMetadata> Cache = new();

    private readonly record struct CacheKey(IModel Model, Type ClrType);

    public static EntityMetadata Get(DbContext context, Type clrType)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clrType);

        return Cache.GetOrAdd(new CacheKey(context.Model, clrType), static key => Create(key.Model, key.ClrType));
    }

    internal static EntityMetadata Create(IModel model, Type clrType)
    {
        var entityType = model.FindEntityType(clrType)
            ?? model.FindRuntimeEntityType(clrType)
            ?? throw new InvalidOperationException(
                $"Type '{clrType.FullName}' is not mapped as an entity on the current DbContext.");

        if (entityType.IsOwned())
        {
            throw new InvalidOperationException(
                $"Owned type '{clrType.FullName}' cannot be bulk-processed on its own. Bulk the owning entity instead.");
        }

        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"Entity '{entityType.DisplayName()}' is not mapped to a SQL table.");

        var schema = entityType.GetSchema();
        var storeObject = StoreObjectIdentifier.Table(tableName, schema);
        var periodStart = entityType.FindAnnotation("SqlServer:TemporalPeriodStartPropertyName")?.Value as string;
        var periodEnd = entityType.FindAnnotation("SqlServer:TemporalPeriodEndPropertyName")?.Value as string;

        var columns = new List<ColumnMapping>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in WalkMappedColumns(entityType))
        {
            var columnName = candidate.Property.GetColumnName(storeObject);
            if (string.IsNullOrEmpty(columnName) || !seen.Add(columnName))
                continue;

            columns.Add(CreateColumn(
                model,
                entityType,
                candidate,
                columnName,
                storeObject,
                periodStart,
                periodEnd));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.DisplayName()}' has no mapped table columns.");
        }

        return new EntityMetadata(entityType, tableName, schema, columns);
    }

    private static ColumnMapping CreateColumn(
        IModel model,
        IEntityType rootEntityType,
        PropertyWalk candidate,
        string columnName,
        StoreObjectIdentifier storeObject,
        string? periodStart,
        string? periodEnd)
    {
        var property = candidate.Property;
        var isPeriod = property.Name == periodStart || property.Name == periodEnd;
        var isIdentity = IsIdentity(property);
        var isComputed = property.GetComputedColumnSql(storeObject) is not null
            || property.ValueGenerated == ValueGenerated.OnAddOrUpdate
               && property.GetBeforeSaveBehavior() == PropertySaveBehavior.Ignore
               && !property.IsConcurrencyToken
               && !isIdentity;
        var isRowVersion = property.IsConcurrencyToken
            && property.ValueGenerated == ValueGenerated.OnAddOrUpdate
            && (property.ClrType == typeof(byte[]) || IsRowVersionStoreType(property.GetColumnType(storeObject)));

        var getter = CreateGetter(model, rootEntityType, candidate);
        var setter = candidate.Path.Count == 0 ? PropertyAccessor.CreateSetter(property) : null;

        return new ColumnMapping(
            propertyName: property.Name,
            propertyPath: candidate.Path.Count == 0 ? property.Name : candidate.DisplayPath,
            columnName: columnName,
            storeType: StoreTypeMapper.GetStoreType(property, storeObject),
            clrType: property.ClrType,
            isNullable: property.IsNullable,
            isKey: property.IsPrimaryKey() && candidate.Path.Count == 0,
            isIdentity: isIdentity,
            isComputed: isComputed && !isRowVersion && !isPeriod,
            isRowVersion: isRowVersion,
            isPeriod: isPeriod,
            converter: property.GetValueConverter(),
            getter: getter,
            setter: setter);
    }

    private static Func<object, object?> CreateGetter(IModel model, IEntityType rootEntityType, PropertyWalk candidate)
    {
        var property = candidate.Property;
        if (property.IsShadowProperty())
        {
            if (property == rootEntityType.FindDiscriminatorProperty()
                || rootEntityType.GetAllBaseTypesInclusive().Any(t => t.FindDiscriminatorProperty() == property))
            {
                return entity =>
                {
                    var actual = model.FindRuntimeEntityType(entity.GetType()) ?? rootEntityType;
                    return actual.GetDiscriminatorValue();
                };
            }

            throw new InvalidOperationException(
                $"Shadow property '{property.Name}' on '{rootEntityType.DisplayName()}' cannot be read from detached entities.");
        }

        var propertyGetter = PropertyAccessor.CreateGetter(property);
        var declaringClr = property.DeclaringType.ClrType;
        if (!declaringClr.IsAssignableFrom(rootEntityType.ClrType))
        {
            var inner = propertyGetter;
            propertyGetter = entity => declaringClr.IsInstanceOfType(entity) ? inner(entity) : null;
        }

        if (candidate.Path.Count == 0)
            return propertyGetter;

        var navGetters = candidate.Path.Select(PropertyAccessor.CreateGetter).ToArray();
        return entity =>
        {
            object? current = entity;
            foreach (var getNav in navGetters)
            {
                current = getNav(current);
                if (current is null)
                    return null;
            }

            return propertyGetter(current);
        };
    }

    private static IEnumerable<PropertyWalk> WalkMappedColumns(IEntityType entityType)
    {
        foreach (var type in entityType.GetDerivedTypesInclusive())
        {
            foreach (var walk in WalkProperties(type, path: [], propertyPath: type.ClrType.Name))
                yield return walk;
        }
    }

    private static IEnumerable<PropertyWalk> WalkProperties(
        IEntityType entityType,
        IReadOnlyList<INavigation> path,
        string propertyPath)
    {
        foreach (var property in entityType.GetProperties())
        {
            if (property.IsIndexerProperty())
                continue;

            yield return new PropertyWalk(property, path, path.Count == 0 ? property.Name : propertyPath + "." + property.Name);
        }

        foreach (var navigation in entityType.GetNavigations())
        {
            if (!navigation.TargetEntityType.IsOwned() || navigation.IsCollection)
                continue;

            var childPath = path.Append(navigation).ToArray();
            var childDisplay = path.Count == 0 ? navigation.Name : propertyPath + "." + navigation.Name;
            foreach (var child in WalkProperties(navigation.TargetEntityType, childPath, childDisplay))
                yield return child;
        }
    }

    private static bool IsIdentity(IProperty property)
    {
        if (property.GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn)
            return true;

        return property.ValueGenerated == ValueGenerated.OnAdd
               && property.IsPrimaryKey()
               && IsInteger(property.ClrType)
               && property.GetComputedColumnSql() is null;
    }

    private static bool IsInteger(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);
    }

    private static bool IsRowVersionStoreType(string? storeType)
        => storeType is not null
           && (storeType.Equals("rowversion", StringComparison.OrdinalIgnoreCase)
               || storeType.Equals("timestamp", StringComparison.OrdinalIgnoreCase));

    private sealed record PropertyWalk(IProperty Property, IReadOnlyList<INavigation> Path, string DisplayPath);
}
