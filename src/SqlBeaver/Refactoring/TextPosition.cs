namespace SqlBeaver.Refactoring
{
    /// <summary>Converte offset de string (CRLF = 2 chars) em linha/coluna 1-based
    /// para uso com EditPoint.MoveToLineAndOffset (EnvDTE conta quebras como 1 char).</summary>
    public static class TextPosition
    {
        public static void FromOffset(string text, int offset, out int line, out int column)
        {
            line = 1;
            int lineStart = 0;
            for (int i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }
            column = offset - lineStart + 1;
        }
    }
}
