using System;
using System.Collections.Generic;
using SqlBeaver.Metadata;

namespace SqlBeaver.Analysis
{
    public enum LocalTableKind { Temp, TableVar, Cte }

    public sealed class LocalTableDef
    {
        /// <summary>Nome original: "#x", "@t", "cte". Compare OrdinalIgnoreCase.</summary>
        public string Name { get; }
        public LocalTableKind Kind { get; }
        /// <summary>Colunas conhecidas; lista vazia quando desconhecidas (ex.: SELECT INTO).</summary>
        public IReadOnlyList<ColumnEntry> Columns { get; }

        public LocalTableDef(string name, LocalTableKind kind, IReadOnlyList<ColumnEntry> columns)
        {
            Name = name;
            Kind = kind;
            Columns = columns ?? Array.Empty<ColumnEntry>();
        }
    }

    /// <summary>
    /// Varre o batch atual (delimitado por GO e limitado a 64 KB em torno do caret) e
    /// devolve todas as tabelas locais definidas: #temp, @tableVar, CTEs.
    /// Puro, sem dependências do VS.
    /// </summary>
    public static class LocalObjectScanner
    {
        private const int WindowSize = 64 * 1024;

        /// <summary>Varre o batch que contém caretPosition e devolve as definições locais.</summary>
        public static IReadOnlyList<LocalTableDef> Scan(string text, int caretPosition)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<LocalTableDef>();

            int clampedCaret = Math.Max(0, Math.Min(caretPosition, text.Length));

            // 1. Encontrar os limites do batch (entre GOs)
            int batchStart = 0;
            int batchEnd = text.Length;
            FindBatchBounds(text, clampedCaret, out batchStart, out batchEnd);

            // 2. Limitar a janela a 64 KB em torno do caret
            int windowStart = Math.Max(batchStart, clampedCaret - WindowSize);
            int windowEnd   = Math.Min(batchEnd,   clampedCaret + WindowSize);

            string batch = text.Substring(windowStart, windowEnd - windowStart);
            // caretOffset dentro do batch (para referência futura; atualmente não usado para filtrar defs)

            return ScanBatch(batch);
        }

