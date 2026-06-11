using System;
using System.Collections.Generic;

namespace SqlBeaver.Session
{
    /// <summary>
    /// Estado ACUMULATIVO puro da sessão: mantém um conjunto de <see cref="SessionEntry"/>
    /// em ordem de primeira aparição (first-seen), indexado por caption. Cada caption
    /// recebe um nome de aba estável <c>tab-NN.sql</c> na primeira vez que é visto e
    /// MANTÉM esse nome enquanto existir — upserts subsequentes nunca renumeram.
    ///
    /// Numeração: ao inserir um caption novo, atribui o MENOR número não utilizado entre
    /// os captions atuais (determinístico e estável). Remover um caption libera seu número
    /// para reuso pelo PRÓXIMO caption novo. O acumulador é puro: ele só decide os nomes
    /// <c>tab-NN.sql</c>; o serviço junta com o diretório de sessão e faz o I/O.
    /// </summary>
    public sealed class SessionAccumulator
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, SessionEntry> _byCaption =
            new Dictionary<string, SessionEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Entradas na ordem de primeira aparição (first-seen).</summary>
        public IReadOnlyList<SessionEntry> Entries
        {
            get
            {
                var list = new List<SessionEntry>(_order.Count);
                foreach (string caption in _order)
                    list.Add(_byCaption[caption]);
                return list;
            }
        }

        /// <summary>
        /// Insere ou atualiza a entrada do <paramref name="caption"/>. Em uma inserção nova,
        /// atribui um nome de aba estável <c>tab-NN.sql</c> (preservado em upserts futuros).
        /// Atualiza hash/server/database/savedAt mantendo a posição. Retorna o nome do
        /// arquivo de aba (bare, sem diretório) associado ao caption.
        /// </summary>
        public string Upsert(string caption, string contentHash, string server, string database, string savedAt)
        {
            if (caption == null) throw new ArgumentNullException(nameof(caption));

            if (_byCaption.TryGetValue(caption, out SessionEntry existing))
            {
                existing.ContentHash = contentHash;
                existing.Server = server;
                existing.Database = database;
                existing.SavedAt = savedAt;
                return existing.File;
            }

            string tabName = NextTabName();
            var entry = new SessionEntry
            {
                File = tabName,
                Caption = caption,
                Server = server,
                Database = database,
                SavedAt = savedAt,
                ContentHash = contentHash
            };
            _byCaption[caption] = entry;
            _order.Add(caption);
            return tabName;
        }

        /// <summary>Remove o caption do conjunto acumulado. Retorna true se existia.</summary>
        public bool Remove(string caption)
        {
            if (caption == null) return false;
            if (!_byCaption.Remove(caption)) return false;

            for (int i = 0; i < _order.Count; i++)
            {
                if (string.Equals(_order[i], caption, StringComparison.OrdinalIgnoreCase))
                {
                    _order.RemoveAt(i);
                    break;
                }
            }
            return true;
        }

        /// <summary>Esvazia o conjunto acumulado (a numeração recomeça em tab-01).</summary>
        public void Clear()
        {
            _order.Clear();
            _byCaption.Clear();
        }

        /// <summary>Menor tab-NN não usado entre os captions atuais.</summary>
        private string NextTabName()
        {
            var used = new HashSet<int>();
            foreach (SessionEntry e in _byCaption.Values)
            {
                int num = ParseTabNumber(e.File);
                if (num > 0) used.Add(num);
            }

            int n = 1;
            while (used.Contains(n)) n++;
            return $"tab-{n:00}.sql";
        }

        private static int ParseTabNumber(string file)
        {
            // file == "tab-NN.sql"
            if (string.IsNullOrEmpty(file)) return 0;
            if (!file.StartsWith("tab-", StringComparison.OrdinalIgnoreCase)) return 0;
            int dot = file.IndexOf('.');
            int start = 4;
            int len = (dot < 0 ? file.Length : dot) - start;
            if (len <= 0) return 0;
            return int.TryParse(file.Substring(start, len), out int n) ? n : 0;
        }
    }
}
