using System.Data.Common;

namespace SQLBulkProcessor.Internal;

internal sealed class EntityDataReader<T> : DbDataReader
    where T : class
{
    private readonly IReadOnlyList<T> _entities;
    private readonly IReadOnlyList<ColumnMapping> _columns;
    private readonly bool _includeRowId;
    private readonly string[] _names;
    private int _index = -1;
    private bool _closed;

    public EntityDataReader(IReadOnlyList<T> entities, IReadOnlyList<ColumnMapping> columns, bool includeRowId)
    {
        _entities = entities;
        _columns = columns;
        _includeRowId = includeRowId;
        var offset = includeRowId ? 1 : 0;
        _names = new string[columns.Count + offset];
        if (includeRowId)
            _names[0] = "_BulkRowId";
        for (var i = 0; i < columns.Count; i++)
            _names[i + offset] = columns[i].ColumnName;
    }

    public override int FieldCount => _names.Length;
    public override bool HasRows => _entities.Count > 0;
    public override bool IsClosed => _closed;
    public override int RecordsAffected => -1;
    public override int Depth => 0;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        _index++;
        return _index < _entities.Count;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read());
    }

    public override string GetName(int ordinal) => _names[ordinal];

    public override int GetOrdinal(string name)
    {
        for (var i = 0; i < _names.Length; i++)
        {
            if (string.Equals(_names[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new IndexOutOfRangeException($"Column '{name}' was not found.");
    }

    public override Type GetFieldType(int ordinal)
    {
        if (_includeRowId && ordinal == 0)
            return typeof(int);

        var index = _includeRowId ? ordinal - 1 : ordinal;
        return _columns[index].ProviderClrType;
    }

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override object GetValue(int ordinal)
    {
        if (_includeRowId && ordinal == 0)
            return _index;

        var index = _includeRowId ? ordinal - 1 : ordinal;
        return _columns[index].GetValue(_entities[_index]) ?? DBNull.Value;
    }

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
            values[i] = GetValue(i);
        return count;
    }

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

    public override bool GetBoolean(int ordinal) => Convert.ToBoolean(GetValue(ordinal));
    public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal));
    public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal));
    public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(GetValue(ordinal));
    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal));
    public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal));
    public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal));
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal));
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal));
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal));
    public override string GetString(int ordinal) => Convert.ToString(GetValue(ordinal)) ?? string.Empty;

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();

    public override bool NextResult() => false;

    public override void Close() => _closed = true;

    public override System.Collections.IEnumerator GetEnumerator()
        => _entities.GetEnumerator();
}
