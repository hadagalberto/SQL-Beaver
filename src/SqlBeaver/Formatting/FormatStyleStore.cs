using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Formatting
{
    /// <summary>
    /// Gerencia estilos de formatação nomeados em
    /// %LOCALAPPDATA%\SqlBeaver\formats\ (um .json por estilo).
    /// O estilo ativo é persistido em %LOCALAPPDATA%\SqlBeaver\formatstyle.json.
    ///
    /// Migração (primeira carga):
    ///   - se a pasta formats\ não existir: cria-a
    ///   - se format.json legado existir: copia para formats\Padrao.json
    ///   - senão: grava defaults em formats\Padrao.json
    ///   - define active = "Padrao" (ou primeiro disponível)
    ///
    /// Thread-safe: volatile cache + _lock para escritas.
    /// Escritas atômicas via tmp + File.Replace/Move.
    /// </summary>
    internal static class FormatStyleStore
    {
        private static readonly object _lock = new object();

        // Volatile: pode ser lido sem lock; escrito com lock.
        private static volatile IReadOnlyList<string> _cachedStyles;
        private static volatile string _cachedActive;

        // ── Paths ─────────────────────────────────────────────────────────────

        private static string BaseDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlBeaver");

        private static string FormatsDir => Path.Combine(BaseDir, "formats");

        private static string PointerPath => Path.Combine(BaseDir, "formatstyle.json");

        private static string LegacyFormatPath => Path.Combine(BaseDir, "format.json");

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Lista os nomes de estilos disponíveis (sem extensão .json), em ordem.</summary>
        public static IReadOnlyList<string> ListStyles()
        {
            EnsureLoaded();
            return _cachedStyles;
        }

        /// <summary>Nome do estilo atualmente ativo.</summary>
        public static string ActiveStyleName
        {
            get
            {
                EnsureLoaded();
                return _cachedActive;
            }
        }

        /// <summary>
        /// Carrega e retorna as opções do estilo ativo.
        /// Fallback para defaults se o arquivo estiver ausente ou corrompido.
        /// </summary>
        public static FormatOptions GetActiveOptions()
        {
            EnsureLoaded();
            string active = _cachedActive;
            if (string.IsNullOrEmpty(active))
                return FormatOptions.CreateDefault();

            return LoadStyle(active);
        }

        /// <summary>Define o estilo ativo e persiste o ponteiro.</summary>
        public static void SetActive(string name)
        {
            lock (_lock)
            {
                EnsureLoaded();
                // Resolve against available (validates it exists or keep as-is)
                var styles = _cachedStyles;
                string resolved = FormatStyleResolver.ResolveActive(styles, name) ?? name;
                _cachedActive = resolved;
                SavePointer(resolved);
            }
        }

        /// <summary>
        /// Salva (cria ou substitui) o estilo com o nome dado.
        /// Recarrega a lista de estilos em memória.
        /// </summary>
        public static void Save(string name, FormatOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Nome do estilo não pode ser vazio.", nameof(name));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            lock (_lock)
            {
                Directory.CreateDirectory(FormatsDir);
                string path = GetStylePath(name);
                AtomicWrite(path, options.Serialize());
                InvalidateStylesCache();
                Log.Info($"Estilo de formatação '{name}' salvo em {path}");
            }
        }

        /// <summary>Exclui o estilo. Se era o ativo, muda para o primeiro disponível.</summary>
        public static void Delete(string name)
        {
            lock (_lock)
            {
                string path = GetStylePath(name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log.Info($"Estilo '{name}' excluído.");
                }

                InvalidateStylesCache();

                // If the deleted style was active, pick a new one
                if (string.Equals(_cachedActive, name, StringComparison.OrdinalIgnoreCase))
                {
                    var remaining = LoadStyleNames();
                    string newActive = remaining.Count > 0 ? remaining[0] : null;
                    _cachedActive = newActive;
                    if (newActive != null) SavePointer(newActive);
                }
            }
        }

        /// <summary>
        /// Duplica sourceName para newName. Retorna o nome efetivo do novo estilo.
        /// </summary>
        public static string Duplicate(string sourceName, string newName)
        {
            lock (_lock)
            {
                string srcPath  = GetStylePath(sourceName);
                string destPath = GetStylePath(newName);
                if (!File.Exists(srcPath))
                    throw new FileNotFoundException($"Estilo '{sourceName}' não encontrado.", srcPath);
                Directory.CreateDirectory(FormatsDir);
                File.Copy(srcPath, destPath, overwrite: true);
                InvalidateStylesCache();
                Log.Info($"Estilo '{sourceName}' duplicado como '{newName}'.");
                return newName;
            }
        }

        /// <summary>Renomeia um estilo. Atualiza o ponteiro se era o ativo.</summary>
        public static void Rename(string oldName, string newName)
        {
            lock (_lock)
            {
                string oldPath = GetStylePath(oldName);
                string newPath = GetStylePath(newName);
                if (!File.Exists(oldPath))
                    throw new FileNotFoundException($"Estilo '{oldName}' não encontrado.", oldPath);
                File.Move(oldPath, newPath);
                InvalidateStylesCache();

                if (string.Equals(_cachedActive, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    _cachedActive = newName;
                    SavePointer(newName);
                }

                Log.Info($"Estilo '{oldName}' renomeado para '{newName}'.");
            }
        }

        /// <summary>Retorna o caminho completo do arquivo .json de um estilo.</summary>
        public static string GetStylePath(string name)
        {
            string fileName = FormatStyleResolver.ToFileName(name);
            if (fileName == null)
                throw new ArgumentException($"Nome de estilo inválido: '{name}'", nameof(name));
            return Path.Combine(FormatsDir, fileName);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Garante que a migração foi feita e os caches estão preenchidos.
        /// Idempotente — só executa I/O na primeira chamada por sessão.
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_cachedStyles != null)
                return;

            lock (_lock)
            {
                if (_cachedStyles != null)
                    return;

                try
                {
                    MigrateIfNeeded();
                    var names   = LoadStyleNames();
                    string active = LoadPointer();
                    active = FormatStyleResolver.ResolveActive(names, active);
                    _cachedStyles = names;
                    _cachedActive = active;
                    Log.Info($"FormatStyleStore: {names.Count} estilo(s) carregado(s); ativo='{active}'.");
                }
                catch (Exception ex)
                {
                    Log.Error("FormatStyleStore: falha na carga inicial — usando defaults", ex);
                    _cachedStyles = Array.Empty<string>();
                    _cachedActive = null;
                }
            }
        }

        private static void MigrateIfNeeded()
        {
            if (Directory.Exists(FormatsDir))
                return; // already migrated or first-time set up already done

            Directory.CreateDirectory(FormatsDir);

            string padrao = Path.Combine(FormatsDir, "Padrao.json");

            bool migrated = false;
            if (File.Exists(LegacyFormatPath))
            {
                try
                {
                    // Preserve the user's existing customisations
                    File.Copy(LegacyFormatPath, padrao, overwrite: false);
                    Log.Info($"Migração: format.json copiado para {padrao}");
                    migrated = true;
                }
                catch (Exception ex)
                {
                    // Legado bloqueado/ilegível: não deixar o estilo Padrao ausente (senão a
                    // próxima sessão retorna cedo na migração e fica sem nenhum estilo).
                    Log.Error("Migração: falha ao copiar format.json; gravando defaults", ex);
                }
            }
            if (!migrated)
            {
                // Fresh install (ou cópia falhou) — write clean defaults
                File.WriteAllText(padrao, FormatOptions.CreateDefault().Serialize(),
                    System.Text.Encoding.UTF8);
                Log.Info($"Migração: Padrao.json de defaults criado em {padrao}");
            }

            // Also write the pointer pointing to "Padrao"
            SavePointer("Padrao");
        }

        private static IReadOnlyList<string> LoadStyleNames()
        {
            if (!Directory.Exists(FormatsDir))
                return Array.Empty<string>();

            string[] files = Directory.GetFiles(FormatsDir, "*.json");
            var names = new List<string>(files.Length);
            foreach (string f in files)
                names.Add(FormatStyleResolver.FromFileName(Path.GetFileName(f)));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static string LoadPointer()
        {
            try
            {
                if (!File.Exists(PointerPath))
                    return null;
                string json = File.ReadAllText(PointerPath, System.Text.Encoding.UTF8);
                // Simple parse: look for "active":"value"
                return ParseActiveFromJson(json);
            }
            catch
            {
                return null;
            }
        }

        private static void SavePointer(string active)
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                string json = $"{{\r\n  \"active\": \"{EscapeJsonString(active)}\"\r\n}}\r\n";
                AtomicWrite(PointerPath, json);
            }
            catch (Exception ex)
            {
                Log.Error("FormatStyleStore: falha ao salvar ponteiro de estilo ativo", ex);
            }
        }

        private static FormatOptions LoadStyle(string name)
        {
            try
            {
                string path = GetStylePath(name);
                if (!File.Exists(path))
                    return FormatOptions.CreateDefault();
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                return FormatOptions.Load(json);
            }
            catch (Exception ex)
            {
                Log.Error($"FormatStyleStore: falha ao carregar estilo '{name}' — usando defaults", ex);
                return FormatOptions.CreateDefault();
            }
        }

        /// <summary>Invalida o cache de lista de estilos (mas não o ativo).</summary>
        private static void InvalidateStylesCache()
        {
            _cachedStyles = null;
        }

        private static void AtomicWrite(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, System.Text.Encoding.UTF8);
            try
            {
                File.Replace(tmp, path, null);
            }
            catch (FileNotFoundException)
            {
                File.Move(tmp, path);
            }
        }

        /// <summary>
        /// Extrai o valor de "active" do JSON de ponteiro sem dependência de deserialização pesada.
        /// Formato esperado: { "active": "NomeDoEstilo" }
        /// </summary>
        private static string ParseActiveFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            // Look for "active" key followed by a string value
            const string key = "\"active\"";
            int ki = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (ki < 0) return null;

            int colon = json.IndexOf(':', ki + key.Length);
            if (colon < 0) return null;

            int quote1 = json.IndexOf('"', colon + 1);
            if (quote1 < 0) return null;

            int quote2 = json.IndexOf('"', quote1 + 1);
            if (quote2 < 0) return null;

            return json.Substring(quote1 + 1, quote2 - quote1 - 1);
        }

        private static string EscapeJsonString(string value)
        {
            if (value == null) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
