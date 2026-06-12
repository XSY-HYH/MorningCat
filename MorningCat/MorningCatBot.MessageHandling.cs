using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Logging;
using MorningCat.Commands;
using MorningCat.Events;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private readonly Dictionary<long, string> _groupNameCache = new();
        
        private void OnPlatformMessageReceived(object? sender, PlatformMessage message)
        {
            try
            {
                var sourceInfo = message.GetSourceInfo();
                Log.Name("MessageHandling");
                Log.Info($"接受来自{sourceInfo}的消息：{message.PlainText}");
                
                var briefInfo = message.GetBriefInfo();
                Log.Debug(briefInfo);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleMessageAsync(message);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"处理消息时出错: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"处理消息时发生异常: {ex.Message}");
            }
        }
        
        private async Task HandleMessageAsync(PlatformMessage message)
        {
            try
            {
                if (string.IsNullOrEmpty(message.PlainText))
                {
                    return;
                }
                
                var config = _configManager.GetConfig();
                if (long.TryParse(message.SenderId, out var senderId) && config.BlockedUsers.Contains(senderId))
                {
                    return;
                }
                if (message.GroupId != null && long.TryParse(message.GroupId, out var groupId) && config.BlockedGroups.Contains(groupId))
                {
                    return;
                }
                
                string cleanedText = MessageHelper.CleanMessageText(message.PlainText);
                
                var selfId = message.SelfId;
                cleanedText = MessageHelper.RemoveAtSegments(cleanedText, selfId);
                
                string text = cleanedText.Trim();
                
                // 检测 #Mct 状态查询
                if (config.EnableMctStatus && (text.Equals("#Mct", StringComparison.OrdinalIgnoreCase) || text.Equals("#mct", StringComparison.OrdinalIgnoreCase)))
                {
                    await HandleMctStatusQuery(message);
                    return;
                }
                
                bool handled = await _commandRegistry.ExecuteCommandAsync(message, text);
                
                if (handled)
                {
                    Log.Name("MessageHandling");
                    Log.Debug($"处理PlainText='{message.PlainText}', SenderId={message.SenderId}, GroupId={message.GroupId}, Command={text}");
                }
                
                if (!handled)
                {
                    RaiseUnhandledMessage(message);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"HandleMessageAsync 异常: {ex.Message}");
                throw;
            }
        }

        private void RaiseUnhandledMessage(PlatformMessage message)
        {
            try
            {
                OnUnhandledMessage?.Invoke(this, new UnhandledMessageEventArgs(message));
            }
            catch (Exception ex)
            {
                Log.Error($"触发未处理消息事件失败: {ex.Message}");
            }
        }

        private async Task SendMessageAsync(PlatformMessage originalMessage, string responseText)
        {
            if (!await _sendSemaphore.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                Log.Warning("获取发送信号量超时");
                return;
            }
            
            try
            {
                var result = await _mdc.SendMessageAsync(originalMessage, responseText);
                if (!result.Success)
                {
                    Log.Warning($"发送消息失败: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"发送消息失败: {ex.Message}");
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        private async Task HandleMctStatusQuery(PlatformMessage message)
        {
            try
            {
                var mctAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MorningCat");
                var version = mctAssembly?.GetName().Version;
                var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";

                var uptime = DateTime.Now - _startTime;
                var uptimeStr = FormatUptime(uptime);

                var wsDisconnects = _wsDisconnectCount;
                var errorCount = Log.ErrorCount;
                var warningCount = Log.WarningCount;

                var statusText = $"MorningCat\nv{versionStr}\nMorningCat已稳定运行{uptimeStr}\nws断开次数：{wsDisconnects}，已捕获的异常数量：{errorCount}，已捕获的警告信息数量：{warningCount}";

                if (Log.LastWarningMessage != null)
                {
                    statusText += $"\n最近的一次警告消息：{Log.LastWarningMessage}";
                }
                if (Log.LastErrorMessage != null)
                {
                    statusText += $"\n最近的一次错误消息：{Log.LastErrorMessage}";
                }

                await _mdc.SendAsync(message, builder => builder
                    .Reply(message.MessageId)
                    .Text(statusText));
            }
            catch (Exception ex)
            {
                Log.Error($"处理#Mct状态查询失败: {ex.Message}");
            }
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            var parts = new List<string>();

            int days = (int)uptime.TotalDays;
            int hours = uptime.Hours;
            int minutes = uptime.Minutes;
            int seconds = uptime.Seconds;

            if (days > 0)
            {
                parts.Add($"{days}天");
                parts.Add($"{hours}小时");
                if (minutes > 0) parts.Add($"{minutes}分钟");
            }
            else if (hours > 0)
            {
                parts.Add($"{hours}小时");
                if (minutes > 0) parts.Add($"{minutes}分钟");
                if (seconds > 0) parts.Add($"{seconds}秒");
            }
            else if (minutes > 0)
            {
                parts.Add($"{minutes}分钟");
                if (seconds > 0) parts.Add($"{seconds}秒");
            }
            else
            {
                parts.Add($"{seconds}秒");
            }

            return string.Join("", parts);
        }
    }
}
