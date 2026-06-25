using System;
using System.Data.SqlClient;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Connection
{
    public sealed class ActiveConnection
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string ConnectionString { get; set; }

        /// <summary>Token Entra da conexão viva do editor; null para auth Windows/SQL.</summary>
        public string AccessToken { get; set; }

        /// <summary>Tipo runtime do provider da conexão viva (ex.: Microsoft.Data.SqlClient.SqlConnection).
        /// Preenchido no caminho Entra MFA sem token; null nos demais modos.</summary>
        public Type ProviderConnectionType { get; set; }
    }

    /// <summary>
    /// Descobre a conexão da janela de query ativa via internals do SSMS
    /// (ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo.UIConnectionInfo).
    /// Reflection defensiva: qualquer falha vira null + uma linha de log, nunca exceção.
    /// Chamar na thread de UI.
    /// </summary>
    public static class ConnectionService
    {
        private static bool _loggedFailure;

        // Cache apenas de acertos: assemblies do SSMS carregam tarde, um miss agora
        // pode virar acerto depois.
        private static Type _serviceCacheType;
        private static PropertyInfo _scriptFactoryProperty;

        public static ActiveConnection GetActiveConnection()
        {
            try
            {
                return GetViaScriptFactory();
            }
            catch (Exception ex)
            {
                LogFailureOnce("Falha ao obter conexão ativa via ScriptFactory", ex);
                return null;
            }
        }

        private static ActiveConnection GetViaScriptFactory()
        {
            Type serviceCacheType = _serviceCacheType
                ?? FindLoadedType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
            if (serviceCacheType == null)
            {
                LogFailureOnce("Tipo ServiceCache não encontrado nos assemblies carregados", null);
                return null;
            }
            _serviceCacheType = serviceCacheType;

            PropertyInfo scriptFactoryProperty = _scriptFactoryProperty
                ?? serviceCacheType.GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static);
            if (scriptFactoryProperty != null)
                _scriptFactoryProperty = scriptFactoryProperty;

            object scriptFactory = scriptFactoryProperty?.GetValue(null);
            object connectionWrapper = GetProperty(scriptFactory, "CurrentlyActiveWndConnectionInfo");
            object uiConnectionInfo = GetProperty(connectionWrapper, "UIConnectionInfo");
            if (uiConnectionInfo == null)
                return null; // janela sem conexão: situação normal, sem log

            var server = GetProperty(uiConnectionInfo, "ServerName") as string;
            if (string.IsNullOrEmpty(server))
                return null;

            string database = GetAdvancedOption(uiConnectionInfo, "DATABASE");
            if (string.IsNullOrEmpty(database))
                database = "master";

            var userName = GetProperty(uiConnectionInfo, "UserName") as string;
            var password = GetProperty(uiConnectionInfo, "Password") as string;
            string authenticationType = Convert.ToString(GetProperty(uiConnectionInfo, "AuthenticationType"));

            if (LooksLikeEntra(userName, password, authenticationType))
            {
                LiveConnectionInfo live = GetLiveConnectionInfo();
                if (live == null)
                {
                    // Conexão viva indisponível (timing/foco em janela restaurada/nova): reaproveita
                    // a última conexão Entra boa do mesmo servidor em vez de matar as sugestões.
                    ActiveConnection reused = ReuseLastGoodEntra(server, database);
                    if (reused != null) return reused;
                    LogEntraWithoutTokenOnce();
                    return null; // degrada em silêncio: melhor sem sugestões que spam de erro 40607
                }

                // A conexão viva é a fonte da verdade: AdvancedOptions["DATABASE"] pode estar
                // dessincronizado da janela (ou ausente → "master"), enquanto live.Database
                // reflete o banco efetivo da sessão.
                if (!string.IsNullOrEmpty(live.Database))
                    database = live.Database;
                if (!string.IsNullOrEmpty(live.DataSource))
                    server = live.DataSource;

                if (!string.IsNullOrEmpty(live.AccessToken))
                {
                    // Modo (b): token Entra disponível na conexão viva — usa System.Data.SqlClient com AccessToken.
                    var tokenBuilder = new SqlConnectionStringBuilder
                    {
                        DataSource = server,
                        InitialCatalog = database,
                        ApplicationName = "SQL Beaver",
                        ConnectTimeout = 10,
                        Encrypt = true, // Azure exige TLS
                        // sem Integrated Security / User ID / Password: incompatíveis com AccessToken
                    };

                    if (!_loggedEntraResolved)
                    {
                        _loggedEntraResolved = true;
                        Log.Info($"Conexão Entra resolvida: servidor={server}, database={database} (via conexão viva: db={live.Database ?? "-"}).");
                    }

                    StoreGoodEntra(server, live.AccessToken, null, null);
                    return new ActiveConnection
                    {
                        Server = server,
                        Database = database,
                        ConnectionString = tokenBuilder.ConnectionString,
                        AccessToken = live.AccessToken,
                    };
                }

                if (!string.IsNullOrEmpty(live.ConnectionString))
                {
                    // Modo (c): sem token mas com ConnectionString legível — clona o provider da
                    // conexão viva (Microsoft.Data.SqlClient) para que o MSAL do processo autentique.
                    var csBuilder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = live.ConnectionString };
                    csBuilder["Initial Catalog"] = database;
                    csBuilder["Application Name"] = "SQL Beaver";

                    if (!_loggedEntraResolved)
                    {
                        _loggedEntraResolved = true;
                        Log.Info($"Conexão Entra resolvida via provider clonado: servidor={server}, database={database}, tipo={live.ConnectionType?.FullName ?? "-"}.");
                    }

                    StoreGoodEntra(server, null, live.ConnectionString, live.ConnectionType);
                    return new ActiveConnection
                    {
                        Server = server,
                        Database = database,
                        ConnectionString = csBuilder.ConnectionString,
                        ProviderConnectionType = live.ConnectionType,
                    };
                }

                ActiveConnection reusedNoToken = ReuseLastGoodEntra(server, database);
                if (reusedNoToken != null) return reusedNoToken;
                LogEntraWithoutTokenOnce();
                return null;
            }

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                ApplicationName = "SQL Beaver",
                ConnectTimeout = 10,
            };

            if (UseIntegratedSecurity(userName, password, authenticationType))
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = userName ?? string.Empty;
                builder.Password = password ?? string.Empty;
            }

            if (IsTrue(GetAdvancedOption(uiConnectionInfo, "TRUST_SERVER_CERTIFICATE")))
                builder.TrustServerCertificate = true;
            if (IsTrue(GetAdvancedOption(uiConnectionInfo, "ENCRYPT_CONNECTION")))
                builder.Encrypt = true;

            return new ActiveConnection
            {
                Server = server,
                Database = database,
                ConnectionString = builder.ConnectionString,
            };
        }

        // Heurística do OpenHint-SQL: o SSMS costuma deixar a conta Windows em UserName
        // com Password vazio — tratar como Windows Auth para não tentar SQL Auth com login de domínio.
        private static bool UseIntegratedSecurity(string userName, string password, string authenticationType)
        {
            if (!string.IsNullOrEmpty(authenticationType))
            {
                if (authenticationType.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    authenticationType.IndexOf("integrated", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (authenticationType.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            if (string.IsNullOrEmpty(password))
                return true;

            return string.IsNullOrEmpty(userName);
        }

        private static string GetAdvancedOption(object uiConnectionInfo, string key)
        {
            object advancedOptions = GetProperty(uiConnectionInfo, "AdvancedOptions");
            if (advancedOptions == null)
                return null;

            PropertyInfo indexer = advancedOptions.GetType().GetProperty("Item", new[] { typeof(string) });
            if (indexer == null)
                return null;

            try
            {
                return indexer.GetValue(advancedOptions, new object[] { key }) as string;
            }
            catch
            {
                return null;
            }
        }

        private static object GetProperty(object instance, string propertyName)
            => instance?.GetType().GetProperty(propertyName)?.GetValue(instance);

        private static object GetPropertySafe(object instance, string propertyName)
        {
            try { return GetProperty(instance, propertyName); }
            catch { return null; }
        }

        private static Type FindLoadedType(string fullTypeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullTypeName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // assemblies dinâmicos podem lançar ao acessar metadata
                }
            }
            return null;
        }

        private static bool IsTrue(string value)
            => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               value == "1";

        private static void LogFailureOnce(string message, Exception ex)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            Log.Error(message + " (a extensão seguirá sem sugestões)", ex);
        }

        private sealed class LiveConnectionInfo
        {
            public string AccessToken;
            public string Database;
            public string DataSource;
            public string ConnectionString;
            public Type ConnectionType;
        }

        private static bool _loggedNoService;
        private static bool _loggedNoConnection;

        private static LiveConnectionInfo GetLiveConnectionInfo()
        {
            try
            {
                Type interfaceType = FindLoadedType("Microsoft.SqlServer.Management.UI.VSIntegration.ISqlEditorService");
                Type serviceType = FindLoadedType("Microsoft.SqlServer.Management.UI.VSIntegration.SSqlEditorService") ?? interfaceType;
                if (interfaceType == null)
                    return null;

                object service =
                    TryGetGlobalService(serviceType) ??
                    TryGetGlobalService(interfaceType);
                if (service == null)
                {
                    if (!_loggedNoService)
                    {
                        _loggedNoService = true;
                        Log.Info("ISqlEditorService não resolvido — sem conexão viva disponível.");
                    }
                    return null;
                }

                MethodInfo getCurrentConnection =
                    interfaceType.GetMethod("GetCurrentConnection") ??
                    service.GetType().GetMethod("GetCurrentConnection");
                object liveConnection = getCurrentConnection?.Invoke(service, null);
                if (liveConnection == null)
                {
                    if (!_loggedNoConnection)
                    {
                        _loggedNoConnection = true;
                        Log.Info("GetCurrentConnection retornou null — nenhuma janela de query ativa com conexão viva.");
                    }
                    return null;
                }

                var info = new LiveConnectionInfo
                {
                    AccessToken      = GetPropertySafe(liveConnection, "AccessToken")      as string,
                    Database         = GetPropertySafe(liveConnection, "Database")         as string,
                    DataSource       = GetPropertySafe(liveConnection, "DataSource")       as string,
                    ConnectionString = GetPropertySafe(liveConnection, "ConnectionString") as string,
                    ConnectionType   = liveConnection.GetType(),
                };

                if (string.IsNullOrEmpty(info.AccessToken) && !_loggedLiveConnectionShape)
                {
                    _loggedLiveConnectionShape = true;
                    Log.Info("Conexão viva sem AccessToken legível: tipo=" + liveConnection.GetType().FullName +
                             ", Database=" + (info.Database ?? "-") + ", DataSource=" + (info.DataSource ?? "-") +
                             ", ConnectionString legível=" + (!string.IsNullOrEmpty(info.ConnectionString) ? "sim" : "não") + ".");
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        private static bool _loggedEntraWithoutToken;
        private static bool _loggedEntraResolved;
        private static bool _loggedLiveConnectionShape;
        private static bool _loggedEntraReused;

        // Última conexão Entra resolvida com sucesso, para reaproveitar quando a conexão viva
        // (GetCurrentConnection) voltar null transitoriamente — janela restaurada/nova via
        // ScriptFactory perde o "current" por timing/foco, matando as sugestões sem motivo.
        private static string _lastGoodServer;
        private static string _lastGoodToken;        // modo (b): token Entra
        private static string _lastGoodConnString;   // modo (c): connection string do provider vivo
        private static Type   _lastGoodProviderType;

        private static void StoreGoodEntra(string server, string token, string connString, Type providerType)
        {
            _lastGoodServer       = server;
            _lastGoodToken        = token;
            _lastGoodConnString   = connString;
            _lastGoodProviderType = providerType;
        }

        // Reconstrói uma ActiveConnection a partir do último Entra bom, se o servidor bater.
        // O banco vem do parâmetro (UIConnectionInfo da janela atual — sempre legível).
        private static ActiveConnection ReuseLastGoodEntra(string server, string database)
        {
            if (string.IsNullOrEmpty(_lastGoodServer) ||
                !string.Equals(_lastGoodServer, server, StringComparison.OrdinalIgnoreCase))
                return null;

            if (!_loggedEntraReused)
            {
                _loggedEntraReused = true;
                Log.Info($"Conexão Entra reusada do cache (conexão viva indisponível): servidor={server}, database={database}.");
            }

            if (!string.IsNullOrEmpty(_lastGoodToken))
            {
                var b = new SqlConnectionStringBuilder
                {
                    DataSource = server,
                    InitialCatalog = database,
                    ApplicationName = "SQL Beaver",
                    ConnectTimeout = 10,
                    Encrypt = true,
                };
                return new ActiveConnection
                {
                    Server = server,
                    Database = database,
                    ConnectionString = b.ConnectionString,
                    AccessToken = _lastGoodToken,
                };
            }

            if (!string.IsNullOrEmpty(_lastGoodConnString))
            {
                var cs = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = _lastGoodConnString };
                cs["Initial Catalog"] = database;
                cs["Application Name"] = "SQL Beaver";
                return new ActiveConnection
                {
                    Server = server,
                    Database = database,
                    ConnectionString = cs.ConnectionString,
                    ProviderConnectionType = _lastGoodProviderType,
                };
            }

            return null;
        }

        // UPN sem senha (Entra MFA) ou dica explícita no tipo de autenticação.
        private static bool LooksLikeEntra(string userName, string password, string authenticationType)
        {
            if (!string.IsNullOrEmpty(authenticationType))
            {
                string[] hints = { "active directory", "activedirectory", "entra", "azure", "mfa", "universal", "interactive" };
                foreach (string hint in hints)
                {
                    if (authenticationType.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            return string.IsNullOrEmpty(password) &&
                   !string.IsNullOrEmpty(userName) &&
                   userName.IndexOf('@') >= 0 &&
                   userName.IndexOf('\\') < 0;
        }

        private static object TryGetGlobalService(Type serviceType)
        {
            if (serviceType == null)
                return null;
            try
            {
                return Package.GetGlobalService(serviceType)
                    ?? Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider.GetService(serviceType);
            }
            catch
            {
                return null;
            }
        }

        private static void LogEntraWithoutTokenOnce()
        {
            if (_loggedEntraWithoutToken) return;
            _loggedEntraWithoutToken = true;
            Log.Info("Conexão Microsoft Entra detectada, mas sem token e sem connection string legível na conexão viva — sem sugestões nesta janela.");
        }
    }
}
