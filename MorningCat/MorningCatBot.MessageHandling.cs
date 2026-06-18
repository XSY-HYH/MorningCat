using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Logging;
using MorningCat.Commands;
using MorningCat.Events;
using MorningCat.I18n;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;
using MorningCat.PluginErrorDatabase;

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
                Log.Info(_i18n.T("message.received", sourceInfo, message.PlainText));
                
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
                        Log.Error(_i18n.T("message.handle_error", ex.Message));
                        TryMatchPluginError(ex, "MessageHandling");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(_i18n.T("message.handle_exception", ex.Message));
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
                    Log.Debug(I18nManager.S("message.handle_debug", message.PlainText, message.SenderId, message.GroupId, text));
                }
                
                if (!handled)
                {
                    RaiseUnhandledMessage(message);
                }
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("message.handle_exception", ex.Message));
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
                Log.Error(_i18n.T("message.unhandled_event_failed", ex.Message));
            }
        }

        private async Task SendMessageAsync(PlatformMessage originalMessage, string responseText)
        {
            if (!await _sendSemaphore.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                Log.Warning(_i18n.T("message.send_semaphore_timeout"));
                return;
            }
            
            try
            {
                var result = await _mdc.SendMessageAsync(originalMessage, responseText);
                if (!result.Success)
                {
                    Log.Warning(_i18n.T("message.send_failed_result", result.ErrorMessage));
                }
            }
            catch (Exception ex)
            {
                Log.Error(_i18n.T("message.send_failed", ex.Message));
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
                Log.Error(I18nManager.S("message.mct_status_failed", ex.Message));
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
