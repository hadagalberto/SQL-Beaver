using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace SqlBeaver.Installer
{
    /// <summary>
    /// Instalador standalone do SQL Beaver para SSMS. Sem repositório: baixa o .vsix mais
    /// recente da release do GitHub e instala numa pasta única de extensão, limpando a versão
    /// antiga. A configuração do usuário (%LOCALAPPDATA%\SqlBeaver) NUNCA é apagada — só é
    /// copiada para um backup por segurança. Funciona por-usuário (sem elevação).
    /// </summary>
    internal static class Program
    {
        private const string Repo = "hadagalberto/SQL-Beaver";
        private const string ReleaseApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

        private static string LocalAppData =>
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        private static int Main()
        {
            try
            {
                Console.Title = "SQL Beaver — Instalador";
                Head("SQL Beaver — Instalador");
                Console.WriteLine("Instala/atualiza a extensão no SSMS. A sua configuração é preservada.\n");

                if (SsmsRunning())
                {
                    Warn("O SSMS está aberto. Feche-o e rode o instalador de novo.");
                    return Pause(1);
                }

                List<string> localRoots = FindLocalRoots();
                List<string> ideDirs = FindIdeDirs();
                if (ideDirs.Count == 0)
                {
                    Err("Não encontrei o SSMS (Ssms.exe) neste PC. Instale o SSMS 22 primeiro.");
                    return Pause(2);
                }
                if (localRoots.Count == 0)
                {
                    Err("Nenhuma instância do SSMS inicializada (pasta de dados ausente). Abra o SSMS uma vez e rode de novo.");
                    return Pause(3);
                }

                // 1) Backup da configuração do usuário (preservada de qualquer forma).
                string cfg = Path.Combine(LocalAppData, "SqlBeaver");
                if (Directory.Exists(cfg))
                {
                    string backup = cfg + ".bak-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    try { CopyDir(cfg, backup); Ok("Configuração salva em backup: " + backup); }
                    catch (Exception ex) { Warn("Não consegui fazer backup da config (" + ex.Message + ") — ela não será apagada mesmo assim."); }
                }

                // 2) Baixar o .vsix mais recente da release.
                Console.WriteLine("Baixando a versão mais recente do GitHub…");
                string vsix = DownloadLatestVsix();
                Ok("Baixado: " + Path.GetFileName(vsix));

                // 3) Remover instalações antigas (todas as pastas com SqlBeaver.dll).
                int removed = RemoveOldInstalls(localRoots, ideDirs);
                Ok(removed > 0 ? $"Versão antiga removida ({removed} pasta(s))." : "Nenhuma versão anterior encontrada.");

                // 4) Instalar (extrair o vsix numa pasta única por instância).
                int installed = InstallToRoots(localRoots, vsix);
                Ok($"Instalado em {installed} instância(s) do SSMS.");

                // 5) Limpar cache MEF + invalidar config.
                ClearMefCaches(localRoots);
                MarkConfigChanged(localRoots, ideDirs);

                // 6) Pré-mesclar a config do shell (headless).
                Console.WriteLine("Registrando no SSMS (headless, ~30s)…");
                MergeShellConfig(ideDirs);

                Console.WriteLine();
                Ok("Instalação concluída. Abra o SSMS (a 1ª abertura é mais lenta — reconstrói o cache).");
                Console.WriteLine("Sua configuração (chaves de IA, ambientes, snippets, histórico) foi preservada.");
                return Pause(0);
            }
            catch (Exception ex)
            {
                Err("Falha na instalação: " + ex.Message);
                return Pause(99);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Descoberta (equivalente ao uninstall.ps1/deploy.ps1)
        // ─────────────────────────────────────────────────────────────────────

        // A extensão só roda no SSMS 22+ (manifest InstallationTarget [22.0,)). SSMS 19/20 têm
        // outro shell (não carregam o vsix) e nem entendem /updateconfiguration → NÃO tocar neles.
        private const int MinSsmsMajor = 22;

        // Extrai o major da versão do SSMS a partir de um caminho ("...Management Studio 22\..."
        // ou pasta de instância "22.0_hash"). Retorna 0 se não achar.
        private static int SsmsMajorFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            Match m = Regex.Match(path, @"Management Studio\s+(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int v)) return v;
            m = Regex.Match(Path.GetFileName(path), @"^(\d+)\.\d+_");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int v2)) return v2;
            return 0;
        }

        private static List<string> FindLocalRoots()
        {
            var roots = new List<string>();
            string baseDir = Path.Combine(LocalAppData, "Microsoft", "SSMS");
            if (!Directory.Exists(baseDir)) return roots;
            // Só INSTÂNCIAS "<versão>_<hash>" com versão >= 22 (ignora BackupFiles/vshub/etc. e SSMS antigos).
            foreach (string d in Directory.GetDirectories(baseDir))
                if (SsmsMajorFromPath(d) >= MinSsmsMajor)
                    roots.Add(d);
            return roots;
        }

        private static List<string> FindIdeDirs()
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string pf in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            })
            {
                if (string.IsNullOrEmpty(pf) || !Directory.Exists(pf)) continue;
                foreach (string root in SafeGlobDirs(pf, "Microsoft SQL Server Management Studio*"))
                {
                    if (SsmsMajorFromPath(root) < MinSsmsMajor) continue; // pula SSMS 19/20/21
                    foreach (string exe in SafeFindFiles(root, "Ssms.exe"))
                        dirs.Add(Path.GetDirectoryName(exe));
                }
            }

            // Registro App Paths.
            foreach (string sub in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Ssms.exe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\Ssms.exe",
            })
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(sub))
                    {
                        string exe = key?.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(exe) && File.Exists(exe) && SsmsMajorFromPath(exe) >= MinSsmsMajor)
                            dirs.Add(Path.GetDirectoryName(exe));
                    }
                }
                catch { /* best-effort */ }
            }

            return dirs.ToList();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Download da release
        // ─────────────────────────────────────────────────────────────────────

        private static string DownloadLatestVsix()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            string json;
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "SqlBeaver-Installer");
                wc.Headers.Add("Accept", "application/vnd.github+json");
                json = wc.DownloadString(ReleaseApi);
            }

            Match m = Regex.Match(json,
                "\"browser_download_url\"\\s*:\\s*\"([^\"]+SqlBeaver-[^\"]+\\.vsix)\"",
                RegexOptions.IgnoreCase);
            if (!m.Success)
                throw new Exception("A release mais recente não tem um .vsix anexado.");

            string url = m.Groups[1].Value;
            string dest = Path.Combine(Path.GetTempPath(), "SqlBeaver-" + Path.GetRandomFileName() + ".vsix");
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "SqlBeaver-Installer");
                wc.DownloadFile(url, dest);
            }
            return dest;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Remoção / instalação
        // ─────────────────────────────────────────────────────────────────────

        private static IEnumerable<string> ExtensionRoots(List<string> localRoots, List<string> ideDirs)
        {
            foreach (string r in localRoots) yield return Path.Combine(r, "Extensions");
            foreach (string d in ideDirs) yield return Path.Combine(d, "Extensions");
        }

        private static int RemoveOldInstalls(List<string> localRoots, List<string> ideDirs)
        {
            int count = 0;
            foreach (string ext in ExtensionRoots(localRoots, ideDirs))
            {
                if (!Directory.Exists(ext)) continue;
                foreach (string dll in SafeFindFiles(ext, "SqlBeaver.dll"))
                {
                    string dir = Path.GetDirectoryName(dll);
                    try { Directory.Delete(dir, recursive: true); count++; }
                    catch (Exception ex) { Warn("Sem acesso a " + dir + " (" + ex.Message + ")."); }
                }
            }
            return count;
        }

        private static int InstallToRoots(List<string> localRoots, string vsix)
        {
            int count = 0;
            foreach (string root in localRoots)
            {
                string dest = Path.Combine(root, "Extensions", "SqlBeaver");
                try
                {
                    if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
                    Directory.CreateDirectory(dest);
                    ZipFile.ExtractToDirectory(vsix, dest);
                    count++;
                }
                catch (Exception ex) { Warn("Falha ao instalar em " + dest + " (" + ex.Message + ")."); }
            }
            return count;
        }

        private static void ClearMefCaches(List<string> localRoots)
        {
            foreach (string root in localRoots)
            {
                string mef = Path.Combine(root, "ComponentModelCache");
                try { if (Directory.Exists(mef)) Directory.Delete(mef, recursive: true); }
                catch { /* best-effort */ }
            }
        }

        private static void MarkConfigChanged(List<string> localRoots, List<string> ideDirs)
        {
            foreach (string ext in ExtensionRoots(localRoots, ideDirs))
            {
                try
                {
                    if (!Directory.Exists(ext)) continue;
                    string marker = Path.Combine(ext, "extensions.configurationchanged");
                    File.WriteAllText(marker, string.Empty);
                    File.SetLastWriteTimeUtc(marker, DateTime.UtcNow);
                }
                catch { /* best-effort */ }
            }
        }

        private static void MergeShellConfig(List<string> ideDirs)
        {
            foreach (string dir in ideDirs)
            {
                string exe = Path.Combine(dir, "Ssms.exe");
                if (!File.Exists(exe)) continue;
                try
                {
                    var psi = new ProcessStartInfo(exe, "/updateconfiguration")
                    {
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                    };
                    Process p = Process.Start(psi);
                    if (p != null && !p.WaitForExit(120000))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch (Exception ex) { Warn("Não consegui pré-mesclar em " + dir + " (" + ex.Message + ") — o SSMS mescla na 1ª abertura."); }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static bool SsmsRunning()
        {
            try { return Process.GetProcessesByName("Ssms").Length > 0; }
            catch { return false; }
        }

        private static IEnumerable<string> SafeGlobDirs(string parent, string pattern)
        {
            try { return Directory.GetDirectories(parent, pattern); }
            catch { return Array.Empty<string>(); }
        }

        private static IEnumerable<string> SafeFindFiles(string root, string fileName)
        {
            try { return Directory.GetFiles(root, fileName, SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(src, dst));
            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(src, dst), overwrite: true);
        }

        private static void Head(string s)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n" + s + "\n" + new string('=', s.Length));
            Console.ResetColor();
        }

        private static void Ok(string s)   { Color(ConsoleColor.Green, "[ok] " + s); }
        private static void Warn(string s) { Color(ConsoleColor.Yellow, "[aviso] " + s); }
        private static void Err(string s)  { Color(ConsoleColor.Red, "[erro] " + s); }

        private static void Color(ConsoleColor c, string s)
        {
            Console.ForegroundColor = c;
            Console.WriteLine(s);
            Console.ResetColor();
        }

        private static int Pause(int code)
        {
            Console.WriteLine("\nPressione qualquer tecla para sair…");
            try { Console.ReadKey(true); } catch { /* sem console interativo */ }
            return code;
        }
    }
}
