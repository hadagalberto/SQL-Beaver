using SqlBeaver.Analysis;

namespace SqlBeaver.Editing
{
    /// <summary>Ação ao digitar aspa simples.</summary>
    public enum QuoteAction
    {
        /// <summary>Comportamento normal: só inserir a aspa digitada.</summary>
        None,
        /// <summary>Próximo char já é uma aspa de fechamento: pular por cima (não duplicar).</summary>
        SkipOver,
        /// <summary>Inserir o par de fechamento e deixar o cursor entre as aspas.</summary>
        InsertPair,
    }

    /// <summary>
    /// Decide o auto-fechamento de aspa simples (<c>'</c>). Puro e testável.
    /// Cuidados: não duplica quando o próximo char já é a aspa de fechamento (pula por cima);
    /// não pareia dentro de string/comentário (a aspa ali fecha a string normalmente);
    /// só pareia quando o próximo char é "fim de token" (fim, espaço, <c>) , ; ]</c>).
    /// </summary>
    public static class AutoCloseQuote
    {
        public static QuoteAction Decide(string text, int caret)
        {
            if (text == null || caret < 0 || caret > text.Length)
                return QuoteAction.None;

            char next = caret < text.Length ? text[caret] : '\0';

            // Próximo char é aspa → pular por cima (fecha sem duplicar). Vale mesmo dentro da string,
            // que é justamente o caso do par recém-inserido: '|' + digitar ' → cursor pula a de fechar.
            if (next == '\'')
                return QuoteAction.SkipOver;

            // Dentro de string/comentário: a aspa digitada fecha a string — não pareia.
            if (SqlContextAnalyzer.IsInsideCommentOrStringAt(text, caret))
                return QuoteAction.None;

            return ShouldClose(next) ? QuoteAction.InsertPair : QuoteAction.None;
        }

        /// <summary>Só pareia se o que vem depois não for "colado" num identificador/valor.</summary>
        private static bool ShouldClose(char next)
            => next == '\0' || char.IsWhiteSpace(next) ||
               next == ')' || next == ',' || next == ';' || next == ']';
    }
}
