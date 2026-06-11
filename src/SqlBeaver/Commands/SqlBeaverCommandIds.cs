using System;

namespace SqlBeaver.Commands
{
    /// <summary>
    /// GUIDs e IDs dos comandos nomeados (devem espelhar VSCommandTable.vsct).
    /// </summary>
    internal static class SqlBeaverCommandIds
    {
        public static readonly Guid CommandSetGuid = new Guid("D7A9B3E4-5C12-4F8A-9B6D-3E7F1A2C4B5D");
        public const int FormatDocument   = 0x0100;
        public const int FindObject       = 0x0101;
        public const int GoToDefinition   = 0x0102;
        public const int FindReferences   = 0x0103;
        public const int QueryHistory     = 0x0104;
        public const int RecoverSession   = 0x0105;
        public const int Environments     = 0x0106;
    }
}
