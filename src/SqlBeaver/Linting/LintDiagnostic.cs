namespace SqlBeaver.Linting
{
    /// <summary>
    /// Representa um diagnóstico de lint produzido por uma <see cref="ISqlLintRule"/>.
    /// </summary>
    public sealed class LintDiagnostic
    {
        public int    Line    { get; }
        public int    Column  { get; }
        public int    Length  { get; }
        public string RuleId  { get; }
        public string Message { get; }

        public LintDiagnostic(string ruleId, string message, int line, int column, int length)
        {
            RuleId  = ruleId;
            Message = message;
            Line    = line;
            Column  = column;
            Length  = length;
        }
    }
}
