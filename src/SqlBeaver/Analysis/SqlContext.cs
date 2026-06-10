namespace SqlBeaver.Analysis
{
    public enum SqlContextKind
    {
        /// <summary>Não sugerir nada (comentário, string, contexto desconhecido).</summary>
        None,

        /// <summary>Após FROM/JOIN/INTO/UPDATE: sugerir schemas + tabelas qualificadas.</summary>
        AfterFromJoin,

        /// <summary>Após "schema.": sugerir somente tabelas daquele schema.</summary>
        AfterSchemaDot,

        /// <summary>Digitação livre de identificador: sugerir schemas + tabelas.</summary>
        FreeIdentifier,
    }

    public sealed class SqlContext
    {
        public static readonly SqlContext None = new SqlContext(SqlContextKind.None, null, string.Empty, -1);

        public SqlContextKind Kind { get; }

        /// <summary>Schema antes do ponto, sem colchetes (apenas para AfterSchemaDot).</summary>
        public string SchemaPrefix { get; }

        /// <summary>Identificador parcial já digitado (pode ser vazio).</summary>
        public string Partial { get; }

        /// <summary>Posição (no texto analisado) onde o parcial começa; -1 para None.</summary>
        public int PartialStart { get; }

        public SqlContext(SqlContextKind kind, string schemaPrefix, string partial, int partialStart)
        {
            Kind = kind;
            SchemaPrefix = schemaPrefix;
            Partial = partial ?? string.Empty;
            PartialStart = partialStart;
        }
    }
}
