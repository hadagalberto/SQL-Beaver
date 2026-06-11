using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Snippets
{
    /// <summary>Carrega o catálogo de snippets (built-ins + snippets.json do usuário);
    /// cria o snippets.json de exemplo na primeira execução. Falhas degradam para os
    /// padrões embutidos. O catálogo fica cacheado e pode ser recarregado em runtime
    /// via <see cref="Reload"/> (gerenciador de snippets) — leitura sem lock, swap com lock.</summary>
    internal static class SnippetStore
    {
        private static readonly object _lock = new object();
        private static volatile IReadOnlyDictionary<string, SnippetDefinition> _catalog;

        public static IReadOnlyDictionary<string, SnippetDefinition> Catalog
        {
            get
            {
                IReadOnlyDictionary<string, SnippetDefinition> local = _catalog;
                if (local != null) return local;

                lock (_lock)
                {
                    if (_catalog == null)
                        _catalog = LoadCatalog();
                    return _catalog;
                }
            }
        }

        /// <summary>Caminho do snippets.json do usuário em %LOCALAPPDATA%\SqlBeaver.</summary>
        public static string UserSnippetsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlBeaver", "snippets.json");

        /// <summary>Relê o snippets.json do usuário e re-merge sobre os built-ins,
        /// trocando o catálogo cacheado em memória (mesma disciplina de lock).</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _catalog = LoadCatalog();
            }
        }

        private static IReadOnlyDictionary<string, SnippetDefinition> LoadCatalog()
        {
            try
            {
                string path = UserSnippetsPath;
                string dir = Path.GetDirectoryName(path);

                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path,
                        "{\r\n  \"snippets\": [\r\n    { \"shortcut\": \"meusnip\", \"title\": \"Exemplo\", " +
                        "\"expansion\": \"SELECT $cursor$ FROM \", \"description\": \"Edite este arquivo e reinicie o SSMS\" }\r\n  ]\r\n}\r\n",
                        Encoding.UTF8);
                    Log.Info("snippets.json de exemplo criado em " + path);
                }

                string json = File.ReadAllText(path);
                var catalog = SnippetCatalog.Load(json);
                Log.Info($"Snippets carregados: {catalog.Count} atalho(s).");
                return catalog;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao carregar snippets.json — usando padrões", ex);
                return SnippetCatalog.Load(null);
            }
        }
    }
}
