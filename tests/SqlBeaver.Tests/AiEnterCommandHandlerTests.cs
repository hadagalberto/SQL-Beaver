using SqlBeaver.Editing;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AiEnterCommandHandlerTests
    {
        [Fact]
        public void RealComment_WithInstruction_IsTrigger()
        {
            Assert.True(CommentTriggerDetector.IsTriggerCommentLine("-- listar pedidos do cliente"));
            Assert.True(CommentTriggerDetector.IsTriggerCommentLine("   -- top 10 clientes"));
        }

        [Fact]
        public void EmptyOrBareLeader_IsNotTrigger()
        {
            Assert.False(CommentTriggerDetector.IsTriggerCommentLine("-- "));
            Assert.False(CommentTriggerDetector.IsTriggerCommentLine("--"));
        }

        [Fact]
        public void TooShort_IsNotTrigger()
        {
            // "--ab" → só 2 chars de instrução (< 8).
            Assert.False(CommentTriggerDetector.IsTriggerCommentLine("--ab"));
            Assert.False(CommentTriggerDetector.IsTriggerCommentLine("-- curto"));
        }

        [Fact]
        public void NonComment_IsNotTrigger()
        {
            Assert.False(CommentTriggerDetector.IsTriggerCommentLine("SELECT * FROM dbo.Pessoas"));
            Assert.False(CommentTriggerDetector.IsTriggerCommentLine("/* bloco de comentario longo */"));
            Assert.False(CommentTriggerDetector.IsTriggerCommentLine(null));
            Assert.False(CommentTriggerDetector.IsTriggerCommentLine(""));
        }
    }
}
