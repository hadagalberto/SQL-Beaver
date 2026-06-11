using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace SqlBeaver.Formatting
{
    /// <summary>
    /// Opções de formatação configuráveis via %LOCALAPPDATA%\SqlBeaver\format.json.
    /// Nomes de membro em camelCase correspondentes às chaves do arquivo JSON.
    /// Use <see cref="Load"/> para obter uma instância com defaults garantidos para
    /// membros ausentes no JSON.
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class FormatOptions
    {
        // ── Keyword casing ────────────────────────────────────────────────────

        /// <summary>"uppercase" | "lowercase" | "none" (mapeado para PascalCase no ScriptDom).</summary>
        [DataMember(Name = "keywordCasing")]
        public string KeywordCasing { get; set; }

        // ── Indentação ────────────────────────────────────────────────────────

        [DataMember(Name = "indentationSize")]
        public int IndentationSize { get; set; }

        // ── Alinhamento / layout ──────────────────────────────────────────────

        [DataMember(Name = "alignClauseBodies")]
        public bool AlignClauseBodies { get; set; }

        [DataMember(Name = "asKeywordOnOwnLine")]
        public bool AsKeywordOnOwnLine { get; set; }

        [DataMember(Name = "includeSemicolons")]
        public bool IncludeSemicolons { get; set; }

        [DataMember(Name = "indentSetClause")]
        public bool IndentSetClause { get; set; }

        // ── Quebras de linha antes de cláusulas ───────────────────────────────

        [DataMember(Name = "newLineBeforeFromClause")]
        public bool NewLineBeforeFromClause { get; set; }

        [DataMember(Name = "newLineBeforeWhereClause")]
        public bool NewLineBeforeWhereClause { get; set; }

        [DataMember(Name = "newLineBeforeGroupByClause")]
        public bool NewLineBeforeGroupByClause { get; set; }

        [DataMember(Name = "newLineBeforeOrderByClause")]
        public bool NewLineBeforeOrderByClause { get; set; }

        [DataMember(Name = "newLineBeforeHavingClause")]
        public bool NewLineBeforeHavingClause { get; set; }

        [DataMember(Name = "newLineBeforeJoinClause")]
        public bool NewLineBeforeJoinClause { get; set; }

        [DataMember(Name = "newLineBeforeOpenParenthesisInMultilineList")]
        public bool NewLineBeforeOpenParenthesisInMultilineList { get; set; }

        [DataMember(Name = "newLineBeforeCloseParenthesisInMultilineList")]
        public bool NewLineBeforeCloseParenthesisInMultilineList { get; set; }

        // ── Listas multiline ──────────────────────────────────────────────────

        [DataMember(Name = "multilineSelectElementsList")]
        public bool MultilineSelectElementsList { get; set; }

        [DataMember(Name = "multilineInsertSourcesList")]
        public bool MultilineInsertSourcesList { get; set; }

        [DataMember(Name = "multilineWherePredicatesList")]
        public bool MultilineWherePredicatesList { get; set; }

        [DataMember(Name = "multilineViewColumnsList")]
        public bool MultilineViewColumnsList { get; set; }

        // ── Factory / Load / Save ─────────────────────────────────────────────

        /// <summary>
        /// Serializa esta instância para JSON no mesmo formato que o arquivo format.json.
        /// </summary>
        public string Serialize()
        {
            return
                "{\r\n" +
                $"  \"keywordCasing\": \"{(KeywordCasing ?? "uppercase").Replace("\\", "\\\\").Replace("\"", "\\\"")}\",\r\n" +
                $"  \"indentationSize\": {IndentationSize},\r\n" +
                $"  \"alignClauseBodies\": {AlignClauseBodies.ToString().ToLowerInvariant()},\r\n" +
                $"  \"asKeywordOnOwnLine\": {AsKeywordOnOwnLine.ToString().ToLowerInvariant()},\r\n" +
                $"  \"includeSemicolons\": {IncludeSemicolons.ToString().ToLowerInvariant()},\r\n" +
                $"  \"indentSetClause\": {IndentSetClause.ToString().ToLowerInvariant()},\r\n" +
                $"  \"newLineBeforeFromClause\": {NewLineBeforeFromClause.ToString().ToLowerInvariant()},\r\n" +
                $"  \"newLineBeforeWhereClause\": {NewLineBeforeWhereClause.ToString().ToLowerInvariant()},\r\n" +
                $"  \"newLineBeforeGroupByClause\": {NewLineBeforeGroupByClause.ToString().ToLowerInvariant()},\r\n" +
                $"  \"newLineBeforeOrderByClause\": {NewLineBeforeOrderByClause.ToString().ToLowerInvariant()},\r\n" +
                $"  \"newLineBeforeHavingClause\": {NewLineBeforeHavingClause.ToString().ToLowerInvariant()},\r\n" +
                $"  \"newLineBeforeJoinClause\": {NewLineBeforeJoinClause.ToString().ToLowerInvariant()},\r\n" +
                $"  \"newLineBeforeOpenParenthesisInMultilineList\": {NewLineBeforeOpenParenthesisInMultilineList.ToString().ToLowerInvariant()},\r\n" +
                $"  \"newLineBeforeCloseParenthesisInMultilineList\": {NewLineBeforeCloseParenthesisInMultilineList.ToString().ToLowerInvariant()},\r\n" +
                $"  \"multilineSelectElementsList\": {MultilineSelectElementsList.ToString().ToLowerInvariant()},\r\n" +
                $"  \"multilineInsertSourcesList\": {MultilineInsertSourcesList.ToString().ToLowerInvariant()},\r\n" +
                $"  \"multilineWherePredicatesList\": {MultilineWherePredicatesList.ToString().ToLowerInvariant()},\r\n" +
                $"  \"multilineViewColumnsList\": {MultilineViewColumnsList.ToString().ToLowerInvariant()}\r\n" +
                "}\r\n";
        }

        /// <summary>Retorna uma instância com todos os defaults aplicados.</summary>
        public static FormatOptions CreateDefault() => new FormatOptions
        {
            KeywordCasing                                = "uppercase",
            IndentationSize                              = 4,
            AlignClauseBodies                            = false,
            AsKeywordOnOwnLine                           = false,
            IncludeSemicolons                            = true,
            IndentSetClause                              = false,
            NewLineBeforeFromClause                      = true,
            NewLineBeforeWhereClause                     = true,
            NewLineBeforeGroupByClause                   = true,
            NewLineBeforeOrderByClause                   = true,
            NewLineBeforeHavingClause                    = true,
            NewLineBeforeJoinClause                      = true,
            NewLineBeforeOpenParenthesisInMultilineList  = false,
            NewLineBeforeCloseParenthesisInMultilineList = false,
            MultilineSelectElementsList                  = true,
            MultilineInsertSourcesList                   = true,
            MultilineWherePredicatesList                 = false,
            MultilineViewColumnsList                     = false,
        };

        /// <summary>
        /// Desserializa o JSON em uma instância de <see cref="FormatOptions"/> com defaults
        /// garantidos para membros ausentes.
        /// JSON nulo, vazio ou inválido retorna defaults — nunca lança.
        /// O arquivo JSON é o objeto diretamente (sem array wrapper):
        /// <c>{ "keywordCasing": "lowercase", "indentationSize": 2 }</c>
        /// </summary>
        /// <remarks>
        /// Usa <see cref="JsonReaderWriterFactory"/> (leitura token-a-token) em vez de
        /// <see cref="DataContractJsonSerializer"/> para evitar a verificação de
        /// <c>RequiresMemberAccessForRead</c> que tenta carregar assemblies do VS Shell
        /// ausentes no ambiente de testes.
        /// </remarks>
        public static FormatOptions Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return CreateDefault();

            try
            {
                FormatOptions opts = CreateDefault();
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(
                    bytes, XmlDictionaryReaderQuotas.Max))
                {
                    // Advance past the root element (<root>) to the first child.
                    reader.Read(); // <root type="object">

                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element)
                            continue;

                        string key = reader.Name;   // JSON key = element local name

                        // JsonReaderWriterFactory maps JSON values to Text nodes inside the
                        // Element, not to the Element's own Value.  Structure:
                        //   Element(key) → Text(value) → EndElement(key)
                        // Advance to the Text child and read reader.Value.
                        if (!reader.Read() || reader.NodeType != XmlNodeType.Text)
                            continue;

                        string value = reader.Value;   // the raw JSON scalar value as string

                        switch (key)
                        {
                            case "keywordCasing":
                                opts.KeywordCasing = value;
                                break;
                            case "indentationSize":
                                if (int.TryParse(value, out int indSize))
                                    opts.IndentationSize = indSize;
                                break;
                            case "alignClauseBodies":
                                if (bool.TryParse(value, out bool acb))
                                    opts.AlignClauseBodies = acb;
                                break;
                            case "asKeywordOnOwnLine":
                                if (bool.TryParse(value, out bool akol))
                                    opts.AsKeywordOnOwnLine = akol;
                                break;
                            case "includeSemicolons":
                                if (bool.TryParse(value, out bool isc))
                                    opts.IncludeSemicolons = isc;
                                break;
                            case "indentSetClause":
                                if (bool.TryParse(value, out bool isclause))
                                    opts.IndentSetClause = isclause;
                                break;
                            case "newLineBeforeFromClause":
                                if (bool.TryParse(value, out bool nlbfc))
                                    opts.NewLineBeforeFromClause = nlbfc;
                                break;
                            case "newLineBeforeWhereClause":
                                if (bool.TryParse(value, out bool nlbwc))
                                    opts.NewLineBeforeWhereClause = nlbwc;
                                break;
                            case "newLineBeforeGroupByClause":
                                if (bool.TryParse(value, out bool nlbgbc))
                                    opts.NewLineBeforeGroupByClause = nlbgbc;
                                break;
                            case "newLineBeforeOrderByClause":
                                if (bool.TryParse(value, out bool nlbobc))
                                    opts.NewLineBeforeOrderByClause = nlbobc;
                                break;
                            case "newLineBeforeHavingClause":
                                if (bool.TryParse(value, out bool nlbhc))
                                    opts.NewLineBeforeHavingClause = nlbhc;
                                break;
                            case "newLineBeforeJoinClause":
                                if (bool.TryParse(value, out bool nlbjc))
                                    opts.NewLineBeforeJoinClause = nlbjc;
                                break;
                            case "newLineBeforeOpenParenthesisInMultilineList":
                                if (bool.TryParse(value, out bool nlbopml))
                                    opts.NewLineBeforeOpenParenthesisInMultilineList = nlbopml;
                                break;
                            case "newLineBeforeCloseParenthesisInMultilineList":
                                if (bool.TryParse(value, out bool nlbcpml))
                                    opts.NewLineBeforeCloseParenthesisInMultilineList = nlbcpml;
                                break;
                            case "multilineSelectElementsList":
                                if (bool.TryParse(value, out bool msel))
                                    opts.MultilineSelectElementsList = msel;
                                break;
                            case "multilineInsertSourcesList":
                                if (bool.TryParse(value, out bool misl))
                                    opts.MultilineInsertSourcesList = misl;
                                break;
                            case "multilineWherePredicatesList":
                                if (bool.TryParse(value, out bool mwpl))
                                    opts.MultilineWherePredicatesList = mwpl;
                                break;
                            case "multilineViewColumnsList":
                                if (bool.TryParse(value, out bool mvcl))
                                    opts.MultilineViewColumnsList = mvcl;
                                break;
                            // Unknown keys: silently ignored for forward-compatibility.
                        }
                    }
                }

                return opts;
            }
            catch
            {
                return CreateDefault();
            }
        }
    }
}
