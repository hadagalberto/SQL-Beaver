using System;
using System.Collections.Generic;
using System.IO;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Snippets
{
    /// <summary>Carrega o catálogo uma vez por sessão; cria o snippets.json de exemplo
    /// na primeira execução. Falhas degradam para os padrões embutidos.</summary>
    internal static class SnippetStore
    {
        private static readonly Lazy<IReadOnlyDictionary<string, SnippetDefinition>> _catalog =
            new Lazy<IReadOnlyDictionary<string, SnippetDefinition>>(LoadCatalog);

        public static IReadOnlyDictionary<string, SnippetDefinition> Catalog => _catalog.Value;

        private static IReadOnlyDictionary<string, SnippetDefinition> LoadCatalog()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlBeaver");
                string path = Path.Combine(dir, "snippets.json");

                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path,
                        "{\r\n  \"snippets\": [\r\n    { \"shortcut\": \"meusnip\", \"title\": \"Exemplo\", " +
                        "\"expansion\": \"SELECT $cursor$ FROM \", \"description\": \"Edite este arquivo e reinicie o SSMS\" }\r\n  ]\r\n}\r\n");
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
