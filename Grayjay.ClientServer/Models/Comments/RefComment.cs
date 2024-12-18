using Grayjay.Engine;
using Grayjay.Engine.Models.Comments;

namespace Grayjay.ClientServer.Models.Comments
{
    public class RefComment: PlatformComment
    {
        public string RefID { get; set; }

        public RefComment(GrayjayPlugin plugin, PlatformComment comment, string id): base(plugin)
        {
            RefID = id;
            ContextUrl = comment.ContextUrl;
            Author = comment.Author;
            Message = comment.Message;
            Rating = comment.Rating;
            Date = comment.Date;
            ReplyCount = comment.ReplyCount;
        }
    }
}
