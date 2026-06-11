using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text.Editor;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Environments
{
    /// <summary>
    /// Faixa colorida no topo do editor com o ambiente da conexão ativa
    /// (Produção/Homologação/Desenvolvimento etc.). Atualiza no foco e a cada 5s
    /// enquanto a view está visível. Nunca lança (margem vazia em falha).
    /// </summary>
    internal sealed class EnvironmentMargin : Border, IWpfTextViewMargin
    {
        public const string MarginName = "SqlBeaverEnvironmentMargin";

        private readonly IWpfTextView _textView;
        private readonly TextBlock _label;
        private readonly DispatcherTimer _timer;
        private bool _disposed;

        public EnvironmentMargin(IWpfTextView textView)
        {
            _textView = textView;
            _label = new TextBlock
            {
                Margin              = new Thickness(8, 2, 8, 2),
                FontWeight          = FontWeights.SemiBold,
                Foreground          = Brushes.White,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            Child      = _label;
            Visibility = Visibility.Collapsed;

            _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromSeconds(5),
            };
            _timer.Tick += (s, e) => Refresh();

            _textView.GotAggregateFocus  += OnGotFocus;
            _textView.LostAggregateFocus += OnLostFocus;
            _textView.Closed             += OnClosed;

            Refresh();
        }

        private void OnGotFocus(object sender, EventArgs e) { Refresh(); _timer.Start(); }
        private void OnLostFocus(object sender, EventArgs e) => _timer.Stop();
        private void OnClosed(object sender, EventArgs e)   => Dispose();

        private void Refresh()
        {
            try
            {
                ActiveConnection connection = ConnectionService.GetActiveConnection();
                EnvironmentRule rule = connection == null
                    ? null
                    : EnvironmentStore.MatchActive(connection.Server, connection.Database);

                if (rule == null)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                Background   = ParseColor(rule.Color);
                _label.Text  = $"{rule.Name.ToUpperInvariant()}  —  {connection.Server} · {connection.Database}";
                Visibility   = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Visibility = Visibility.Collapsed;
                Log.Error("EnvironmentMargin.Refresh", ex);
            }
        }

        private static Brush ParseColor(string hex)
        {
            try
            {
                return (Brush)new BrushConverter().ConvertFromString(
                    string.IsNullOrWhiteSpace(hex) ? "#666666" : hex);
            }
            catch
            {
                return Brushes.DimGray;
            }
        }

        public FrameworkElement VisualElement => this;
        public double           MarginSize    => ActualHeight;
        public bool             Enabled       => true;

        public ITextViewMargin GetTextViewMargin(string marginName)
            => string.Equals(marginName, MarginName, StringComparison.OrdinalIgnoreCase) ? this : null;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _textView.GotAggregateFocus  -= OnGotFocus;
            _textView.LostAggregateFocus -= OnLostFocus;
            _textView.Closed             -= OnClosed;
        }
    }
}
