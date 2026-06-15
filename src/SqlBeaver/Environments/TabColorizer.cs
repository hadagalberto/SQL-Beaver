using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Environments
{
    /// <summary>
    /// Pinta a ABA do documento com a cor do ambiente da conexão. Não há API pública:
    /// manipula a árvore visual do shell (técnica do AxialSqlTools, Apache-2.0, comprovada
    /// no SSMS 21/22). Nomes de tipos internos por string — pode quebrar em updates do
    /// SSMS; toda falha degrada para "aba sem cor" + log único, nunca exceção.
    /// </summary>
    internal static class TabColorizer
    {
        // Refs fortes: eventos COM são coletados sem isso.
        private static WindowEvents _windowEvents;
        private static DispatcherTimer _reapplyTimer;
        private static int _reapplyTicksLeft;
        private static readonly Dictionary<string, Color?> _colorsByCaption =
            new Dictionary<string, Color?>(StringComparer.OrdinalIgnoreCase);
        private static bool _loggedWalkFailure;
        // Diagnóstico: loga UMA vez os tipos de elemento candidatos a aba quando nenhuma aba
        // for encontrada (nomes internos do SSMS variam entre builds).
        private static bool _loggedNoTabs;

        // Retido para uso em RefreshAfterRulesChanged (chamado fora da inicialização)
        private static DTE2 _dte;

        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                if (dte == null) { Log.Info("TabColorizer: DTE indisponível."); return; }
                _dte = dte;

                _windowEvents = dte.Events.WindowEvents;
                _windowEvents.WindowActivated += (gotFocus, lostFocus) => { LearnActiveWindow(dte); ScheduleReapply(); };
                _windowEvents.WindowCreated += w => ScheduleReapply();
                _windowEvents.WindowClosing += w => ScheduleReapply();

                _reapplyTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
                {
                    Interval = TimeSpan.FromMilliseconds(150),
                };
                _reapplyTimer.Tick += (s, e) =>
                {
                    ReapplyAll();
                    if (--_reapplyTicksLeft <= 0) _reapplyTimer.Stop();
                };

                LearnActiveWindow(dte);
                ScheduleReapply();
                Log.Info("TabColorizer ativo (cor de aba por ambiente).");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao inicializar o TabColorizer", ex);
            }
        }

        private static void LearnActiveWindow(DTE2 dte)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                string caption = NormalizeCaption(dte.ActiveWindow?.Caption);
                if (string.IsNullOrEmpty(caption)) return;

                ActiveConnection connection = ConnectionService.GetActiveConnection();
                EnvironmentRule rule = connection == null
                    ? null
                    : EnvironmentStore.MatchActive(connection.Server, connection.Database);

                _colorsByCaption[caption] = ParseColor(rule?.Color);
            }
            catch (Exception ex)
            {
                Log.Error("TabColorizer.LearnActiveWindow", ex);
            }
        }

        private static void ScheduleReapply()
        {
            _reapplyTicksLeft = 4;
            if (_reapplyTimer != null && !_reapplyTimer.IsEnabled)
                _reapplyTimer.Start();
        }

        private static void ReapplyAll()
        {
            try
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow == null) return;

                var tabs = new List<FrameworkElement>();
                CollectByTypeName(mainWindow, "DocumentTabItem", tabs, 0);
                if (tabs.Count == 0)
                {
                    var groups = new List<FrameworkElement>();
                    CollectByTypeName(mainWindow, "DocumentGroupControl", groups, 0);
                    foreach (FrameworkElement group in groups)
                        CollectByTypeName(group, "DragUndockHeader", tabs, 0);
                }
                // Fallbacks adicionais: builds do SSMS 22 podem usar outros nomes de tipo
                // para a aba. Tenta candidatos conhecidos e qualquer tipo terminado em "TabItem".
                if (tabs.Count == 0)
                {
                    CollectByTypeName(mainWindow, "TitleBarButton", tabs, 0);
                    CollectByTypeName(mainWindow, "DragUndockHeader", tabs, 0);
                    CollectBySuffix(mainWindow, "TabItem", tabs, 0);
                }

                if (tabs.Count == 0)
                {
                    LogTabTypeNamesOnce(mainWindow);
                    return;
                }

                foreach (FrameworkElement tab in tabs)
                {
                    string header = NormalizeCaption((tab as HeaderedContentControl)?.Header?.ToString());
                    // Sem cor aprendida para esta legenda: NÃO limpa — abas visitadas mantêm a cor.
                    if (header == null || !_colorsByCaption.TryGetValue(header, out Color? color))
                        continue;

                    // Candidatos de pintura: as bordas curvas conhecidas e, como fallback, qualquer
                    // Border/Grid descendente (mais builds do SSMS são pintadas).
                    FrameworkElement target =
                        FindDescendantByTypeName(tab, "SimpleCurvedBorder") ??
                        FindDescendantByTypeName(tab, "TopCurvedBorder") ??
                        FindDescendantOfType<Border>(tab) ??
                        FindDescendantOfType<System.Windows.Controls.Grid>(tab) ??
                        tab;

                    if (color == null)
                    {
                        target.ClearValue(Control.BackgroundProperty);
                        target.ClearValue(Border.BackgroundProperty);
                    }
                    else
                    {
                        var brush = new SolidColorBrush(color.Value);
                        brush.Freeze();
                        if (target is Border)
                            target.SetValue(Border.BackgroundProperty, brush);
                        else if (target is Panel)
                            target.SetValue(Panel.BackgroundProperty, brush);
                        else
                            target.SetValue(Control.BackgroundProperty, brush);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_loggedWalkFailure)
                {
                    _loggedWalkFailure = true;
                    Log.Error("TabColorizer: falha ao varrer/pintar a árvore visual (abas sem cor)", ex);
                }
            }
        }

        /// <summary>
        /// Limpa o cache de cores, reclassifica a janela ativa com as novas regras
        /// e agenda a reaplicação a todas as abas. Deve ser chamado na UI thread
        /// após gravar as novas regras em EnvironmentStore.
        /// </summary>
        internal static void RefreshAfterRulesChanged()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _colorsByCaption.Clear();
                if (_dte != null)
                    LearnActiveWindow(_dte);
                ScheduleReapply();
            }
            catch (Exception ex)
            {
                Log.Error("TabColorizer.RefreshAfterRulesChanged", ex);
            }
        }

        private static string NormalizeCaption(string caption)
        {
            if (string.IsNullOrWhiteSpace(caption)) return null;
            string result = caption.Trim();
            if (result.EndsWith("*", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 1).TrimEnd();
            return result;
        }

        private static Color? ParseColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        private static void CollectByTypeName(DependencyObject root, string typeName, List<FrameworkElement> result, int depth)
        {
            if (root == null || depth > 30) return;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe && fe.GetType().Name == typeName)
                    result.Add(fe);
                CollectByTypeName(child, typeName, result, depth + 1);
            }
        }

        private static FrameworkElement FindDescendantByTypeName(DependencyObject root, string typeName)
        {
            var matches = new List<FrameworkElement>();
            CollectByTypeName(root, typeName, matches, 0);
            return matches.Count > 0 ? matches[0] : null;
        }

        // Coleta FrameworkElements cujo nome de tipo termina com 'suffix' (ex.: "TabItem").
        private static void CollectBySuffix(DependencyObject root, string suffix, List<FrameworkElement> result, int depth)
        {
            if (root == null || depth > 30) return;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe && fe.GetType().Name.EndsWith(suffix, StringComparison.Ordinal))
                    result.Add(fe);
                CollectBySuffix(child, suffix, result, depth + 1);
            }
        }

        // Primeiro descendente do tipo T (busca em largura simples via recursão).
        private static FrameworkElement FindDescendantOfType<T>(DependencyObject root) where T : FrameworkElement
            => FindDescendantOfType<T>(root, 0);

        private static FrameworkElement FindDescendantOfType<T>(DependencyObject root, int depth) where T : FrameworkElement
        {
            if (root == null || depth > 30) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                FrameworkElement found = FindDescendantOfType<T>(child, depth + 1);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Diagnóstico (uma vez): quando nenhuma aba é encontrada, loga os nomes de tipo distintos
        /// dos FrameworkElements sob a janela principal cujo nome contém "Tab"/"DocumentGroup"/"Title"
        /// (profundidade ≤ 12), para revelar o tipo real da aba neste build do SSMS e permitir ajuste.
        /// </summary>
        private static void LogTabTypeNamesOnce(DependencyObject mainWindow)
        {
            if (_loggedNoTabs) return;
            _loggedNoTabs = true;
            try
            {
                var names = new SortedSet<string>(StringComparer.Ordinal);
                CollectCandidateTypeNames(mainWindow, names, 0);
                string list = names.Count > 0 ? string.Join(", ", names) : "(nenhum)";
                Log.Info("TabColorizer: tipos de aba encontrados: " + list);
            }
            catch (Exception ex)
            {
                Log.Error("TabColorizer.LogTabTypeNamesOnce", ex);
            }
        }

        private static void CollectCandidateTypeNames(DependencyObject root, SortedSet<string> names, int depth)
        {
            if (root == null || depth > 12) return;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe)
                {
                    string n = fe.GetType().Name;
                    if (n.IndexOf("Tab", StringComparison.Ordinal) >= 0 ||
                        n.IndexOf("DocumentGroup", StringComparison.Ordinal) >= 0 ||
                        n.IndexOf("Title", StringComparison.Ordinal) >= 0)
                        names.Add(n);
                }
                CollectCandidateTypeNames(child, names, depth + 1);
            }
        }
    }
}
