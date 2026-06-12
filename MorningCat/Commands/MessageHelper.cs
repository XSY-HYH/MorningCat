using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Logging;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat.Commands
{
    public static class MessageHelper
    {
        private static readonly Regex AtBotRegex = new Regex(
            @"\[CQ:at,qq=(\d+)\]\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AtAllRegex = new Regex(
            @"\[CQ:at,qq=all\]\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AtAnyRegex = new Regex(
            @"\[CQ:at,qq=\d+\]\s*", RegexOptions.Compiled);
        private static readonly Regex ReplyCQRegex = new Regex(
            @"\[CQ:reply,id=-?\d+\]\s*", RegexOptions.Compiled);

        public static string CleanMessageText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string cleaned = text.Trim();

            if (cleaned.StartsWith("\"") && cleaned.EndsWith("\"") && cleaned.Length > 1)
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2);
            }

            cleaned = ReplyCQRegex.Replace(cleaned, "").Trim();

            return cleaned;
        }

        public static string RemoveAtSegments(string text, string selfId)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var atBotPattern = $@"\[CQ:at,qq={selfId}\]\s*";
            var cleaned = Regex.Replace(text, atBotPattern, "", RegexOptions.IgnoreCase).Trim();
            cleaned = AtAllRegex.Replace(cleaned, "").Trim();
            cleaned = AtAnyRegex.Replace(cleaned, "").Trim();

            return cleaned;
        }

        /// <summary>
        /// 使用IMessageBuilder构建回复+AT消息体
        /// </summary>
        public static MessageBody BuildReplyMessage(string? messageId, string? userId, string text, bool reply = false, bool at = false)
        {
            // 不直接创建builder，因为需要知道平台。这里用SimpleMessageBuilder构建通用消息体
            var builder = new SimpleMessageBuilder();

            if (reply && messageId != null)
            {
                builder.Reply(messageId);
            }

            if (at && userId != null)
            {
                builder.At(userId);
            }

            if (!string.IsNullOrEmpty(text))
            {
                builder.Text(text);
            }

            return builder.Build();
        }

        #region 兼容旧CQ码API（逐步废弃）

        public static string ReplyCQ(string messageId)
        {
            return $"[CQ:reply,id={messageId}]";
        }

        public static string AtCQ(string userId)
        {
            return $"[CQ:at,qq={userId}]";
        }

        public static string ReplyWithAtCQ(string messageId, string userId)
        {
            return $"[CQ:reply,id={messageId}][CQ:at,qq={userId}]";
        }

        public static string BuildReplyMessageCQ(string? messageId, string? userId, string text, bool reply = false, bool at = false)
        {
            var result = "";

            if (reply && messageId != null)
            {
                result += $"[CQ:reply,id={messageId}]";
            }

            if (at && userId != null)
            {
                result += $"[CQ:at,qq={userId}]";
            }

            result += text;
            return result;
        }

        #endregion

        /// <summary>
        /// 使用MDC发送消息（便捷方法）
        /// </summary>
        public static async Task SendMessageAsync(MessageDistributionCore mdc, PlatformMessage message, string text)
        {
            try
            {
                await mdc.SendMessageAsync(message, text);
            }
            catch (System.Exception ex)
            {
                Log.Error($"发送消息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 使用MDC+IMessageBuilder发送消息（推荐）
        /// </summary>
        public static async Task SendAsync(MessageDistributionCore mdc, PlatformMessage message, Action<IMessageBuilder> configure)
        {
            try
            {
                await mdc.SendAsync(message, configure);
            }
            catch (System.Exception ex)
            {
                Log.Error($"发送消息失败: {ex.Message}");
            }
        }
    }
}
