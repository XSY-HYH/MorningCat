using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using OneBotLib;
using OneBotLib.MessageSegment;
using OneBotLib.Models;

namespace MorningCat.Modules
{
    public class ApiTestModule : ModuleBase
    {
        private OneBotClient _client = null!;
        private CommandRegistry _commandRegistry = null!;

        private Action<string, string, string, string, string, string> _setMetadata = null!;
        public Action<string, string, string, string, string, string> SetMetadataCallback
        {
            set => _setMetadata = value;
        }

        public OneBotClient Client
        {
            get => _client;
            set => _client = value;
        }

        public CommandRegistry CommandRegistry
        {
            get => _commandRegistry;
            set => _commandRegistry = value;
        }

        public override async Task Init()
        {
            var iconBase64 = LoadIconAsBase64();
            
            _setMetadata?.Invoke(
                "ApiTestModule",
                "API测试模块",
                "MorningCat",
                "",
                "测试OneBotLib各项API功能",
                iconBase64
            );

            RegisterCommands();
            Log.Info("ApiTestModule 初始化完成");
            await Task.CompletedTask;
        }

        private string? LoadIconAsBase64()
        {
            try
            {
                var assembly = typeof(ApiTestModule).Assembly;
                using var stream = assembly.GetManifestResourceStream("icon.png");
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var bytes = ms.ToArray();
                    Log.Debug($"从嵌入资源加载图标，大小: {bytes.Length} 字节");
                    return Convert.ToBase64String(bytes);
                }
                Log.Debug("未找到嵌入资源图标");
            }
            catch (Exception ex)
            {
                Log.Debug($"加载图标失败: {ex.Message}");
            }
            return null;
        }

        public override IEnumerable<string> GetDependencies()
        {
            return Array.Empty<string>();
        }

        public override async Task Exit()
        {
            _commandRegistry?.UnregisterModuleCommands("ApiTestModule");
            Log.Info("ApiTestModule 已卸载");
            await Task.CompletedTask;
        }

        private void RegisterCommands()
        {
            var subParams = new List<CommandParameter>
            {
                new CommandParameter
                {
                    Name = "功能",
                    Description = "测试功能编号: 1=消息表情回应, 2=发QQ动态(不支持), 3-0=预留",
                    IsRequired = true,
                    Type = ParameterType.String,
                    DefaultValue = "1"
                }
            };

            _commandRegistry?.RegisterCommand(
                "test",
                "API测试命令",
                "test <功能编号>\n" +
                "功能编号:\n" +
                "  1 - 消息表情回应\n" +
                "  2 - 发QQ动态(OneBot11不支持)\n" +
                "  3-0 - 预留功能",
                subParams,
                HandleTestCommand,
                "ApiTestModule"
            );
        }

        private async Task HandleTestCommand(CommandContext context)
        {
            var message = context.Message;
            var parameters = context.Parameters;
            
            if (!parameters.TryGetValue("功能", out var funcParam) || string.IsNullOrEmpty(funcParam))
            {
                await SendMessageAsync(message, "请指定功能编号，例如: test 1");
                return;
            }

            var funcNum = funcParam.Trim();
            
            try
            {
                switch (funcNum)
                {
                    case "1":
                        await TestReplyMessage(message);
                        break;
                    case "2":
                        await TestSendFeed(message);
                        break;
                    case "3":
                    case "4":
                    case "5":
                    case "6":
                    case "7":
                    case "8":
                    case "9":
                    case "0":
                        await SendMessageAsync(message, $"功能 {funcNum} 暂未实现");
                        break;
                    default:
                        await SendMessageAsync(message, $"未知功能编号: {funcNum}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"执行测试命令失败: {ex.Message}");
                await SendMessageAsync(message, $"执行失败: {ex.Message}");
            }
        }

        private async Task TestReplyMessage(MessageObject message)
        {
            var result = await _client.SetMsgEmojiLikeAsync(message.MessageId, "66");
            
            if (result.Success)
            {
                Log.Info($"[Test] 消息回应测试完成，消息ID: {message.MessageId}");
            }
            else
            {
                Log.Error($"[Test] 消息回应失败: {result.ErrorMessage}");
                await SendMessageAsync(message, $"消息回应失败: {result.ErrorMessage}");
            }
        }

        private async Task TestSendFeed(MessageObject message)
        {
            await SendMessageAsync(message, "OneBot 11 协议不支持发送 QQ 动态功能。\n该功能需要使用 QQ 客户端特有的非公开 API。");
            Log.Info("[Test] 发送动态功能不支持");
        }

        private async Task SendMessageAsync(MessageObject message, string text)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (message.MessageType == "private")
                    {
                        await _client.SendPrivateMsgAsync(message.UserId ?? 0, text);
                    }
                    else if (message.MessageType == "group")
                    {
                        await _client.SendGroupMsgAsync(message.GroupId ?? 0, text);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"发送消息失败: {ex.Message}");
                }
            });
            await Task.CompletedTask;
        }

        private async Task SendMessageWithSegmentsAsync(MessageObject message, List<MessageSegment> segments)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (message.MessageType == "private")
                    {
                        await _client.SendPrivateMsgAsync(message.UserId ?? 0, segments);
                    }
                    else if (message.MessageType == "group")
                    {
                        await _client.SendGroupMsgAsync(message.GroupId ?? 0, segments);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"发送消息失败: {ex.Message}");
                }
            });
            await Task.CompletedTask;
        }
    }
}
