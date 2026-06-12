using SqlBeaver.Ai;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AiSecretProtectorTests
    {
        [Fact]
        public void Roundtrip_PreservesValue()
        {
            const string secret = "sk-ant-EXEMPLO-1234567890";
            string protectedB64 = AiSecretProtector.Protect(secret);

            Assert.NotNull(protectedB64);
            Assert.NotEqual(secret, protectedB64); // realmente criptografado
            Assert.Equal(secret, AiSecretProtector.Unprotect(protectedB64));
        }

        [Fact]
        public void Protect_NullOrEmpty_ReturnsNull()
        {
            Assert.Null(AiSecretProtector.Protect(null));
            Assert.Null(AiSecretProtector.Protect(""));
        }

        [Fact]
        public void Unprotect_InvalidBase64_ReturnsNullWithoutThrowing()
        {
            Assert.Null(AiSecretProtector.Unprotect("!!notbase64!!"));
            Assert.Null(AiSecretProtector.Unprotect(null));
            Assert.Null(AiSecretProtector.Unprotect(""));
        }
    }
}
