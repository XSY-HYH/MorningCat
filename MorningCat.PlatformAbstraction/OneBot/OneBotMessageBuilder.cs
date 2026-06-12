using System.Collections.Generic;

namespace MorningCat.PlatformAbstraction
{
    /// <summary>
    /// OneBot消息构造器 - 实现标准API + OneBot特殊API
    /// </summary>
    public class OneBotMessageBuilder : MessageBuilderBase, IOneBotMessageBuilder
    {
        public IOneBotMessageBuilder Face(int faceId)
        {
            _segments.Add(new MessageSegment { Type = "face", Data = { ["id"] = faceId.ToString() } });
            return this;
        }

        public IOneBotMessageBuilder Poke(string userId)
        {
            _segments.Add(new MessageSegment { Type = "poke", Data = { ["qq"] = userId } });
            return this;
        }

        public IOneBotMessageBuilder ForwardNode(string userId, string nickname, string content)
        {
            _segments.Add(new MessageSegment
            {
                Type = "node",
                Data = { ["user_id"] = userId, ["nickname"] = nickname, ["content"] = content }
            });
            return this;
        }

        /// <summary>
        /// 将消息体转换为OneBot CQ码字符串
        /// </summary>
        public static string ToCQCode(MessageBody body)
        {
            var parts = new List<string>();
            foreach (var seg in body.Segments)
            {
                switch (seg.Type)
                {
                    case "text":
                        var text = seg.Data.TryGetValue("text", out var t) ? t?.ToString() ?? "" : "";
                        parts.Add(text);
                        break;
                    case "at":
                        var userId = seg.Data.TryGetValue("user_id", out var uid) ? uid?.ToString() ?? "" : "";
                        if (userId == "all")
                            parts.Add("[CQ:at,qq=all]");
                        else
                            parts.Add($"[CQ:at,qq={userId}]");
                        break;
                    case "reply":
                        var msgId = seg.Data.TryGetValue("message_id", out var mid) ? mid?.ToString() ?? "" : "";
                        parts.Add($"[CQ:reply,id={msgId}]");
                        break;
                    case "image":
                        var url = seg.Data.TryGetValue("url", out var u) ? u?.ToString() ?? "" : "";
                        parts.Add($"[CQ:image,file={url}]");
                        break;
                    case "face":
                        var faceId = seg.Data.TryGetValue("id", out var fid) ? fid?.ToString() ?? "0" : "0";
                        parts.Add($"[CQ:face,id={faceId}]");
                        break;
                    case "poke":
                        var pokeId = seg.Data.TryGetValue("qq", out var pid) ? pid?.ToString() ?? "" : "";
                        parts.Add($"[CQ:poke,qq={pokeId}]");
                        break;
                    default:
                        if (seg.Data.Count > 0)
                        {
                            var dataStr = string.Join(",", seg.Data.Select(kv => $"{kv.Key}={kv.Value}"));
                            parts.Add($"[CQ:{seg.Type},{dataStr}]");
                        }
                        else
                        {
                            parts.Add($"[CQ:{seg.Type}]");
                        }
                        break;
                }
            }
            return string.Join("", parts);
        }
    }
}
