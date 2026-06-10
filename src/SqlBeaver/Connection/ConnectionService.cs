using System;
using System.Data.SqlClient;
using System.Reflection;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Connection
{
    public sealed class ActiveConnection
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string ConnectionString { get; set; }
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
    }
}
