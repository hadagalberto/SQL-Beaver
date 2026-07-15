using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Connection;
using SqlBeaver.Completion;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;

namespace SqlBeaver.Navigation
{
    /// <summary>
    /// Serviços de navegação: Ir para definição e Localizar referências.
    /// Chamados na thread de UI; operações de I/O são disparadas em background.
    /// </summary>
    internal static class DefinitionService
    {
        private const int CommandTimeoutSeconds = 15;

        // ---------------------------------------------------------------
        // Public entry points
        // ---------------------------------------------------------------

        public static void GoToDefinition()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string name = GetWordUnderCaret(out string schemaPrefix);
            if (string.IsNullOrEmpty(name)) { ShowStatus("Ir para definição: nenhum identificador sob o cursor."); return; }
            GoTo(schemaPrefix, name);
        }

        public static void FindReferences()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string name = GetWordUnderCaret(out _);
            if (string.IsNullOrEmpty(name)) { ShowStatus("Localizar referências: nenhum identificador sob o cursor."); return; }

            ActiveConnection conn = ConnectionService.GetActiveConnection();
            if (conn == null) { ShowStatus("Localizar referências: sem conexão ativa."); return; }

            string server = conn.Server;
            string database = conn.Database;
            MetadataRequest req = BuildRequest(conn);

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var refs = new List<ReferenceListFormatter.ReferencedObject>();
                    using (IDbConnection connection = OpenConnection(req))
                    {
                        connection.Open();
                        using (IDbCommand cmd = connection.CreateCommand())
                        {
                            cmd.CommandTimeout = CommandTimeoutSeconds;
                            cmd.CommandText =
                                "SELECT DISTINCT OBJECT_SCHEMA_NAME(d.referencing_id) AS s, " +
                                "OBJECT_NAME(d.referencing_id) AS n " +
                                "FROM sys.sql_expression_dependencies AS d " +
                                "WHERE d.referenced_entity_name = @name " +
                                "ORDER BY s, n;";
                            IDbDataParameter p = cmd.CreateParameter();
                            p.ParameterName = "@name";
                            p.Value = name;
                            cmd.Parameters.Add(p);
                            using (IDataReader reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string s = reader.IsDBNull(0) ? "" : reader.GetString(0);
                                    string n = reader.IsDBNull(1) ? "" : reader.GetString(1);
                                    if (!string.IsNullOrEmpty(n))
                                        refs.Add(new ReferenceListFormatter.ReferencedObject(s, n));
                                }
                            }
                        }
                    }

                    string content = ReferenceListFormatter.Format(name, server, database, refs);
                    _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        OpenNewQueryWindow(content);
                    });
                }
                catch (Exception ex)
                {
                    Log.Error("Localizar referências: " + name, ex);
                    _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        ShowStatus("Localizar referências: falha — veja Output > SQL Beaver");
                    });
                }
            });
        }

        // ---------------------------------------------------------------
        // Internal GoTo — shared by GoToDefinition and FindObjectDialog
        // ---------------------------------------------------------------

        internal static void GoTo(string schemaOrNull, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ActiveConnection conn = ConnectionService.GetActiveConnection();
            if (conn == null) { ShowStatus("Ir para definição: sem conexão ativa."); return; }

            DbMetadata metadata = SqlBeaverCompletionSourceProvider.Cache.TryGet(
                conn.Server, conn.Database,
                new MetadataRequest
                {
                    ConnectionString = conn.ConnectionString,
                    AccessToken = conn.AccessToken,
                    ProviderConnectionType = conn.ProviderConnectionType
                });

            if (metadata == null)
            {
                ShowStatus("Ir para definição: cache ainda carregando — tente novamente em instantes.");
                return;
            }

            // Try table first
            string schema = schemaOrNull;
            if (string.IsNullOrEmpty(schema))
                schema = metadata.ResolveUniqueSchema(name);

            if (schema != null)
            {
                string key = DbMetadata.TableKey(schema, name);
                if (metadata.ColumnsByTable.TryGetValue(key, out IReadOnlyList<ColumnEntry> columns))
                {
                    string script = TableScriptBuilder.Build(schema, name, columns);
                    OpenNewQueryWindow(script);
                    Log.Info("Ir para definição (tabela): " + schema + "." + name);
                    return;
                }
            }

            // Also try table lookup without schema prefix from table list
            if (schema == null)
            {
                foreach (TableEntry t in metadata.Tables)
                {
                    if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (metadata.ColumnsByTable.TryGetValue(DbMetadata.TableKey(t.Schema, t.Name), out IReadOnlyList<ColumnEntry> cols))
                        {
                            string script = TableScriptBuilder.Build(t.Schema, t.Name, cols);
                            OpenNewQueryWindow(script);
                            Log.Info("Ir para definição (tabela): " + t.Schema + "." + t.Name);
                            return;
                        }
                    }
                }
            }

            // Try objects (procedures, views, functions)
            string resolvedSchema = schemaOrNull;
            string resolvedName = name;
            bool found = false;
            foreach (ObjectEntry obj in metadata.Objects)
            {
                bool nameMatch = string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase);
                bool schemaMatch = string.IsNullOrEmpty(schemaOrNull) ||
                                   string.Equals(obj.Schema, schemaOrNull, StringComparison.OrdinalIgnoreCase);
                if (nameMatch && schemaMatch)
                {
                    resolvedSchema = obj.Schema;
                    resolvedName = obj.Name;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                ShowStatus("Ir para definição: objeto '" + name + "' não encontrado no cache.");
                return;
            }

            MetadataRequest request = BuildRequest(conn);
            string qualifiedName = resolvedSchema + "." + resolvedName;

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string definition = null;
                    using (IDbConnection connection = OpenConnection(request))
                    {
                        connection.Open();
                        using (IDbCommand cmd = connection.CreateCommand())
                        {
                            cmd.CommandTimeout = CommandTimeoutSeconds;
                            cmd.CommandText = "SELECT OBJECT_DEFINITION(OBJECT_ID(@name))";
                            IDbDataParameter p = cmd.CreateParameter();
                            p.ParameterName = "@name";
                            p.Value = qualifiedName;
                            cmd.Parameters.Add(p);
                            object result = cmd.ExecuteScalar();
                            if (result != null && result != DBNull.Value)
                                definition = result.ToString();
                        }
                    }

                    string content = string.IsNullOrEmpty(definition)
                        ? "-- definição indisponível (criptografada/sem permissão): " + qualifiedName
                        : definition;

                    _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        OpenNewQueryWindow(content);
                        Log.Info("Ir para definição (objeto): " + qualifiedName);
                    });
                }
                catch (Exception ex)
                {
                    Log.Error("Ir para definição: " + qualifiedName, ex);
                    _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        ShowStatus("Ir para definição: falha — veja Output > SQL Beaver");
                    });
                }
            });
        }

        // ---------------------------------------------------------------
        // Open new query window
        // ---------------------------------------------------------------

        internal static void OpenNewQueryWindow(string content)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                // Primary path: ScriptFactory.CreateNewBlankScript via reflection
                Type serviceCacheType = FindLoadedType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
                if (serviceCacheType != null)
                {
                    PropertyInfo sfProp = serviceCacheType.GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static);
                    object scriptFactory = sfProp?.GetValue(null);
                    if (scriptFactory != null)
                    {
                        // Resolve ScriptType.Sql enum
                        Type scriptTypeEnum = FindLoadedType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptType");
                        object sqlEnum = scriptTypeEnum != null
                            ? Enum.Parse(scriptTypeEnum, "Sql")
                            : null;

                        MethodInfo createMethod = null;
                        foreach (MethodInfo m in scriptFactory.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (m.Name == "CreateNewBlankScript")
                            {
                                ParameterInfo[] pars = m.GetParameters();
                                if (pars.Length >= 1 && pars.Length <= 3)
                                {
                                    createMethod = m;
                                    break;
                                }
                            }
                        }

                        if (createMethod != null && sqlEnum != null)
                        {
                            ParameterInfo[] pars = createMethod.GetParameters();
                            object[] args = new object[pars.Length];
                            args[0] = sqlEnum;
                            // remaining params left as null (defaults)
                            createMethod.Invoke(scriptFactory, args);

                            // Insert content into the newly opened document
                            var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                            var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                            if (doc != null)
                            {
                                EditPoint ep = doc.EndPoint.CreateEditPoint();
                                ep.Insert(content);
                                Log.Info("OpenNewQueryWindow: criado via ScriptFactory.CreateNewBlankScript.");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Info("OpenNewQueryWindow: ScriptFactory falhou (" + ex.Message + "), usando fallback de arquivo temp.");
            }

            // Fallback: arquivo temp (abre DESCONECTADO, sem prompt de conexão).
            OpenDisconnectedQueryWindow(content);
        }

        /// <summary>
        /// Abre o conteúdo numa janela de query SEM pedir conexão — grava um .sql temporário e o
        /// abre como documento (igual File > Open). Usado na restauração de sessão para que o SSMS
        /// não dispare o diálogo "Conectar ao servidor" de cada aba reaberta; o usuário conecta
        /// manualmente se quiser (ao executar, o SSMS pergunta a conexão normalmente).
        /// </summary>
        /// <summary>
        /// Reabre um arquivo .sql REAL do disco pelo caminho (igual File &gt; Open), preservando a
        /// referência de arquivo salvo. Usado na restauração de sessão para abas que eram arquivos
        /// salvos — não vira query nova/untitled. O SSMS pode pedir conexão ao abrir (comportamento
        /// nativo de abrir .sql); é aceitável para um arquivo real.
        /// </summary>
        internal static void OpenExistingFile(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte2 = Package.GetGlobalService(typeof(DTE)) as DTE2;
                dte2?.ItemOperations?.OpenFile(path);
                Log.Info("SessionRestore: arquivo salvo reaberto: " + path);
            }
            catch (Exception ex)
            {
                Log.Error("OpenExistingFile: falhou (" + path + ")", ex);
            }
        }

        internal static void OpenDisconnectedQueryWindow(string content)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "SqlBeaver_" + System.IO.Path.GetRandomFileName() + ".sql");
                System.IO.File.WriteAllText(path, content, System.Text.Encoding.UTF8);
                var dte2 = Package.GetGlobalService(typeof(DTE)) as DTE2;
                dte2?.ItemOperations?.OpenFile(path);
                Log.Info("OpenDisconnectedQueryWindow: aberto via arquivo temp (sem conexão): " + path);
            }
            catch (Exception ex2)
            {
                Log.Error("OpenDisconnectedQueryWindow: falhou", ex2);
                ShowStatus("Não foi possível abrir nova janela — veja Output > SQL Beaver");
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Reads the word under the caret using DTE line text.
        /// Also extracts an optional schema prefix (schema.name).
        /// Returns the object name; schemaPrefix is set when found.
        /// </summary>
        private static string GetWordUnderCaret(out string schemaPrefix)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            schemaPrefix = null;
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) return null;

                TextSelection sel = doc.Selection;
                int line = sel.ActivePoint.Line;
                int col = sel.ActivePoint.LineCharOffset; // 1-based

                EditPoint ep = doc.StartPoint.CreateEditPoint();
                ep.MoveToLineAndOffset(line, 1);
                string lineText = ep.GetText(ep.Line == doc.EndPoint.Line
                    ? doc.EndPoint
                    : ep.CreateEditPoint());

                // Fallback: get just the line using GetLines
                try
                {
                    lineText = doc.StartPoint.CreateEditPoint().GetLines(line, line + 1);
                }
                catch { /* use lineText already computed */ }

                if (string.IsNullOrEmpty(lineText)) return null;

                // clamp col to line length
                int caretIdx = Math.Min(col - 1, lineText.Length - 1);
                if (caretIdx < 0) return null;

                // Expand word at caret position (SQL identifier chars: letters, digits, _, @, #)
                int start = caretIdx;
                while (start > 0 && IsIdentChar(lineText[start - 1])) start--;
                int end = caretIdx;
                while (end < lineText.Length && IsIdentChar(lineText[end])) end++;

                string word = lineText.Substring(start, end - start).Trim('[', ']');
                if (string.IsNullOrEmpty(word)) return null;

                // Check for schema prefix: look back past the word start for "schema."
                if (start > 0 && lineText[start - 1] == '.')
                {
                    int sEnd = start - 1;
                    int sStart = sEnd - 1;
                    while (sStart > 0 && IsIdentChar(lineText[sStart - 1])) sStart--;
                    if (sStart < sEnd)
                        schemaPrefix = lineText.Substring(sStart, sEnd - sStart).Trim('[', ']');
                }

                return word;
            }
            catch (Exception ex)
            {
                Log.Error("GetWordUnderCaret", ex);
                return null;
            }
        }

        private static bool IsIdentChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '[' || c == ']';

        internal static IDbConnection OpenConnection(MetadataRequest request)
        {
            if (request.ProviderConnectionType != null)
                return (IDbConnection)Activator.CreateInstance(request.ProviderConnectionType, request.ConnectionString);

            var conn = new SqlConnection(request.ConnectionString);
            if (!string.IsNullOrEmpty(request.AccessToken))
                conn.AccessToken = request.AccessToken;
            return conn;
        }

        private static MetadataRequest BuildRequest(ActiveConnection conn)
            => new MetadataRequest
            {
                ConnectionString = conn.ConnectionString,
                AccessToken = conn.AccessToken,
                ProviderConnectionType = conn.ProviderConnectionType
            };

        private static void ShowStatus(string message)
        {
            _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync("SQL Beaver: " + message);
        }

        private static Type FindLoadedType(string fullTypeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullTypeName, throwOnError: false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }
    }
}
