using System;
using System.Threading.Tasks;
using Logging;
using OneBotLib;
using OneBotLib.Events;
using OneBotLib.Models;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private void OnMessageReceived(object? sender, MessageEventArgs e)
        {
            try
            {
                var message = e.Message;
                var sourceInfo = GetMessageSourceInfo(message);
                Log.Info($"接受来自{sourceInfo}的消息：{message.PlainText}");
                
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
        
        private async Task HandleMessageAsync(MessageObject message)
        {
            try
            {
                Log.Debug($"处理消息: PlainText='{message.PlainText}', UserId={message.UserId}, GroupId={message.GroupId}");
                
                if (string.IsNullOrEmpty(message.PlainText))
                {
                    Log.Debug("消息内容为空，跳过处理");
                    return;
                }
                
                string cleanedText = message.PlainText.Trim();
                if (cleanedText.StartsWith("\"") && cleanedText.EndsWith("\""))
                {
                    cleanedText = cleanedText.Substring(1, cleanedText.Length - 2);
                }
                
                cleanedText = System.Text.RegularExpressions.Regex.Replace(
                    cleanedText, @"\[CQ:at,qq=\d+\]\s*", "").Trim();
                cleanedText = System.Text.RegularExpressions.Regex.Replace(
                    cleanedText, @"\[CQ:at,qq=all\]\s*", "").Trim();
                
                string text = cleanedText.Trim();
                Log.Debug($"处理消息: {text}");
                
                bool isAtBot = CheckIsAtBot(message);
                bool hasSlash = text.StartsWith("/");
                
                if (hasSlash || isAtBot)
                {
                    bool handled = await _commandRegistry.ExecuteCommandAsync(message, text);
                    
                    if (!handled)
                    {
                        Log.Debug($"未识别的命令: {text}");
                    }
                }
                else
                {
                    Log.Debug($"非命令消息: {text}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"HandleMessageAsync 异常: {ex.Message}");
                throw;
            }
        }

        private bool CheckIsAtBot(MessageObject message)
        {
            var selfId = _client.CurrentAccountInfo?.UserId ?? 0;
            
            if (message.MessageSegments != null && message.MessageSegments.Count > 0)
            {
                foreach (var segment in message.MessageSegments)
                {
                    if (segment.Type == "at" && segment.Data != null)
                    {
                        if (segment.Data.TryGetValue("qq", out var qqValue))
                        {
                            var qqStr = qqValue?.ToString();
                            if (qqStr == selfId.ToString() || qqStr == "all")
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(message.PlainText))
            {
                var atPattern = $"[CQ:at,qq={selfId}]";
                var containsBot = message.PlainText.Contains(atPattern);
                var containsAll = message.PlainText.Contains("[CQ:at,qq=all]");
                Log.Debug($"检查@: selfId={selfId}, PlainText='{message.PlainText}', atPattern='{atPattern}', containsBot={containsBot}, containsAll={containsAll}");
                
                if (containsBot || containsAll)
                {
                    Log.Debug("检测到@机器人");
                    return true;
                }
            }

            return false;
        }
        
        private async Task SendMessageAsync(MessageObject originalMessage, string responseText)
        {
            Log.Debug("等待发送信号量...");
            if (!await _sendSemaphore.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                Log.Warning("获取发送信号量超时");
                return;
            }
            
            try
            {
                var targetInfo = originalMessage.MessageType == "group" 
                    ? $"群{originalMessage.GroupId}" 
                    : $"私聊{originalMessage.UserId}";
                Log.Debug($"回复->{targetInfo}: {responseText}");
                
                if (originalMessage.MessageType == "private")
                {
                    var result = await _client.SendPrivateMsgAsync(originalMessage.UserId ?? 0, responseText);
                    if (!result.Success)
                    {
                        Log.Warning($"发送私聊消息失败: {result.ErrorMessage}");
                    }
                }
                else if (originalMessage.MessageType == "group")
                {
                    var result = await _client.SendGroupMsgAsync(originalMessage.GroupId ?? 0, responseText);
                    if (!result.Success)
                    {
                        Log.Warning($"发送群消息失败: {result.ErrorMessage}");
                    }
                }
                else
                {
                    Log.Warning($"未知的消息类型: {originalMessage.MessageType}");
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
        
        private string GetMessageSourceInfo(MessageObject message)
        {
            if (message.MessageType == "group")
            {
                return $"{message.Sender?.Nickname ?? "未知"}（{message.UserId}）, 群组: {message.GroupId}";
            }
            else
            {
                return $"{message.Sender?.Nickname ?? "未知"}（{message.UserId}）";
            }
        }
    }
}
