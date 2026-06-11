using System;
using System.Collections.Generic;
using System.Text;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;

namespace SqlBeaver.Completion
{
    /// <summary>Monta o texto do tooltip para o identificador sob o cursor, resolvendo SOMENTE no
    /// metadata já carregado (nunca consulta o banco). Retorna null quando não há nada a mostrar.</summary>
    public static class QuickInfoBuilder
    {
        private const int MaxColumns = 20;

        /// <summary>
        /// Resolução por prioridade:
        /// 1. identifier == alias do escopo → "alias → Schema.Tabela" + colunas (ou colunas locais).
        /// 2. identifier é coluna de tabela(s) do escopo → "Tabela.Coluna — tipo [NULL] [PK]".
        /// 3. identifier == nome de tabela no metadata → "Schema.Tabela" + colunas.
        /// 4. identifier == objeto (proc/func) → "Schema.Objeto (tipo)" + parâmetros.
        /// 5. null.
        /// </summary>
        public static string Build(
            string identifier,
            string dotPrefixOrNull,
            IReadOnlyList<TableRef> scope,
            DbMetadata metadata,
            IReadOnlyList<LocalTableDef> locals)
        {
            if (string.IsNullOrEmpty(identifier) || metadata == null)
                return null;

            // ---- 1. alias do escopo ----
            foreach (TableRef tr in scope)
            {
                if (tr.Alias != null &&
                    string.Equals(tr.Alias, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    // Tenta resolver como tabela local primeiro
                    LocalTableDef local = FindLocal(locals, tr.Table);
                    if (local != null && local.Columns.Count > 0)
                        return BuildLocalTableTooltip(identifier + " → " + tr.Table, local.Columns);

                    // Resolução no metadata
                    string key = ResolveTableKey(tr, metadata);
                    if (key != null && metadata.ColumnsByTable.TryGetValue(key, out IReadOnlyList<ColumnEntry> aliasCols))
                    {
                        string aliasLabel = identifier + " → " + BuildSchemaTable(tr);
                        return BuildTableTooltip(aliasLabel, aliasCols);
                    }

                    // Alias sem colunas conhecidas: mostra ao menos "alias → Schema.Tabela"
                    string schemaTable = BuildSchemaTable(tr);
                    if (schemaTable != null)
                        return identifier + " → " + schemaTable;

                    return null;
                }
            }

            // ---- 2. coluna de tabela(s) no escopo ----
            {
                var matches = new List<(string tableDisplay, ColumnEntry col)>();
                foreach (TableRef tr in scope)
                {
                    // tabelas locais
                    LocalTableDef local = FindLocal(locals, tr.Table);
                    if (local != null)
                    {
                        foreach (ColumnEntry c in local.Columns)
                        {
                            if (string.Equals(c.Name, identifier, StringComparison.OrdinalIgnoreCase))
                                matches.Add((tr.Alias ?? tr.Table, c));
                        }
                        continue;
                    }

                    // metadata
                    string key = ResolveTableKey(tr, metadata);
                    if (key == null) continue;
                    if (!metadata.ColumnsByTable.TryGetValue(key, out IReadOnlyList<ColumnEntry> scopeCols)) continue;
                    foreach (ColumnEntry c in scopeCols)
                    {
                        if (string.Equals(c.Name, identifier, StringComparison.OrdinalIgnoreCase))
                            matches.Add((tr.Alias ?? tr.Table, c));
                    }
                }

                if (matches.Count == 1)
                {
                    (string tbl, ColumnEntry col) = matches[0];
                    return BuildColumnTooltip(tbl, col);
                }
                if (matches.Count > 1)
                {
                    var sb = new StringBuilder();
                    foreach ((string tbl, ColumnEntry col) in matches)
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(BuildColumnTooltip(tbl, col));
                    }
                    return sb.ToString();
                }
            }

            // ---- 3. tabela no metadata ----
            {
                string resolvedSchema = null;
                foreach (TableEntry t in metadata.Tables)
                {
                    if (string.Equals(t.Name, identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        if (resolvedSchema == null)
                            resolvedSchema = t.Schema;
                        else
                        {
                            // ambíguo: usa qualquer um (primeiro encontrado)
                            resolvedSchema = null;
                            break;
                        }
                    }
                }

                if (resolvedSchema == null)
                {
                    // tentativa: usa a primeira tabela encontrada pelo nome
                    foreach (TableEntry t in metadata.Tables)
                    {
                        if (string.Equals(t.Name, identifier, StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedSchema = t.Schema;
                            break;
                        }
                    }
                }

                if (resolvedSchema != null)
                {
                    string tableKey = DbMetadata.TableKey(resolvedSchema, identifier);
                    string label = resolvedSchema + "." + identifier;
                    if (metadata.ColumnsByTable.TryGetValue(tableKey, out IReadOnlyList<ColumnEntry> tblCols))
                        return BuildTableTooltip(label, tblCols);
                    return label;
                }
            }

            // ---- 4. objeto (proc/func/view) ----
            foreach (ObjectEntry obj in metadata.Objects)
            {
                if (string.Equals(obj.Name, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    string objKey = DbMetadata.TableKey(obj.Schema, obj.Name);
                    IReadOnlyList<ParameterEntry> prms;
                    if (!metadata.ParametersByObject.TryGetValue(objKey, out prms))
                        prms = Array.Empty<ParameterEntry>();
                    return BuildObjectTooltip(obj, prms);
                }
            }

            return null;
        }

        // -----------------------------------------------------------------------
        // Helpers de formatação
        // -----------------------------------------------------------------------

        private static string BuildTableTooltip(string label, IReadOnlyList<ColumnEntry> columns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(label);
            int shown = Math.Min(columns.Count, MaxColumns);
            for (int i = 0; i < shown; i++)
            {
                ColumnEntry c = columns[i];
                sb.Append("  ");
                sb.Append(c.Name);
                if (!string.IsNullOrEmpty(c.SqlType))
                {
                    sb.Append(" ");
                    sb.Append(c.SqlType);
                }
                if (c.IsPrimaryKey) sb.Append(" [PK]");
                if (i < shown - 1 || columns.Count > MaxColumns)
                    sb.AppendLine();
                else
                    sb.Append(string.Empty);
            }
            if (columns.Count > MaxColumns)
            {
                sb.Append("  +");
                sb.Append(columns.Count - MaxColumns);
                sb.Append(" coluna(s)");
            }
            return sb.ToString().TrimEnd();
        }

        private static string BuildLocalTableTooltip(string label, IReadOnlyList<ColumnEntry> columns)
            => BuildTableTooltip(label, columns);

        private static string BuildColumnTooltip(string tableDisplay, ColumnEntry col)
        {
            var sb = new StringBuilder();
            sb.Append(tableDisplay);
            sb.Append(".");
            sb.Append(col.Name);
            sb.Append(" — ");
            if (!string.IsNullOrEmpty(col.SqlType))
            {
                sb.Append(col.SqlType);
                sb.Append(" ");
            }
            sb.Append(col.IsNullable ? "NULL" : "NOT NULL");
            if (col.IsPrimaryKey) sb.Append(" [PK]");
            return sb.ToString();
        }

        private static string BuildObjectTooltip(ObjectEntry obj, IReadOnlyList<ParameterEntry> parameters)
        {
            string typeName = obj.Type == DbObjectType.Procedure     ? "procedure"
                            : obj.Type == DbObjectType.ScalarFunction ? "scalar function"
                            : obj.Type == DbObjectType.TableFunction   ? "table function"
                            : "view";

            var sb = new StringBuilder();
            sb.AppendLine(obj.Schema + "." + obj.Name + " (" + typeName + ")");
            foreach (ParameterEntry p in parameters)
            {
                sb.Append("  ");
                sb.Append(p.Name);
                if (!string.IsNullOrEmpty(p.SqlType))
                {
                    sb.Append(" ");
                    sb.Append(p.SqlType);
                }
                if (p.IsOutput) sb.Append(" [OUTPUT]");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string BuildSchemaTable(TableRef tr)
        {
            if (tr.Schema != null)
                return tr.Schema + "." + tr.Table;
            return tr.Table;
        }

        // -----------------------------------------------------------------------
        // Resolução de chave de tabela
        // -----------------------------------------------------------------------

        private static string ResolveTableKey(TableRef tableRef, DbMetadata metadata)
        {
            if (tableRef.Schema != null)
                return DbMetadata.TableKey(tableRef.Schema, tableRef.Table);
            string schema = metadata.ResolveUniqueSchema(tableRef.Table);
            return schema == null ? null : DbMetadata.TableKey(schema, tableRef.Table);
        }

        private static LocalTableDef FindLocal(IReadOnlyList<LocalTableDef> locals, string name)
        {
            if (locals == null) return null;
            foreach (LocalTableDef l in locals)
            {
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
                    return l;
            }
            return null;
        }
    }
}
