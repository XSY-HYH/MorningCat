using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Logging;
using MCProtocol.Game;
using ModuleManagerLib;
using MorningCat.Commands;
using OneBotLib;
using OneBotLib.MessageSegment;
using OneBotLib.Models;
using SkiaSharp;

namespace MorningCat.Modules
{
    public class MCServerQueryModule : ModuleBase
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
                "MCServerQueryModule",
                "MC服务器查询",
                "MorningCat",
                "",
                "查询Minecraft服务器状态并生成卡片图片",
                iconBase64
            );

            RegisterCommands();
            Log.Info("MCServerQueryModule 初始化完成");
            await Task.CompletedTask;
        }

        private string? LoadIconAsBase64()
        {
            try
            {
                var assembly = typeof(MCServerQueryModule).Assembly;
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
            _commandRegistry?.UnregisterModuleCommands("MCServerQueryModule");
            Log.Info("MCServerQueryModule 已卸载");
            await Task.CompletedTask;
        }

        private void RegisterCommands()
        {
            var subParams = new List<CommandParameter>
            {
                new CommandParameter
                {
                    Name = "查询",
                    Description = "查询子命令",
                    IsRequired = true,
                    Type = ParameterType.String,
                    SubParameters = new List<CommandParameter>
                    {
                        new CommandParameter
                        {
                            Name = "server",
                            Description = "服务器IP地址",
                            IsRequired = true,
                            Type = ParameterType.String
                        }
                    }
                }
            };

            var success = _commandRegistry.RegisterCommand(
                "mc",
                "MC服务器查询",
                "@机器人 mc 查询 <服务器IP> 查询Minecraft服务器状态",
                subParams,
                HandleMCCommand,
                "MCServerQueryModule",
                CommandPermission.Everyone,
                CommandScope.All,
                requireAt: true,
                requireSlash: false
            );

            if (success)
            {
                Log.Info("mc命令注册成功");
            }
        }

        private async Task HandleMCCommand(CommandContext context)
        {
            var message = context.Message;
            var parameters = context.Parameters;

            if (!parameters.TryGetValue("查询", out var subCommand) || subCommand != "查询")
            {
                var segments = new List<MessageSegment>
                {
                    MessageSegment.Text("用法: mc 查询 <服务器IP>")
                };
                await SendMessageWithSegmentsAsync(message, segments);
                return;
            }

            if (!parameters.TryGetValue("server", out var serverIp) || string.IsNullOrEmpty(serverIp))
            {
                var segments = new List<MessageSegment>
                {
                    MessageSegment.Text("请提供服务器IP地址")
                };
                await SendMessageWithSegmentsAsync(message, segments);
                return;
            }

            try
            {
                var serverInfo = await QueryServerAsync(serverIp);
                if (serverInfo != null)
                {
                    var imagePath = await GenerateServerCardAsync(serverInfo);
                    if (imagePath != null)
                    {
                        var segments = new List<MessageSegment>
                        {
                            MessageSegment.Reply(message.MessageId),
                            MessageSegment.Image($"file:///{imagePath}")
                        };
                        await SendMessageWithSegmentsAsync(message, segments);
                    }
                    else
                    {
                        var segments = new List<MessageSegment>
                        {
                            MessageSegment.Reply(message.MessageId),
                            MessageSegment.Text("生成卡片失败")
                        };
                        await SendMessageWithSegmentsAsync(message, segments);
                    }
                }
                else
                {
                    var segments = new List<MessageSegment>
                    {
                        MessageSegment.Reply(message.MessageId),
                        MessageSegment.Text($"无法连接到服务器 {serverIp}")
                    };
                    await SendMessageWithSegmentsAsync(message, segments);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"查询MC服务器失败: {ex.Message}");
                var segments = new List<MessageSegment>
                {
                    MessageSegment.Reply(message.MessageId),
                    MessageSegment.Text($"查询失败: {ex.Message}")
                };
                await SendMessageWithSegmentsAsync(message, segments);
            }
        }

        private async Task<MCServerInfo?> QueryServerAsync(string address)
        {
            var parts = address.Split(':');
            var host = parts[0];
            var port = parts.Length > 1 ? int.Parse(parts[1]) : 25565;

            Log.Debug($"开始查询MC服务器: {host}:{port}");

            try
            {
                using var client = new MinecraftClient();
                var serverInfo = await client.QueryServerAsync(host, port);

                if (serverInfo != null)
                {
                    return new MCServerInfo
                    {
                        Host = host,
                        Port = port,
                        Description = serverInfo.Description ?? "",
                        OnlinePlayers = serverInfo.Players.Online,
                        MaxPlayers = serverInfo.Players.Max,
                        Version = serverInfo.Version.Name,
                        Favicon = serverInfo.Favicon
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"连接服务器失败: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> GenerateServerCardAsync(MCServerInfo info)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var width = 650;
                    var height = 350;

                    using var surface = SKSurface.Create(new SKImageInfo(width, height));
                    var canvas = surface.Canvas;

                    canvas.Clear(new SKColor(25, 25, 35));

                    using var paint = new SKPaint
                    {
                        Color = SKColors.White,
                        IsAntialias = true
                    };

                    var typeface = SKTypeface.FromFamilyName("SimHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                    if (typeface == null)
                    {
                        typeface = SKTypeface.CreateDefault();
                    }

                    using var titleFont = new SKFont(typeface, 32);
                    using var infoFont = new SKFont(typeface, 22);
                    using var smallFont = new SKFont(typeface, 18);

                    using var bgPaint = new SKPaint
                    {
                        Color = new SKColor(40, 40, 55),
                        Style = SKPaintStyle.Fill
                    };
                    var rect = new SKRoundRect(new SKRect(15, 15, width - 15, height - 15), 20);
                    canvas.DrawRoundRect(rect, bgPaint);

                    SKBitmap? serverIcon = null;
                    int iconSize = 64;
                    int iconX = width - 15 - iconSize - 20;
                    int iconY = 30;

                    if (!string.IsNullOrEmpty(info.Favicon))
                    {
                        serverIcon = LoadServerIcon(info.Favicon);
                        if (serverIcon != null)
                        {
                            using var iconPaint = new SKPaint
                            {
                                FilterQuality = SKFilterQuality.High,
                                IsAntialias = true
                            };
                            canvas.DrawBitmap(serverIcon, new SKRect(iconX, iconY, iconX + iconSize, iconY + iconSize), iconPaint);
                        }
                    }

                    using var statusPaint = new SKPaint
                    {
                        Color = new SKColor(76, 175, 80),
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawCircle(45, 55, 10, statusPaint);

                    paint.Color = SKColors.White;
                    canvas.DrawText("Minecraft Server", 70, 62, titleFont, paint);

                    paint.Color = new SKColor(160, 160, 170);
                    canvas.DrawText($"{info.Host}:{info.Port}", 35, 115, infoFont, paint);

                    paint.Color = SKColors.White;
                    canvas.DrawText($"版本: {info.Version}", 35, 160, infoFont, paint);

                    paint.Color = new SKColor(100, 200, 255);
                    canvas.DrawText($"在线玩家: {info.OnlinePlayers} / {info.MaxPlayers}", 35, 205, infoFont, paint);

                    paint.Color = new SKColor(200, 200, 210);
                    var desc = info.Description ?? "无描述";
                    if (desc.Length > 40) desc = desc.Substring(0, 40) + "...";
                    canvas.DrawText($"描述: {desc}", 35, 250, smallFont, paint);

                    paint.Color = new SKColor(120, 120, 140);
                    canvas.DrawText($"查询时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", 35, 320, smallFont, paint);

                    using var borderPaint = new SKPaint
                    {
                        Color = new SKColor(70, 70, 90),
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = 3
                    };
                    canvas.DrawRoundRect(rect, borderPaint);

                    serverIcon?.Dispose();

                    using var image = surface.Snapshot();
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);

                    var fileName = $"mc_server_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    var tempPath = Path.Combine(Path.GetTempPath(), fileName);
                    using var fileStream = File.OpenWrite(tempPath);
                    data.SaveTo(fileStream);

                    return tempPath;
                }
                catch (Exception ex)
                {
                    Log.Error($"生成卡片失败: {ex.Message}");
                    return null;
                }
            });
        }

        private SKBitmap? LoadServerIcon(string favicon)
        {
            try
            {
                if (string.IsNullOrEmpty(favicon))
                    return null;

                var base64Data = favicon;
                if (favicon.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIndex = favicon.IndexOf(',');
                    if (commaIndex >= 0)
                    {
                        base64Data = favicon.Substring(commaIndex + 1);
                    }
                }

                var imageBytes = Convert.FromBase64String(base64Data);
                using var stream = new MemoryStream(imageBytes);
                return SKBitmap.Decode(stream);
            }
            catch (Exception ex)
            {
                Log.Debug($"加载服务器图标失败: {ex.Message}");
                return null;
            }
        }

        private Task SendMessageWithSegmentsAsync(MessageObject message, List<MessageSegment> segments)
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
            return Task.CompletedTask;
        }
    }

    public class MCServerInfo
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Description { get; set; } = "";
        public int OnlinePlayers { get; set; }
        public int MaxPlayers { get; set; }
        public string Version { get; set; } = "";
        public string? Favicon { get; set; }
    }
}
