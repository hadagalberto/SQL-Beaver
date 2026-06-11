using System;
using System.IO;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Session
{
    /// <summary>
    /// Grava cada Execute não cancelado em %LOCALAPPDATA%\SqlBeaver\history\yyyy-MM-dd.sql.
    /// Todas as operações de IO são fire-and-forget em background; nunca bloqueia o Execute.
    /// </summary>
    internal static class QueryHistoryService
    {
        private static readonly object _lock = new object();
        private static bool _errorLogged;

        private static string HistoryDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlBeaver", "history");

        private static string TodayFilePath =>
            Path.Combine(HistoryDir, DateTime.Now.ToString("yyyy-MM-dd") + ".sql");

        /// <summary>
        /// Grava o SQL no arquivo de histórico do dia.
        /// Fire-and-forget — nunca bloqueia o Execute.
        /// </summary>
        public static void Record(string server, string database, string sql)
        {
            if (string.IsNullOrEmpty(sql)) return;

            string entry = HistoryEntryFormatter.Format(DateTime.Now, server, database, sql);
            if (string.IsNullOrEmpty(entry)) return;

            string path = TodayFilePath;

            _ = Task.Run(() =>
            {
                lock (_lock)
                {
                    try
                    {
                        string dir = HistoryDir;
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        File.AppendAllText(path, entry, System.Text.Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        if (!_errorLogged)
                        {
                            _errorLogged = true;
                            Log.Error("QueryHistoryService.Record: falha ao gravar histórico", ex);
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Garante que o arquivo do dia existe (criando com cabeçalho se necessário)
        /// e abre no SSMS.
        /// </summary>
        public static void OpenTodayFile()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                string dir  = HistoryDir;
                string path = TodayFilePath;

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "-- Histórico SQL Beaver de " + DateTime.Now.ToString("yyyy-MM-dd") + "\r\n",
                        System.Text.Encoding.UTF8);
                }

                var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE)) as DTE2;
                dte?.ItemOperations?.OpenFile(path);
            }
            catch (Exception ex)
            {
                Log.Error("QueryHistoryService.OpenTodayFile", ex);
                _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync(
                    "SQL Beaver: falha ao abrir histórico — veja Output > SQL Beaver");
            }
        }
    }
}
