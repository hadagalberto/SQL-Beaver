namespace SqlBeaver.Metadata
{
    public enum DbObjectType { Procedure, View, ScalarFunction, TableFunction }

    public sealed class ObjectEntry
    {
        public string Schema { get; }
        public string Name { get; }
        public DbObjectType Type { get; }

        public ObjectEntry(string schema, string name, DbObjectType type)
        {
            Schema = schema;
            Name = name;
            Type = type;
        }
    }
}
