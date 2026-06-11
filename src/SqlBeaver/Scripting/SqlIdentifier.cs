namespace SqlBeaver.Scripting
{
    public static class SqlIdentifier
    {
        /// <summary>Envolve em colchetes quando o identificador não é "regular" ([A-Za-z0-9_]).</summary>
        public static string Bracket(string identifier)
        {
            foreach (char c in identifier)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return "[" + identifier.Replace("]", "]]") + "]";
            }
            return identifier;
        }
    }
}
