using System;
using System.Security.Cryptography;
using System.Text;

namespace SqlBeaver.Ai
{
    /// <summary>
    /// Protege a chave de API com DPAPI (escopo CurrentUser). Apenas o usuário Windows
    /// atual consegue descriptografar. Nunca lança — em erro, retorna null.
    /// </summary>
    public static class AiSecretProtector
    {
        /// <summary>Texto puro → base64 do blob DPAPI; nulo/vazio → null.</summary>
        public static string Protect(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return null;
            try
            {
                byte[] blob = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(blob);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Base64 do blob DPAPI → texto puro; nulo/vazio/inválido → null (nunca lança).</summary>
        public static string Unprotect(string protectedB64)
        {
            if (string.IsNullOrEmpty(protectedB64))
                return null;
            try
            {
                byte[] blob = Convert.FromBase64String(protectedB64);
                byte[] plain = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (FormatException) { return null; }
            catch (CryptographicException) { return null; }
            catch { return null; }
        }
    }
}
