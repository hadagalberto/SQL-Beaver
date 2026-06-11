namespace SqlBeaver.Metadata
{
    /// <summary>Parâmetro de procedure ou função armazenada.</summary>
    public sealed class ParameterEntry
    {
        public string Name    { get; }
        public string SqlType { get; }
        public bool   IsOutput { get; }
        public int    Ordinal  { get; }

        public ParameterEntry(string name, string sqlType, bool isOutput, int ordinal)
        {
            Name     = name;
            SqlType  = sqlType;
            IsOutput = isOutput;
            Ordinal  = ordinal;
        }
    }
}
