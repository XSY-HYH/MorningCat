using System.Collections.Generic;
using OneBotLib;
using OneBotLib.MessageSegment;

namespace MorningCat.Commands
{
    public static class MessageHelper
    {
        public static string ReplyCQ(long messageId)
        {
            return $"[CQ:reply,id={messageId}]";
        }

        public static string AtCQ(long userId)
        {
            return $"[CQ:at,qq={userId}]";
        }

        public static string ReplyWithAtCQ(long messageId, long userId)
        {
            return $"[CQ:reply,id={messageId}][CQ:at,qq={userId}]";
        }

        public static List<MessageSegment> Reply(long messageId)
        {
            return new List<MessageSegment> { MessageSegment.Reply(messageId) };
        }

        public static List<MessageSegment> At(long userId)
        {
            return new List<MessageSegment> { MessageSegment.At(userId) };
        }

        public static List<MessageSegment> ReplyWithAt(long messageId, long userId)
        {
            return new List<MessageSegment>
            {
                MessageSegment.Reply(messageId),
                MessageSegment.At(userId)
            };
        }

        public static List<MessageSegment> BuildReplyMessage(long? messageId, long? userId, string text, bool reply = false, bool at = false)
        {
            var segments = new List<MessageSegment>();

            if (reply && messageId.HasValue)
            {
                segments.Add(MessageSegment.Reply(messageId.Value));
            }

            if (at && userId.HasValue)
            {
                segments.Add(MessageSegment.At(userId.Value));
            }

            if (!string.IsNullOrEmpty(text))
            {
                segments.Add(MessageSegment.Text(text));
            }

            return segments;
        }

        public static string BuildReplyMessageCQ(long? messageId, long? userId, string text, bool reply = false, bool at = false)
        {
            var result = "";

            if (reply && messageId.HasValue)
            {
                result += $"[CQ:reply,id={messageId.Value}]";
            }

            if (at && userId.HasValue)
            {
                result += $"[CQ:at,qq={userId.Value}]";
            }

            result += text;
            return result;
        }
    }
}
