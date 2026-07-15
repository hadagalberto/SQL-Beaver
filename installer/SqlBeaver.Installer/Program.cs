using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlBeaver.Installer
{
    /// <summary>
    /// Instalador standalone do SQL Beaver para SSMS 22+. Sem repositório: baixa o .vsix mais
    /// recente da release do GitHub e instala pelo VSIXInstaller.exe OFICIAL (registro robusto em
    /// qualquer máquina, ao contrário de extrair a pasta na mão). A configuração do usuário
    /// (%LOCALAPPDATA%\SqlBeaver) NUNCA é apagada — só é copiada para backup. Sem elevação.
    /// Grava um log em %TEMP%\SqlBeaver-Setup.log para diagnóstico.
    /// </summary>
    internal static class Program
    {
        private const string Repo = "hadagalberto/SQL-Beaver";
        private const string ReleaseApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        private const string ExtensionId = "SqlBeaver.E7F4C9D2-3B61-4A8E-9C57-D2A41B6F8E03";

        // Extensão só roda no SSMS 22+ (manifest InstallationTarget [22.0,)).
        private const int MinSsmsMajor = 22;

        private static string LocalAppData =>
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        private static StreamWriter _log;
        private static string _logPath;

        private static int Main()
        {
            InitLog();
            try
            {
                Console.Title = "SQL Beaver — Instalador";
                Head("SQL Beaver — Instalador");
                Info("Instala/atualiza a extensão no SSMS 22+. A sua configuração é preservada.");
                Info("Log detalhado: " + _logPath + "\n");

                if (SsmsRunning())
                {
                    Warn("O SSMS está aberto. Feche-o e rode o instalador de novo.");
                    return Pause(1);
                }

                List<string> ideDirs = FindIdeDirs();
                if (ideDirs.Count == 0)
                {
                    Err("Não encontrei o SSMS 22+ (Ssms.exe) neste PC. Instale o SSMS 22 primeiro.");
                    return Pause(2);
                }
                Info("SSMS 22+ encontrado em: " + string.Join(" ; ", ideDirs));

                string vsixInstaller = FindVsixInstaller(ideDirs);
                if (vsixInstaller == null)
                {
                    Err("VSIXInstaller.exe não encontrado ao lado do Ssms.exe. Instalação abortada.");
                    return Pause(3);
                }
                Info("VSIXInstaller: " + vsixInstaller);

                // 1) Backup da configuração do usuário (preservada de qualquer forma).
                string cfg = Path.Combine(LocalAppData, "SqlBeaver");
                if (Directory.Exists(cfg))
                {
                    string backup = cfg + ".bak-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    try { CopyDir(cfg, backup); Ok("Configuração salva em backup: " + backup); }
                    catch (Exception ex) { Warn("Backup da config falhou (" + ex.Message + ") — ela não será apagada mesmo assim."); }
                }

                // 2) Baixar o .vsix mais recente da release.
                Info("Baixando a versão mais recente do GitHub…");
                string vsix = DownloadLatestVsix();
                Ok("Baixado: " + Path.GetFileName(vsix));

                // 3) Desinstalar versão antiga (best-effort; ignora se não instalada).
                Info("Removendo versão antiga (se houver)…");
                int un = RunVsix(vsixInstaller, "/quiet /uninstall:" + ExtensionId, 300);
                Info("  VSIXInstaller /uninstall exit=" + un + (un == 0 ? " (removida)" : " (nada a remover ou já ausente)"));

                // 4) Instalar a nova — /quiet primeiro; se falhar, com UI para o usuário ver o motivo.
                Info("Instalando…");
                int inst = RunVsix(vsixInstaller, "/quiet \"" + vsix + "\"", 600);
                if (inst != 0 && inst != 1001)
                {
                    Warn("Instalação silenciosa retornou " + inst + " — abrindo o instalador com interface…");
                    inst = RunVsix(vsixInstaller, "\"" + vsix + "\"", 600);
                }
                if (inst != 0 && inst != 1001)
                {
                    Err("VSIXInstaller retornou código " + inst + ". Veja o log: " + _logPath);
                    return Pause(4);
                }
                Ok("Extensão instalada (VSIXInstaller exit=" + inst + ").");

                // 5) Limpar cache MEF + registrar (headless).
                int mef = ClearMefCaches();
                Info("Cache MEF limpo em " + mef + " instância(s).");
                Info("Registrando no SSMS (headless, ~30s)…");
                MergeShellConfig(ideDirs);

                // 6) Verificação.
                int copies = CountInstalledDlls(out string where);
                if (copies >= 1)
                    Ok($"Verificado: {copies} cópia(s) instalada(s). Ex.: {where}");
                else
                    Warn("Não localizei o SqlBeaver.dll após instalar — pode ter ido para uma pasta protegida. Veja o log.");

                Info("");
                Ok("Instalação concluída. Abra o SSMS (a 1ª abertura é mais lenta — reconstrói o cache).");
                Info("Se o menu 'Tools > SQL Beaver' não aparecer na 1ª vez, feche e abra o SSMS de novo.");
                Info("Sua configuração (chaves de IA, ambientes, snippets, histórico) foi preservada.");
                return Pause(0);
            }
            catch (Exception ex)
            {
                Err("Falha na instalação: " + ex);
                return Pause(99);
            }
            finally
            {
                try { _log?.Flush(); _log?.Dispose(); } catch { }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Descoberta
        // ─────────────────────────────────────────────────────────────────────

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
                    if (SsmsMajorFromPath(root) < MinSsmsMajor) continue;
                    foreach (string exe in SafeFindFiles(root, "Ssms.exe"))
                        dirs.Add(Path.GetDirectoryName(exe));
                }
            }

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
                catch { }
            }

            return dirs.ToList();
        }

        private static string FindVsixInstaller(List<string> ideDirs)
        {
            foreach (string dir in ideDirs)
            {
                string p = Path.Combine(dir, "VSIXInstaller.exe");
                if (File.Exists(p)) return p;
            }
            // Fallback: procura sob a raiz de cada instalação do SSMS.
            foreach (string dir in ideDirs)
            {
                try
                {
                    string root = Directory.GetParent(dir)?.Parent?.FullName; // ...\Common7\IDE -> ...\
                    if (root == null) continue;
                    string found = SafeFindFiles(root, "VSIXInstaller.exe").FirstOrDefault();
                    if (found != null) return found;
                }
                catch { }
            }
            return null;
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
            Info("  URL: " + url);
            string dest = Path.Combine(Path.GetTempPath(), "SqlBeaver-" + Path.GetRandomFileName() + ".vsix");
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "SqlBeaver-Installer");
                wc.DownloadFile(url, dest);
            }
            return dest;
        }

        // ─────────────────────────────────────────────────────────────────────
        // VSIXInstaller / MEF / merge / verificação
        // ─────────────────────────────────────────────────────────────────────

        private static int RunVsix(string installerExe, string args, int timeoutSec)
        {
            try
            {
                var psi = new ProcessStartInfo(installerExe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return -1;
                    if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(); } catch { } return -2; }
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Warn("VSIXInstaller falhou ao executar (" + ex.Message + ").");
                return -3;
            }
        }

        private static int ClearMefCaches()
        {
            int n = 0;
            foreach (string root in FindLocalRoots())
            {
                string mef = Path.Combine(root, "ComponentModelCache");
                try { if (Directory.Exists(mef)) { Directory.Delete(mef, recursive: true); n++; } }
                catch { }
            }
            return n;
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
                    using (Process p = Process.Start(psi))
                    {
                        if (p != null && !p.WaitForExit(120000)) { try { p.Kill(); } catch { } }
                    }
                }
                catch (Exception ex) { Warn("Não consegui pré-mesclar em " + dir + " (" + ex.Message + ") — o SSMS mescla na 1ª abertura."); }
            }
        }

        private static int CountInstalledDlls(out string firstDir)
        {
            firstDir = null;
            int count = 0;
            foreach (string root in FindLocalRoots())
            {
                string ext = Path.Combine(root, "Extensions");
                if (!Directory.Exists(ext)) continue;
                foreach (string dll in SafeFindFiles(ext, "SqlBeaver.dll"))
                {
                    count++;
                    if (firstDir == null) firstDir = Path.GetDirectoryName(dll);
                }
            }
            return count;
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

        private static void InitLog()
        {
            try
            {
                _logPath = Path.Combine(Path.GetTempPath(), "SqlBeaver-Setup.log");
                _log = new StreamWriter(_logPath, append: false, encoding: Encoding.UTF8) { AutoFlush = true };
                _log.WriteLine("SQL Beaver Installer — " + DateTime.Now.ToString("o"));
            }
            catch { _log = null; }
        }

        private static void ToLog(string tag, string s)
        {
            try { _log?.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + tag + s); } catch { }
        }

        private static void Head(string s)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n" + s + "\n" + new string('=', s.Length));
            Console.ResetColor();
            ToLog("", s);
        }

        private static void Info(string s) { Console.WriteLine(s); ToLog("", s); }
        private static void Ok(string s)   { Color(ConsoleColor.Green, "[ok] " + s); ToLog("[ok] ", s); }
        private static void Warn(string s) { Color(ConsoleColor.Yellow, "[aviso] " + s); ToLog("[aviso] ", s); }
        private static void Err(string s)  { Color(ConsoleColor.Red, "[erro] " + s); ToLog("[erro] ", s); }

        private static void Color(ConsoleColor c, string s)
        {
            Console.ForegroundColor = c;
            Console.WriteLine(s);
            Console.ResetColor();
        }

        private static int Pause(int code)
        {
            Console.WriteLine("\nPressione qualquer tecla para sair…");
            try { Console.ReadKey(true); } catch { }
            return code;
        }
    }
}
