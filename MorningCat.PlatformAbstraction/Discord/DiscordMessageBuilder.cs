using System.Collections.Generic;

namespace MorningCat.PlatformAbstraction
{
    /// <summary>
    /// Discord消息构造器 - 实现标准API + Discord特殊API
    /// </summary>
    public class DiscordMessageBuilder : MessageBuilderBase, IDiscordMessageBuilder
    {
        private readonly List<DiscordEmbed> _embeds = new();
        private readonly List<DiscordButton> _buttons = new();

        public IDiscordMessageBuilder Embed(string title, string description, int color = 0)
        {
            _embeds.Add(new DiscordEmbed { Title = title, Description = description, Color = color });
            return this;
        }

        public IDiscordMessageBuilder Button(string label, string customId, string style = "Primary")
        {
            _buttons.Add(new DiscordButton { Label = label, CustomId = customId, Style = style });
            return this;
        }

        public override MessageBody Build()
        {
            var body = base.Build();

            // 将Discord特殊数据附加到消息段的额外信息中
            foreach (var embed in _embeds)
            {
                body.Segments.Add(new MessageSegment
                {
                    Type = "discord_embed",
                    Data = { ["title"] = embed.Title, ["description"] = embed.Description, ["color"] = embed.Color }
                });
            }

            foreach (var button in _buttons)
            {
                body.Segments.Add(new MessageSegment
                {
                    Type = "discord_button",
                    Data = { ["label"] = button.Label, ["custom_id"] = button.CustomId, ["style"] = button.Style }
                });
            }

            return body;
        }

        public override IMessageBuilder Clear()
        {
            _embeds.Clear();
            _buttons.Clear();
            return base.Clear();
        }

        internal class DiscordEmbed
        {
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public int Color { get; set; }
        }

        internal class DiscordButton
        {
            public string Label { get; set; } = "";
            public string CustomId { get; set; } = "";
            public string Style { get; set; } = "Primary";
        }
    }
}
