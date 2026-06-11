using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlBeaver.Environments
{
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(EnvironmentMargin.MarginName)]
    [MarginContainer(PredefinedMarginNames.Top)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class EnvironmentMarginProvider : IWpfTextViewMarginProvider
    {
        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin marginContainer)
            => new EnvironmentMargin(wpfTextViewHost.TextView);
    }
}