        // -----------------------------------------------------------------------
        // Localizar os limites do batch que contém o caret
        // -----------------------------------------------------------------------
        private static void FindBatchBounds(string text, int caret, out int start, out int end)
        {
            start = 0;
            end   = text.Length;

            // Procura GOs no texto, rastreando comentários e strings
            bool inLineComment = false, inString = false, inQuotedIdent = false, inBracket = false;
            int blockCommentDepth = 0;
            int i = 0;
            int lastGoBatchStart = 0;

            while (i < text.Length)
            {
                char c = text[i];

                if (inLineComment)  { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)       { if (c == '\'') inString = false; i++; continue; }
                if (inBracket)      { if (c == ']')  inBracket = false; i++; continue; }
                if (inQuotedIdent)  { if (c == '"')  inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[')  { inBracket = true; i++; continue; }
                if (c == '"')  { inQuotedIdent = true; i++; continue; }

                // Detectar GO em linha própria
                if ((c == 'G' || c == 'g') && IsGoKeyword(text, i))
                {
                    int goEnd = i + 2;
                    // GO precede/segue apenas whitespace na linha
                    if (goEnd <= caret)
                    {
                        // este GO está antes do caret: novo batch começa aqui
                        lastGoBatchStart = goEnd;
                        start = goEnd;
                    }
                    else
                    {
                        // GO está depois do caret: batch termina aqui
                        end = i;
                        break;
                    }
                    i = goEnd;
                    continue;
                }

                i++;
            }
        }

        private static bool IsGoKeyword(string text, int pos)
        {
            if (pos + 1 >= text.Length) return false;
            if (!(text[pos] == 'G' || text[pos] == 'g')) return false;
            if (!(text[pos + 1] == 'O' || text[pos + 1] == 'o')) return false;

            // próximo char (se existir) deve ser whitespace ou fim do texto
            int after = pos + 2;
            if (after < text.Length && IsIdentifierChar(text[after])) return false;

            // char anterior (se existir) deve ser whitespace ou início
            if (pos > 0 && !char.IsWhiteSpace(text[pos - 1]) && text[pos - 1] != '\n') return false;

            return true;
        }

        // -----------------------------------------------------------------------
        // Scanner principal do batch
        // -----------------------------------------------------------------------
        private static IReadOnlyList<LocalTableDef> ScanBatch(string batch)
        {
            var results = new List<LocalTableDef>();

            bool inLineComment = false, inString = false, inQuotedIdent = false, inBracket = false;
            int blockCommentDepth = 0;
            int parenDepth = 0;

            int i = 0;
            while (i < batch.Length)
            {
                char c = batch[i];

                if (inLineComment)  { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < batch.Length && batch[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)       { if (c == '\'') inString = false; i++; continue; }
                if (inBracket)      { if (c == ']')  inBracket = false; i++; continue; }
                if (inQuotedIdent)  { if (c == '"')  inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < batch.Length && batch[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[')  { inBracket = true; i++; continue; }
                if (c == '"')  { inQuotedIdent = true; i++; continue; }
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

                if (parenDepth == 0 && IsIdentifierStart(c))
                {
                    int tokenStart = i;
                    string token = ReadToken(batch, ref i);

                    // CREATE TABLE #x (...)
                    if (string.Equals(token, "CREATE", StringComparison.OrdinalIgnoreCase))
                    {
                        TryParseCreateTable(batch, ref i, results);
                        continue;
                    }

                    // DECLARE @t TABLE (...)
                    if (string.Equals(token, "DECLARE", StringComparison.OrdinalIgnoreCase))
                    {
                        TryParseDeclareTable(batch, ref i, results);
                        continue;
                    }

                    // SELECT ... INTO #x FROM ...
                    if (string.Equals(token, "SELECT", StringComparison.OrdinalIgnoreCase))
                    {
                        TryParseSelectInto(batch, ref i, results);
                        continue;
                    }

                    // WITH cte (...) AS (...)
                    if (string.Equals(token, "WITH", StringComparison.OrdinalIgnoreCase))
                    {
                        TryParseWith(batch, ref i, results);
                        continue;
                    }

                    continue;
                }

                i++;
            }

            return results;
        }

        // -----------------------------------------------------------------------
        // CREATE TABLE #x ( col tipo, ... )
        // -----------------------------------------------------------------------
        private static void TryParseCreateTable(string batch, ref int i, List<LocalTableDef> results)
        {
            SkipWhitespaceAndComments(batch, ref i);
            if (!PeekToken(batch, i, "TABLE")) return;
            ReadToken(batch, ref i); // consume TABLE

            SkipWhitespaceAndComments(batch, ref i);
            if (i >= batch.Length) return;

            string name = ReadLocalName(batch, ref i);
            if (name == null || (!name.StartsWith("#") && !name.StartsWith("@"))) return;

            LocalTableKind kind = name.StartsWith("#") ? LocalTableKind.Temp : LocalTableKind.TableVar;

            SkipWhitespaceAndComments(batch, ref i);
            if (i >= batch.Length || batch[i] != '(') return;
            i++; // consume '('

            var columns = ParseColumnList(batch, ref i);
            results.Add(new LocalTableDef(name, kind, columns));
        }

        // -----------------------------------------------------------------------
        // DECLARE @t TABLE ( col tipo, ... )
        // -----------------------------------------------------------------------
        private static void TryParseDeclareTable(string batch, ref int i, List<LocalTableDef> results)
        {
            SkipWhitespaceAndComments(batch, ref i);
            if (i >= batch.Length) return;

            // Read the @var name
            string varName = ReadLocalName(batch, ref i);
            if (varName == null || !varName.StartsWith("@")) return;

            SkipWhitespaceAndComments(batch, ref i);
            if (!PeekToken(batch, i, "TABLE")) return;
            ReadToken(batch, ref i); // consume TABLE

            SkipWhitespaceAndComments(batch, ref i);
            if (i >= batch.Length || batch[i] != '(') return;
            i++; // consume '('

            var columns = ParseColumnList(batch, ref i);
            results.Add(new LocalTableDef(varName, LocalTableKind.TableVar, columns));
        }

        // -----------------------------------------------------------------------
        // SELECT ... INTO #x FROM ...
        // -----------------------------------------------------------------------
        private static void TryParseSelectInto(string batch, ref int i, List<LocalTableDef> results)
        {
            // Scan forward (at depth 0) for INTO keyword followed by a #name
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;
            int blockCommentDepth = 0;
            int parenDepth = 0;

            while (i < batch.Length)
            {
                char c = batch[i];

                if (inLineComment)  { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < batch.Length && batch[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)       { if (c == '\'') inString = false; i++; continue; }
                if (inBracket)      { if (c == ']')  inBracket = false; i++; continue; }
                if (inQuotedIdent)  { if (c == '"')  inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < batch.Length && batch[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[')  { inBracket = true; i++; continue; }
                if (c == '"')  { inQuotedIdent = true; i++; continue; }
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

                if (parenDepth == 0 && IsIdentifierStart(c))
                {
                    string tok = ReadToken(batch, ref i);

                    // INTO followed by #name
                    if (string.Equals(tok, "INTO", StringComparison.OrdinalIgnoreCase))
                    {
                        SkipWhitespaceAndComments(batch, ref i);
                        if (i < batch.Length && batch[i] == '#')
                        {
                            string name = ReadLocalName(batch, ref i);
                            if (name != null)
                                results.Add(new LocalTableDef(name, LocalTableKind.Temp, Array.Empty<ColumnEntry>()));
                        }
                        return;
                    }

                    // New statement starter at depth 0 (other than SELECT internals)
                    if (IsStatementStarter(tok))
                        return;

                    continue;
                }

                if (c == ';' && parenDepth == 0) return;

                i++;
            }
        }

        // -----------------------------------------------------------------------
        // WITH cte [(cols)] AS (...) [, cte2 ...]
        // -----------------------------------------------------------------------
        private static void TryParseWith(string batch, ref int i, List<LocalTableDef> results)
        {
            // Can define multiple CTEs separated by commas
            while (true)
            {
                SkipWhitespaceAndComments(batch, ref i);
                if (i >= batch.Length) return;

                // Read CTE name (regular identifier, not # or @)
                if (!IsIdentifierStart(batch[i])) return;
                string cteName = ReadToken(batch, ref i);

                // Skip optional column list: cte (c1, c2, ...)
                SkipWhitespaceAndComments(batch, ref i);
                List<ColumnEntry> explicitCols = null;
                if (i < batch.Length && batch[i] == '(')
                {
                    // Check if next non-whitespace after matching ')' is AS
                    int saved = i;
                    i++; // consume '('
                    var cols = ParseSimpleNameList(batch, ref i);
                    // ParseSimpleNameList reads until ')'
                    SkipWhitespaceAndComments(batch, ref i);
                    if (i < batch.Length && PeekToken(batch, i, "AS"))
                    {
                        explicitCols = cols;
                    }
                    else
                    {
                        // Not a column list — might be the AS (...) body. Reset.
                        i = saved;
                    }
                }

                SkipWhitespaceAndComments(batch, ref i);
                if (!PeekToken(batch, i, "AS")) return;
                ReadToken(batch, ref i); // consume AS

                SkipWhitespaceAndComments(batch, ref i);
                if (i >= batch.Length || batch[i] != '(') return;
                i++; // consume '('

                if (explicitCols != null)
                {
                    // Skip the body (find matching paren)
                    SkipMatchingParens(batch, ref i);
                    results.Add(new LocalTableDef(cteName, LocalTableKind.Cte, explicitCols));
                }
                else
                {
                    // Heuristic: parse first SELECT list
                    var cols = ParseCteSelectColumns(batch, ref i);
                    SkipMatchingParens(batch, ref i); // consume remainder to ')'
                    results.Add(new LocalTableDef(cteName, LocalTableKind.Cte, cols));
                }

                // Check for comma → another CTE
                SkipWhitespaceAndComments(batch, ref i);
                if (i >= batch.Length || batch[i] != ',') return;
                i++; // consume ','
            }
        }

        // -----------------------------------------------------------------------
        // Parse column definitions: col TYPE [constraints], ...  until closing )
        // -----------------------------------------------------------------------
        private static List<ColumnEntry> ParseColumnList(string batch, ref int i)
        {
            var columns = new List<ColumnEntry>();
            int depth = 1; // we are already inside the opening '('

            while (i < batch.Length && depth > 0)
            {
                SkipWhitespaceAndComments(batch, ref i);
                if (i >= batch.Length) break;

                if (batch[i] == ')')
                {
                    depth--;
                    i++;
                    if (depth == 0) break;
                    continue;
                }

                if (batch[i] == ',') { i++; continue; }

                if (!IsIdentifierStart(batch[i])) { i++; continue; }

                // Read column name
                string colName = ReadToken(batch, ref i);
                if (string.IsNullOrEmpty(colName)) { i++; continue; }

                SkipWhitespaceAndComments(batch, ref i);
                if (i >= batch.Length || batch[i] == ')' || batch[i] == ',')
                {
                    // no type info
                    columns.Add(new ColumnEntry(colName, "", true, false));
                    continue;
                }

                // Read type: next token(s) up to comma or ')' at depth 1
                // depth tracks sub-parens within the type (e.g. decimal(10,2))
                string sqlType = ReadColumnType(batch, ref i);

                // Skip remaining column-def tokens (NOT NULL, PRIMARY KEY, CONSTRAINT, etc.)
                // until we hit ',' or ')' at depth 1
                SkipToColumnSeparator(batch, ref i, ref depth);

                columns.Add(new ColumnEntry(colName, sqlType, true, false));
            }

            return columns;
        }

        private static string ReadColumnType(string batch, ref int i)
        {
            // type = first token + optional (n) or (n,m)
            SkipWhitespaceAndComments(batch, ref i);
            if (i >= batch.Length || batch[i] == ')' || batch[i] == ',')
                return "";

            int start = i;
            string typeName = ReadToken(batch, ref i);
            if (string.IsNullOrEmpty(typeName)) return "";

            SkipWhitespaceAndComments(batch, ref i);
            // optional precision/scale in parens
            if (i < batch.Length && batch[i] == '(')
            {
                int parenStart = i;
                i++; // consume '('
                int depth = 1;
                while (i < batch.Length && depth > 0)
                {
                    if (batch[i] == '(') depth++;
                    else if (batch[i] == ')') depth--;
                    i++;
                }
                return typeName + batch.Substring(parenStart, i - parenStart);
            }

            return typeName;
        }

        private static void SkipToColumnSeparator(string batch, ref int i, ref int depth)
        {
            // Skip until we find ',' or ')' at current paren depth (depth passed in is 1)
            while (i < batch.Length)
            {
                char c = batch[i];
                if (c == '(') { depth++; i++; continue; }
                if (c == ')')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                    i++; continue;
                }
                if (c == ',' && depth == 1) { break; }
                i++;
            }
        }

        // -----------------------------------------------------------------------
        // Parse simple comma-separated name list until ')': c1, c2, c3
        // -----------------------------------------------------------------------
        private static List<ColumnEntry> ParseSimpleNameList(string batch, ref int i)
        {
            var cols = new List<ColumnEntry>();
            while (i < batch.Length)
            {
                SkipWhitespaceAndComments(batch, ref i);
                if (i >= batch.Length) break;
                if (batch[i] == ')') { i++; break; }
                if (batch[i] == ',') { i++; continue; }
                if (!IsIdentifierStart(batch[i])) { i++; continue; }
                string name = ReadToken(batch, ref i);
                if (!string.IsNullOrEmpty(name))
                    cols.Add(new ColumnEntry(name, "", true, false));
            }
            return cols;
        }

        // -----------------------------------------------------------------------
        // Parse CTE SELECT columns heuristically (first SELECT at depth 0)
        // -----------------------------------------------------------------------
        private static List<ColumnEntry> ParseCteSelectColumns(string batch, ref int i)
        {
            // Find the first SELECT keyword at depth 0
            int depth = 0; // already consumed the opening '(' of the CTE body
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;
            int blockCommentDepth = 0;

            while (i < batch.Length)
            {
                char c = batch[i];

                if (inLineComment)  { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < batch.Length && batch[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)       { if (c == '\'') inString = false; i++; continue; }
                if (inBracket)      { if (c == ']')  inBracket = false; i++; continue; }
                if (inQuotedIdent)  { if (c == '"')  inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < batch.Length && batch[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[')  { inBracket = true; i++; continue; }
                if (c == '"')  { inQuotedIdent = true; i++; continue; }
                if (c == '(') { depth++; i++; continue; }
                if (c == ')')
                {
                    if (depth == 0) return new List<ColumnEntry>(); // end of CTE body, no SELECT found
                    depth--;
                    i++; continue;
                }

                if (depth == 0 && IsIdentifierStart(c))
                {
                    string tok = ReadToken(batch, ref i);
                    if (string.Equals(tok, "SELECT", StringComparison.OrdinalIgnoreCase))
                    {
                        return ParseSelectColumnList(batch, ref i);
                    }
                    continue;
                }

                i++;
            }

            return new List<ColumnEntry>();
        }

        // -----------------------------------------------------------------------
        // Parse a SELECT column list: for each item extract alias or trailing identifier
        // Items: "a", "t.b AS c", "x+1 AS val", bare expressions (skipped)
        // -----------------------------------------------------------------------
        private static List<ColumnEntry> ParseSelectColumnList(string batch, ref int i)
        {
            var cols = new List<ColumnEntry>();
            // Read tokens until FROM (at depth 0) or end
            int depth = 0;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;
            int blockCommentDepth = 0;

            // Per-item tracking
            var itemTokens = new List<string>(); // tokens for current item
            bool hasAs = false;
            string lastIdent = null;

            void FlushItem()
            {
                string colName = null;
                if (hasAs && lastIdent != null)
                    colName = lastIdent;
                else if (itemTokens.Count == 1)
                    colName = itemTokens[0]; // bare identifier
                else if (itemTokens.Count >= 1)
                {
                    // dotted ref: last part
                    string last = itemTokens[itemTokens.Count - 1];
                    // only if it looks like a plain identifier (not an operator etc.)
                    if (IsAlphaToken(last))
                        colName = last;
                }
                if (colName != null && IsAlphaToken(colName))
                    cols.Add(new ColumnEntry(colName, "", true, false));
                itemTokens.Clear();
                hasAs = false;
                lastIdent = null;
            }

            while (i < batch.Length)
            {
                char c = batch[i];

                if (inLineComment)  { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < batch.Length && batch[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)       { if (c == '\'') inString = false; i++; continue; }
                if (inBracket)      { if (c == ']')  inBracket = false; i++; continue; }
                if (inQuotedIdent)  { if (c == '"')  inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < batch.Length && batch[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[')  { inBracket = true; i++; continue; }
                if (c == '"')  { inQuotedIdent = true; i++; continue; }
                if (c == '(') { depth++; i++; continue; }
                if (c == ')')
                {
                    if (depth == 0) { FlushItem(); return cols; }
                    depth--;
                    i++; continue;
                }

                if (c == ',' && depth == 0)
                {
                    FlushItem();
                    i++;
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    string tok = ReadToken(batch, ref i);

                    if (depth == 0)
                    {
                        // FROM ends the SELECT list
                        if (string.Equals(tok, "FROM", StringComparison.OrdinalIgnoreCase))
                        {
                            FlushItem();
                            return cols;
                        }
                        if (string.Equals(tok, "AS", StringComparison.OrdinalIgnoreCase))
                        {
                            hasAs = true;
                            // next token is the alias
                            SkipWhitespaceAndComments(batch, ref i);
                            if (i < batch.Length && IsIdentifierStart(batch[i]))
                            {
                                lastIdent = ReadToken(batch, ref i);
                                itemTokens.Add("AS");
                                itemTokens.Add(lastIdent);
                            }
                        }
                        else
                        {
                            itemTokens.Add(tok);
                            if (IsAlphaToken(tok))
                                lastIdent = tok;
                        }
                    }
                    continue;
                }

                i++;
            }

            FlushItem();
            return cols;
        }

        private static bool IsAlphaToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (!IsIdentifierChar(c)) return false;
            return true;
        }

        // -----------------------------------------------------------------------
        // Skip the remaining content of a paren block (already consumed opening '(')
        // -----------------------------------------------------------------------
        private static void SkipMatchingParens(string batch, ref int i)
        {
            // Already past the opening '(', skip to matching ')'
            int depth = 1;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;
            int blockCommentDepth = 0;

            while (i < batch.Length && depth > 0)
            {
                char c = batch[i];

                if (inLineComment)  { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < batch.Length && batch[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)       { if (c == '\'') inString = false; i++; continue; }
                if (inBracket)      { if (c == ']')  inBracket = false; i++; continue; }
                if (inQuotedIdent)  { if (c == '"')  inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < batch.Length && batch[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[')  { inBracket = true; i++; continue; }
                if (c == '"')  { inQuotedIdent = true; i++; continue; }
                if (c == '(') { depth++; i++; continue; }
                if (c == ')') { depth--; i++; continue; }
                i++;
            }
        }

        // -----------------------------------------------------------------------
        // Token helpers
        // -----------------------------------------------------------------------
        private static string ReadToken(string text, ref int i)
        {
            int start = i;
            while (i < text.Length && IsIdentifierChar(text[i]))
                i++;
            return text.Substring(start, i - start);
        }

        /// <summary>Reads a local table/variable name: #name, ##name, @name, or plain identifier.</summary>
        private static string ReadLocalName(string text, ref int i)
        {
            if (i >= text.Length) return null;

            if (text[i] == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close < 0) return null;
                string n = text.Substring(i, close - i + 1);
                i = close + 1;
                return n;
            }

            int start = i;
            // allow # ## @ at start
            if (i < text.Length && (text[i] == '#' || text[i] == '@'))
            {
                i++;
                if (i < text.Length && text[i] == '#') i++; // ##global
            }

            while (i < text.Length && IsIdentifierChar(text[i]))
                i++;

            if (i == start) return null;
            return text.Substring(start, i - start);
        }

        private static bool PeekToken(string text, int i, string expected)
        {
            if (i >= text.Length) return false;
            if (!IsIdentifierStart(text[i])) return false;

            int j = i;
            string tok = ReadToken(text, ref j);
            return string.Equals(tok, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void SkipWhitespaceAndComments(string text, ref int i)
        {
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
                {
                    i += 2;
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                        i++;
                    i += 2;
                    continue;
                }
                break;
            }
        }

        private static readonly HashSet<string> StatementStarterSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "DECLARE", "EXEC", "EXECUTE",
            "CREATE", "ALTER", "DROP", "TRUNCATE", "RETURN", "PRINT", "USE", "FROM", "WHERE",
            "GROUP", "ORDER", "HAVING",
        };

        private static bool IsStatementStarter(string token)
            => StatementStarterSet.Contains(token) &&
               !string.Equals(token, "SELECT", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(token, "FROM", StringComparison.OrdinalIgnoreCase);

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '#' || c == '@';
        private static bool IsIdentifierChar(char c)  => char.IsLetterOrDigit(c) || c == '_' || c == '$';
    }
}
