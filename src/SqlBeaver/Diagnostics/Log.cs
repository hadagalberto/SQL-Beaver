using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;

namespace SqlBeaver.Diagnostics
{
    /// <summary>
    /// Log no painel "SQL Beaver" do Output window. Fire-and-forget e nunca lança:
    /// se o pane não puder ser criado, o log é desativado silenciosamente.
    /// </summary>
    public static class Log
    {
        private static OutputWindowPane _pane;
        private static bool _disabled;

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string message, Exception exception = null)
            => Write("ERRO", exception == null ? message : message + " :: " + exception);

        private static void Write(string level, string message)
        {
            if (_disabled) return;
            _ = WriteAsync(level, message);
        }

        private static async Task WriteAsync(string level, string message)
        {
            try
            {
                if (_pane == null)
                    _pane = await VS.Windows.CreateOutputWindowPaneAsync("SQL Beaver");
                await _pane.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] {level}: {message}");
            }
            catch
            {
                _disabled = true;
            }
        }
    }
}
