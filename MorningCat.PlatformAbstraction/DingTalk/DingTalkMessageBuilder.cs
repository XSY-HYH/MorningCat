using System.Collections.Generic;

namespace MorningCat.PlatformAbstraction
{
    /// <summary>
    /// 钉钉消息构造器 - 实现标准API + 钉钉特殊API
    /// </summary>
    public class DingTalkMessageBuilder : MessageBuilderBase, IDingTalkMessageBuilder
    {
        private string? _markdownTitle;
        private string? _markdownText;
        private string? _oaTitle;
        private string? _oaContent;

        public IDingTalkMessageBuilder Markdown(string title, string markdownText)
        {
            _markdownTitle = title;
            _markdownText = markdownText;
            return this;
        }

        public IDingTalkMessageBuilder OaMessage(string title, string content)
        {
            _oaTitle = title;
            _oaContent = content;
            return this;
        }

        public override MessageBody Build()
        {
            var body = base.Build();

            if (_markdownTitle != null)
            {
                body.Segments.Add(new MessageSegment
                {
                    Type = "dingtalk_markdown",
                    Data = { ["title"] = _markdownTitle, ["text"] = _markdownText ?? "" }
                });
            }

            if (_oaTitle != null)
            {
                body.Segments.Add(new MessageSegment
                {
                    Type = "dingtalk_oa",
                    Data = { ["title"] = _oaTitle, ["content"] = _oaContent ?? "" }
                });
            }

            return body;
        }

        public override IMessageBuilder Clear()
        {
            _markdownTitle = null;
            _markdownText = null;
            _oaTitle = null;
            _oaContent = null;
            return base.Clear();
        }
    }
}
