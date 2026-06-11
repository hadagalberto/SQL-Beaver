namespace SqlBeaver.Metadata
{
    public sealed class ColumnEntry
    {
        public string Name { get; }
        /// <summary>Tipo SQL formatado, ex.: "varchar(250)", "decimal(18,2)".</summary>
        public string SqlType { get; }
        public bool IsNullable { get; }
        public bool IsPrimaryKey { get; }

        public ColumnEntry(string name, string sqlType, bool isNullable, bool isPrimaryKey)
        {
            Name = name;
            SqlType = sqlType;
            IsNullable = isNullable;
            IsPrimaryKey = isPrimaryKey;
        }
    }
}
