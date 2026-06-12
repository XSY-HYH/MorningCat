using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.Config;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;
using OneBotLib;
using OneBotLib.MessageSegment;
using OneBotLib.Models;

namespace PetPet
{
    public class PetPetPlugin : ModuleBase
    {
        private CommandRegistry _commandRegistry = null!;
        private PluginConfigManager _configManager = null!;
        private MessageDistributionCore _mdc = null!;
        private Action<string, string, string, string, string, string> _setMetadata = null!;

        // 创建支持忽略SSL验证的HttpClientHandler
        private static readonly HttpClientHandler _httpHandler = new HttpClientHandler
        {
            // 忽略SSL证书验证错误（解决ERR_CERT_AUTHORITY_INVALID问题）
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            // 启用自动解压缩以提高性能
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        // 使用自定义Handler创建HttpClient
        private static readonly HttpClient _httpClient = new HttpClient(_httpHandler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private const string PetPetApiUrl = "https://www.petpetgenerator.com/api/generate";
        private const string AvatarUrlTemplate = "https://q1.qlogo.cn/g?b=qq&nk={0}&s=640";

        private readonly ConcurrentDictionary<long, int> _userUsageCount = new();
        private readonly ConcurrentDictionary<long, bool> _processingUsers = new();

        private PetPetConfig _config = new();

        public Action<string, string, string, string, string, string> SetMetadataCallback
        {
            set => _setMetadata = value;
        }

        public CommandRegistry CommandRegistry
        {
            get => _commandRegistry;
            set => _commandRegistry = value;
        }

        public PluginConfigManager ConfigManager
        {
            get => _configManager;
            set => _configManager = value;
        }

        public MessageDistributionCore MDC
        {
            get => _mdc;
            set => _mdc = value;
        }

        public override async Task Init()
        {
            await LoadConfigAsync();

            var iconBase64 = LoadIconAsBase64();
            _setMetadata?.Invoke(
                "PetPetPlugin",
                "摸摸头",
                "MorningCat",
                "1.0.0",
                "摸摸别人的头！使用摸命令+@目标生成摸头GIF",
                iconBase64 ?? ""
            );

            RegisterCommands();

            Log.Info("[摸摸头] 插件已加载");
        }

        private async Task LoadConfigAsync()
        {
            try
            {
                if (_configManager != null)
                {
                    _config = await _configManager.GetConfigAsync<PetPetConfig>("PetPet", "config", new PetPetConfig());
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[摸摸头] 加载配置失败，使用默认配置: {ex.Message}");
                _config = new PetPetConfig();
            }
        }

        private async Task SaveConfigAsync()
        {
            try
            {
                if (_configManager != null)
                {
                    await _configManager.SetConfigAsync("PetPet", "config", _config);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[摸摸头] 保存配置失败: {ex.Message}");
            }
        }

        private void RegisterCommands()
        {
            if (_commandRegistry == null)
            {
                Log.Warning("[摸摸头] CommandRegistry未注入，无法注册命令");
                return;
            }

            var targetParam = new CommandParameter
            {
                Name = "目标",
                Description = "被摸的人（@目标）",
                IsRequired = true,
                Type = ParameterType.At
            };

            var success = _commandRegistry.RegisterCommand(
                "摸",
                "摸摸头 - 生成摸头GIF",
                "摸 @ta",
                new List<CommandParameter> { targetParam },
                HandlePetCommand,
                "PetPetPlugin",
                CommandPermission.Everyone,
                CommandScope.All,
                requireAt: true,
                requireSlash: false
            );

            if (success)
            {
                Log.Info("[摸摸头] 命令注册成功");
            }
            else
            {
                Log.Warning("[摸摸头] 命令注册失败");
            }
        }

        private async Task HandlePetCommand(CommandContext context)
        {
            var message = context.Message;
            var userId = message.SenderId;

            if (string.IsNullOrEmpty(userId))
            {
                await _mdc.SendMessageAsync(message, "无法识别你的身份");
                return;
            }

            var userIdLong = long.TryParse(userId, out var uid) ? uid : 0L;

            if (!context.Parameters.TryGetValue("目标", out var targetStr) || !long.TryParse(targetStr, out var targetId))
            {
                await _mdc.SendMessageAsync(message, "请@你要摸的人");
                return;
            }

            if (targetId == userIdLong)
            {
                await _mdc.SendMessageAsync(message, "不能摸自己哦~");
                return;
            }

            var currentCount = _userUsageCount.GetValueOrDefault(userIdLong, 0);
            if (currentCount >= _config.DailyLimit)
            {
                await _mdc.SendMessageAsync(message, $"你今天的摸头次数已用完（{_config.DailyLimit}次/天）");
                return;
            }

            if (_processingUsers.ContainsKey(userIdLong))
            {
                await _mdc.SendMessageAsync(message, "你有一个摸头请求正在处理中，请稍等~");
                return;
            }

            _processingUsers[userIdLong] = true;
            try
            {
                await ProcessPetRequest(message, userIdLong, targetId);
            }
            finally
            {
                _processingUsers.TryRemove(userIdLong, out _);
            }
        }

        private async Task ProcessPetRequest(PlatformMessage message, long userId, long targetId)
        {
            try
            {
                var avatarBytes = await DownloadAvatarAsync(targetId);
                if (avatarBytes == null)
                {
                    await _mdc.SendMessageAsync(message, "获取头像失败，请稍后再试");
                    return;
                }

                var gifBytes = await GeneratePetGifAsync(avatarBytes);
                if (gifBytes == null)
                {
                    await _mdc.SendMessageAsync(message, "生成摸头GIF失败，请稍后再试");
                    return;
                }

                _userUsageCount.AddOrUpdate(userId, 1, (_, count) => count + 1);

                var base64Gif = Convert.ToBase64String(gifBytes);

                await _mdc.SendAsync(message, builder =>
                {
                    if (!string.IsNullOrEmpty(message.MessageId))
                        builder.Reply(message.MessageId);
                    builder.ImageBase64(base64Gif);
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[摸摸头] 处理请求失败: {ex.Message}");
                await _mdc.SendMessageAsync(message, "处理请求时出错了，请稍后再试");
            }
        }

        private async Task<byte[]?> DownloadAvatarAsync(long userId)
        {
            try
            {
                var avatarUrl = string.Format(AvatarUrlTemplate, userId);
                var response = await _httpClient.GetAsync(avatarUrl);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }
                Log.Warning($"[摸摸头] 下载头像失败: HTTP {(int)response.StatusCode}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"[摸摸头] 下载头像异常: {ex.Message}");
                return null;
            }
        }

        private async Task<byte[]?> GeneratePetGifAsync(byte[] avatarBytes)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                var imageContent = new ByteArrayContent(avatarBytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                content.Add(imageContent, "image", "avatar.png");
                content.Add(new StringContent(_config.Resolution.ToString()), "resolution");
                content.Add(new StringContent(_config.Delay.ToString()), "delay");
                content.Add(new StringContent(_config.BackgroundColor), "backgroundColor");

                var response = await _httpClient.PostAsync(PetPetApiUrl, content);

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (contentType.Contains("image/gif"))
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }

                var errorText = await response.Content.ReadAsStringAsync();
                Log.Warning($"[摸摸头] API返回非GIF内容: {errorText}");
                return null;
            }
            catch (TaskCanceledException)
            {
                Log.Warning("[摸摸头] API请求超时");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"[摸摸头] 生成GIF异常: {ex.Message}");
                return null;
            }
        }

        public override async Task Exit()
        {
            _commandRegistry?.UnregisterModuleCommands("PetPetPlugin");
            Log.Info("[摸摸头] 插件已卸载");
            await Task.CompletedTask;
        }

        public override IEnumerable<string> GetDependencies()
        {
            return Array.Empty<string>();
        }

        private string? LoadIconAsBase64()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("icon.png");
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[摸摸头] 加载图标失败: {ex.Message}");
            }
            return null;
        }
    }

    public class PetPetConfig
    {
        public int DailyLimit { get; set; } = 5;
        public bool EnableQueue { get; set; } = false;
        public int Resolution { get; set; } = 128;
        public int Delay { get; set; } = 20;
        public string BackgroundColor { get; set; } = "#ffffff";
    }
}