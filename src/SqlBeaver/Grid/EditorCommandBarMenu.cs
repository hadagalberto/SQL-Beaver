using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;

namespace SqlBeaver.Grid
{
    /// <summary>
    /// Registra os comandos do SQL Beaver no menu de contexto do editor SQL
    /// (CommandBar "SQL Files Editor Context"). Botões temporários, recriados a
    /// cada sessão do SSMS. Padrão do AxialSqlTools (Apache-2.0).
    /// </summary>
    internal static class EditorCommandBarMenu
    {
        private const string EditorContextBarName = "SQL Files Editor Context";

        // Referências fortes: handlers COM são coletados pelo GC sem isso.
        private static CommandBarButton _formatButton;
        private static CommandBarButton _refreshCacheButton;

        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var bars = dte?.CommandBars as CommandBars;
                if (bars == null)
                {
                    Log.Info("CommandBars indisponível — menu do editor não registrado.");
                    return;
                }

                CommandBar editorBar = bars[EditorContextBarName];

                _formatButton = AddButton(editorBar, "SQL Beaver: Format Document", OnFormatDocument, beginGroup: true);
                _refreshCacheButton = AddButton(editorBar, "SQL Beaver: Refresh metadata cache", OnRefreshCache, beginGroup: false);

                Log.Info("Comandos registrados no menu de contexto do editor SQL.");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao registrar o menu do editor (\"" + EditorContextBarName + "\")", ex);
            }
        }

        private static CommandBarButton AddButton(
            CommandBar bar,
            string caption,
            _CommandBarButtonEvents_ClickEventHandler onClick,
            bool beginGroup)
        {
            var button = (CommandBarButton)bar.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, /*temporary:*/ true);
            button.Caption    = caption;
            button.BeginGroup = beginGroup;
            button.Click     += onClick;
            return button;
        }

        private static void ShowStatus(string message)
        {
            _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync("SQL Beaver: " + message);
        }

        private static void OnFormatDocument(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("Format: nenhum documento ativo."); return; }

                bool hasSelection = !doc.Selection.IsEmpty;
                string original = hasSelection
                    ? doc.Selection.Text
                    : doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);

                if (string.IsNullOrWhiteSpace(original)) { ShowStatus("Format: nada para formatar."); return; }

                if (!Formatting.SqlFormatterService.TryFormat(original, out string formatted, out string error))
                {
                    ShowStatus("não formatado: " + error);
                    Log.Info("Format Document abortado: " + error);
                    return;
                }

                dte.UndoContext.Open("SQL Beaver Format Document");
                try
                {
                    if (hasSelection)
                    {
                        doc.Selection.Insert(formatted,
                            (int)vsInsertFlags.vsInsertFlagsContainNewText);
                    }
                    else
                    {
                        EditPoint start = doc.StartPoint.CreateEditPoint();
                        start.ReplaceText(doc.EndPoint, formatted,
                            (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                ShowStatus("documento formatado.");
                Log.Info("Format Document aplicado" + (hasSelection ? " (seleção)." : " (documento inteiro)."));
            }
            catch (Exception ex)
            {
                Log.Error("Format Document", ex);
                ShowStatus("falha no Format Document — veja Output > SQL Beaver");
            }
        }

        private static void OnRefreshCache(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ActiveConnection connection = ConnectionService.GetActiveConnection();
                MetadataCache cache = Completion.SqlBeaverCompletionSourceProvider.Cache;

                if (connection == null)
                {
                    cache.InvalidateAll();
                    ShowStatus("cache de metadata limpo (todas as conexões).");
                    Log.Info("Refresh cache: sem conexão ativa — cache inteiro limpo.");
                    return;
                }

                cache.Invalidate(connection.Server, connection.Database);
                // dispara a recarga já, para o próximo popup vir quente
                cache.TryGet(connection.Server, connection.Database,
                    new SqlBeaver.Metadata.MetadataRequest { ConnectionString = connection.ConnectionString, AccessToken = connection.AccessToken, ProviderConnectionType = connection.ProviderConnectionType });
                ShowStatus($"cache de [{connection.Database}] atualizando em background.");
                Log.Info($"Refresh cache: [{connection.Server}].[{connection.Database}] invalidado e recarregando.");
            }
            catch (Exception ex)
            {
                Log.Error("Refresh metadata cache", ex);
                ShowStatus("falha no refresh do cache — veja Output > SQL Beaver");
            }
        }
    }
}
