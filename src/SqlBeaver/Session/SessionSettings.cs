using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Session
{
    [DataContract(Namespace = "")]
    public sealed class SessionSettingsFile
    {
        [DataMember(Name = "restoreOnStartup")]
        public bool RestoreOnStartup { get; set; }
    }

    /// <summary>
    /// Configurações de sessão persistidas em %LOCALAPPDATA%\SqlBeaver\session-settings.json.
    /// Hoje só controla se as abas da última sessão são reabertas ao iniciar (padrão: ligado).
    /// </summary>
    public static class SessionSettings
    {
        private static readonly object _lock = new object();
        private static bool _loaded;
        private static bool _restoreOnStartup = true; // padrão: reabrir abas

        private static string FilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlBeaver", "session-settings.json");

        /// <summary>Reabrir as abas da última sessão ao iniciar o SSMS.</summary>
        public static bool RestoreOnStartup
        {
            get { EnsureLoaded(); return _restoreOnStartup; }
            set { lock (_lock) { _restoreOnStartup = value; _loaded = true; Save(); } }
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    string path = FilePath;
                    if (!File.Exists(path)) return;
                    using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                    {
                        var ser = new DataContractJsonSerializer(typeof(SessionSettingsFile));
                        var f = ser.ReadObject(ms) as SessionSettingsFile;
                        if (f != null) _restoreOnStartup = f.RestoreOnStartup;
                    }
                }
                catch (Exception ex) { Log.Error("SessionSettings.Load", ex); }
            }
        }

        private static void Save()
        {
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(typeof(SessionSettingsFile));
                    ser.WriteObject(ms, new SessionSettingsFile { RestoreOnStartup = _restoreOnStartup });
                    File.WriteAllBytes(path, ms.ToArray());
                }
            }
            catch (Exception ex) { Log.Error("SessionSettings.Save", ex); }
        }
    }
}
