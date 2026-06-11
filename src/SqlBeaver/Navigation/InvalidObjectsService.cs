using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;

namespace SqlBeaver.Navigation
{
    /// <summary>
    /// Lista objetos com dependências quebradas (referências não resolvíveis) a partir de
    /// sys.sql_expression_dependencies, gerando um relatório em uma nova janela de query.
    /// Integração — sem teste de banco. Reusa a conexão compartilhada e OpenNewQueryWindow.
    /// </summary>
    internal static class InvalidObjectsService
    {
        private const int CommandTimeoutSeconds = 30;

        private const string Sql =
            "SELECT OBJECT_SCHEMA_NAME(o.object_id) AS sch, o.name AS obj, " +
            "d.referenced_entity_name AS missing_ref " +
            "FROM sys.sql_expression_dependencies d " +
            "JOIN sys.objects o ON o.object_id = d.referencing_id " +
            "WHERE d.referenced_id IS NULL AND d.is_ambiguous = 0 " +
            "  AND d.referenced_entity_name IS NOT NULL " +
            "ORDER BY sch, obj;";

        public static void Run()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ActiveConnection conn = ConnectionService.GetActiveConnection();
            if (conn == null) { ShowStatus("Objetos inválidos: sem conexão ativa."); return; }

            string server = conn.Server;
            string database = conn.Database;
            var request = new MetadataRequest
            {
                ConnectionString = conn.ConnectionString,
                AccessToken = conn.AccessToken,
                ProviderConnectionType = conn.ProviderConnectionType
            };

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var rows = new List<Tuple<string, string, string>>();
                    using (IDbConnection connection = DefinitionService.OpenConnection(request))
                    {
                        connection.Open();
                        using (IDbCommand cmd = connection.CreateCommand())
                        {
                            cmd.CommandTimeout = CommandTimeoutSeconds;
                            cmd.CommandText = Sql;
                            using (IDataReader reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string sch = reader.IsDBNull(0) ? "" : reader.GetString(0);
                                    string obj = reader.IsDBNull(1) ? "" : reader.GetString(1);
                                    string miss = reader.IsDBNull(2) ? "" : reader.GetString(2);
                                    rows.Add(Tuple.Create(sch, obj, miss));
                                }
                            }
                        }
                    }

                    string content = FormatReport(server, database, rows);
                    _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        DefinitionService.OpenNewQueryWindow(content);
                        Log.Info("Objetos inválidos: " + rows.Count + " referência(s) quebrada(s).");
                    });
                }
                catch (Exception ex)
                {
                    Log.Error("Objetos inválidos", ex);
                    _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        ShowStatus("Objetos inválidos: falha — veja Output > SQL Beaver");
                    });
                }
            });
        }

        private static string FormatReport(
            string server, string database, List<Tuple<string, string, string>> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("/* SQL Beaver — Objetos inválidos");
            sb.AppendLine("   Servidor: " + (server ?? "") + "   Banco: " + (database ?? ""));
            if (rows.Count == 0)
                sb.AppendLine("   nenhuma referência quebrada encontrada");
            else
                sb.AppendLine("   " + rows.Count + " referência(s) quebrada(s)");
            sb.AppendLine("   ============================ */");

            foreach (Tuple<string, string, string> r in rows)
            {
                string schema = string.IsNullOrEmpty(r.Item1) ? "" : r.Item1 + ".";
                sb.AppendLine("-- " + schema + r.Item2 + " → referência quebrada '" + r.Item3 + "'");
            }

            return sb.ToString();
        }

        private static void ShowStatus(string message)
        {
            _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync("SQL Beaver: " + message);
        }
    }
}
