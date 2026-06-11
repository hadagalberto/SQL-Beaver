using System;
using System.Text;

namespace SqlBeaver.Session
{
    /// <summary>
    /// Formata uma entrada de histórico de consultas para gravação em arquivo .sql diário.
    /// </summary>
    public static class HistoryEntryFormatter
    {
        /// <summary>
        /// Bloco de histórico: /* ===== HH:mm:ss [servidor].[database] ===== */ + sql + linha em branco.
        /// CRLF; server/db nulos → "?"; sql null/vazio → retorna string.Empty.
        /// </summary>
        public static string Format(DateTime timestamp, string server, string database, string sql)
        {
            if (string.IsNullOrEmpty(sql))
                return string.Empty;

            string srv = string.IsNullOrEmpty(server) ? "?" : server;
            string db  = string.IsNullOrEmpty(database) ? "?" : database;

            // Trim trailing \r\n from sql
            string trimmedSql = sql.TrimEnd('\r', '\n');

            if (string.IsNullOrEmpty(trimmedSql))
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append("/* ===== ");
            sb.Append(timestamp.ToString("HH:mm:ss"));
            sb.Append("  [");
            sb.Append(srv);
            sb.Append("].[");
            sb.Append(db);
            sb.Append("] ===== */\r\n");
            sb.Append(trimmedSql);
            sb.Append("\r\n\r\n");
            return sb.ToString();
        }
    }
}
