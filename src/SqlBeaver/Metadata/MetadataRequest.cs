using System;

namespace SqlBeaver.Metadata
{
    /// <summary>Como abrir a conexão de metadata. Um dos três modos:
    /// (a) ConnectionString simples (System.Data.SqlClient — Windows/SQL auth);
    /// (b) ConnectionString + AccessToken (Entra com token);
    /// (c) ConnectionString + ProviderConnectionType (Entra MFA: clona o provider
    ///     da conexão viva do SSMS; o MSAL do processo autentica em silêncio).</summary>
    public sealed class MetadataRequest
    {
        public string ConnectionString { get; set; }
        public string AccessToken { get; set; }
        /// <summary>Tipo runtime da conexão viva (ex.: Microsoft.Data.SqlClient.SqlConnection).</summary>
        public Type ProviderConnectionType { get; set; }
    }
}
