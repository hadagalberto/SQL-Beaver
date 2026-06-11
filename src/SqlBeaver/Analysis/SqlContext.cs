namespace SqlBeaver.Analysis
{
    public enum SqlContextKind
    {
        /// <summary>Não sugerir nada (comentário, string, contexto desconhecido).</summary>
        None,

        /// <summary>Após FROM/INTO/UPDATE: tabelas qualificadas (alias automático só em FROM).</summary>
        AfterFromJoin,

        /// <summary>Após JOIN: sugestões de FK no topo + tabelas.</summary>
        AfterJoin,

        /// <summary>Após "x.": alias → colunas; schema → tabelas.</summary>
        AfterDot,

        /// <summary>Após SELECT/WHERE/ON/AND/OR/HAVING/BY/SET ou vírgula no nível 0: colunas do escopo.</summary>
        ColumnContext,

        /// <summary>Digitação livre de identificador: tabelas + schemas.</summary>
        FreeIdentifier,
    }

    public sealed class SqlContext
    {
        public static readonly SqlContext None = new SqlContext(SqlContextKind.None, null, string.Empty, -1);

        public SqlContextKind Kind { get; }

        /// <summary>Identificador antes do ponto, sem colchetes (apenas para AfterDot).</summary>
        public string DotPrefix { get; }

        /// <summary>Identificador parcial já digitado (pode ser vazio).</summary>
        public string Partial { get; }

        /// <summary>Posição (no texto analisado) onde o parcial começa; -1 para None.</summary>
        public int PartialStart { get; }

        /// <summary>Keyword que disparou o contexto ("FROM"/"JOIN"/"INTO"/"UPDATE"); null nos demais.</summary>
        public string TriggerKeyword { get; }

        public SqlContext(SqlContextKind kind, string dotPrefix, string partial, int partialStart,
            string triggerKeyword = null)
        {
            Kind = kind;
            DotPrefix = dotPrefix;
            Partial = partial ?? string.Empty;
            PartialStart = partialStart;
            TriggerKeyword = triggerKeyword;
        }
    }
}
