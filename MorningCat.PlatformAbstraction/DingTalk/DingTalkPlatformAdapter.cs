using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MorningCat.PlatformAbstraction.DingTalk
{
    /// <summary>
    /// 钉钉平台适配器 - 基于Stream API（长连接接收）+ HTTP API（发送消息）
    /// </summary>
    public class DingTalkPlatformAdapter : IPlatformAdapter
    {
        private readonly DingTalkConfig _config;
        private readonly HttpClient _httpClient;
        private DingTalkStreamClient? _streamClient;
        private bool _isAuthenticated;
        private PlatformConnectionState _connectionState = PlatformConnectionState.Disconnected;
        private bool _disposed;
        private string? _accessToken;
        private DateTime _tokenExpireTime;

        public PlatformId Platform => PlatformId.DingTalk;
        public string PlatformName => "钉钉";
        public PlatformConnectionState ConnectionState => _connectionState;
        public bool IsAuthenticated => _isAuthenticated;

        public event EventHandler<PlatformMessage>? OnMessageReceived;
        public event EventHandler<GroupJoinRequest>? OnGroupJoinRequest;
        public event EventHandler<PlatformConnectionState>? OnConnectionStateChanged;
        public event EventHandler? OnAuthenticated;
        public event EventHandler<string>? OnDisconnected;

        public DingTalkPlatformAdapter(DingTalkConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpClient = new HttpClient();
        }

        public async Task<bool> ConnectAsync()
        {
            SetConnectionState(PlatformConnectionState.Connecting);

            try
            {
                // 获取access_token
                var tokenResult = await GetAccessTokenAsync();
                if (!tokenResult)
                {
                    SetConnectionState(PlatformConnectionState.Disconnected);
                    return false;
                }

                _isAuthenticated = true;
                SetConnectionState(PlatformConnectionState.Authenticated);
                OnAuthenticated?.Invoke(this, EventArgs.Empty);

                // 使用Stream模式接收消息
                if (_config.UseStreamMode)
                {
                    _streamClient = new DingTalkStreamClient(_config.ClientId, _config.ClientSecret, OnStreamMessageReceived, OnStreamDisconnected);
                    _ = _streamClient.ConnectAsync(); // 后台运行
                }

                return true;
            }
            catch (Exception ex)
            {
                SetConnectionState(PlatformConnectionState.Disconnected);
                throw new InvalidOperationException($"钉钉连接失败: {ex.Message}", ex);
            }
        }

        public async Task CloseAsync()
        {
            _isAuthenticated = false;
            _streamClient?.Disconnect();
            SetConnectionState(PlatformConnectionState.Disconnected);
            OnDisconnected?.Invoke(this, "主动断开");
            await Task.CompletedTask;
        }

        public async Task<bool> ReconnectAsync()
        {
            await CloseAsync();
            return await ConnectAsync();
        }

        public async Task<SendMessageResult> SendMessageAsync(PlatformMessage target, string text)
        {
            if (!_isAuthenticated)
                return SendMessageResult.Fail("钉钉未认证");

            if (target.MessageType == UnifiedMessageType.Private)
                return await SendPrivateMessageAsync(target.SenderId, text);
            else if (target.GroupId != null)
                return await SendGroupMessageAsync(target.GroupId, text);
            else
                return SendMessageResult.Fail("无法确定消息目标");
        }

        public async Task<SendMessageResult> SendMessageAsync(PlatformMessage target, List<MessageSegment> segments)
        {
            // 钉钉消息段拼接为文本
            var text = new StringBuilder();
            foreach (var seg in segments)
            {
                if (seg.Type == "text" && seg.Data.TryGetValue("text", out var t))
                    text.Append(t);
                else if (seg.Type == "at" && seg.Data.TryGetValue("user_id", out var uid))
                    text.Append($"@{uid} ");
            }
            return await SendMessageAsync(target, text.ToString());
        }

        public async Task<SendMessageResult> SendPrivateMessageAsync(string userId, string text)
        {
            if (!_isAuthenticated)
                return SendMessageResult.Fail("钉钉未认证");

            try
            {
                await EnsureTokenAsync();
                var url = $"https://oapi.dingtalk.com/topapi/message/corpconversation/asyncsend_v2?access_token={_accessToken}";

                var payload = new
                {
                    agent_id = _config.AppKey,
                    userid_list = userId,
                    msg = new
                    {
                        msgtype = "text",
                        text = new { content = text }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                var result = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(result);
                var root = doc.RootElement;
                var errcode = root.GetProperty("errcode").GetInt32();

                if (errcode == 0)
                {
                    var taskId = root.TryGetProperty("task_id", out var tid) ? tid.ToString() : null;
                    return SendMessageResult.Ok(taskId);
                }
                else
                {
                    var errmsg = root.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "未知错误";
                    return SendMessageResult.Fail($"钉钉发送失败: {errmsg}");
                }
            }
            catch (Exception ex)
            {
                return SendMessageResult.Fail(ex.Message);
            }
        }

        public async Task<SendMessageResult> SendGroupMessageAsync(string groupId, string text)
        {
            if (!_isAuthenticated)
                return SendMessageResult.Fail("钉钉未认证");

            try
            {
                // 优先使用Webhook发送群消息
                if (!string.IsNullOrWhiteSpace(_config.WebhookUrl))
                {
                    return await SendViaWebhookAsync(text);
                }

                // 否则使用API发送
                await EnsureTokenAsync();
                var url = $"https://oapi.dingtalk.com/topapi/message/corpconversation/asyncsend_v2?access_token={_accessToken}";

                var payload = new
                {
                    agent_id = _config.AppKey,
                    chat_id = groupId,
                    msg = new
                    {
                        msgtype = "text",
                        text = new { content = text }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                var result = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(result);
                var root = doc.RootElement;
                var errcode = root.GetProperty("errcode").GetInt32();

                if (errcode == 0)
                    return SendMessageResult.Ok(root.TryGetProperty("task_id", out var tid) ? tid.ToString() : null);
                else
                {
                    var errmsg = root.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "未知错误";
                    return SendMessageResult.Fail($"钉钉发送失败: {errmsg}");
                }
            }
            catch (Exception ex)
            {
                return SendMessageResult.Fail(ex.Message);
            }
        }

        public IMessageBuilder CreateMessageBuilder()
        {
            return new DingTalkMessageBuilder();
        }

        public async Task<SendMessageResult> SendAsync(PlatformMessage target, MessageBody body)
        {
            if (!_isAuthenticated)
                return SendMessageResult.Fail("钉钉未认证");

            // 检查是否有钉钉特殊消息段
            foreach (var seg in body.Segments)
            {
                if (seg.Type == "dingtalk_markdown")
                {
                    var title = seg.Data.TryGetValue("title", out var t) ? t?.ToString() ?? "" : "";
                    var text = seg.Data.TryGetValue("text", out var mt) ? mt?.ToString() ?? "" : "";
                    return await SendDingTalkMarkdownAsync(target, title, text);
                }
                else if (seg.Type == "dingtalk_oa")
                {
                    var title = seg.Data.TryGetValue("title", out var t) ? t?.ToString() ?? "" : "";
                    var content = seg.Data.TryGetValue("content", out var c) ? c?.ToString() ?? "" : "";
                    return await SendDingTalkOaAsync(target, title, content);
                }
            }

            // 标准消息段拼接为文本
            var sb = new StringBuilder();
            foreach (var seg in body.Segments)
            {
                switch (seg.Type)
                {
                    case "text":
                        sb.Append(seg.Data.TryGetValue("text", out var t) ? t?.ToString() ?? "" : "");
                        break;
                    case "at":
                        var uid = seg.Data.TryGetValue("user_id", out var u) ? u?.ToString() ?? "" : "";
                        sb.Append($"@{uid} ");
                        break;
                    case "reply":
                        // 钉钉不支持回复消息，忽略
                        break;
                    case "image":
                        // 钉钉图片需要特殊处理，暂忽略
                        break;
                    default:
                        sb.Append(seg.Data.TryGetValue("text", out var dt) ? dt?.ToString() ?? "" : "");
                        break;
                }
            }

            return await SendMessageAsync(target, sb.ToString());
        }

        private async Task<SendMessageResult> SendDingTalkMarkdownAsync(PlatformMessage target, string title, string text)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_config.WebhookUrl) && target.MessageType != UnifiedMessageType.Private)
                {
                    var webhookUrl = _config.WebhookUrl;
                    if (!string.IsNullOrWhiteSpace(_config.WebhookSecret))
                    {
                        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                        var stringToSign = $"{timestamp}\n{_config.WebhookSecret}";
                        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_config.WebhookSecret));
                        var signBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
                        var sign = Convert.ToBase64String(signBytes);
                        webhookUrl += $"&timestamp={timestamp}&sign={Uri.EscapeDataString(sign)}";
                    }

                    var payload = new
                    {
                        msgtype = "markdown",
                        markdown = new { title, text }
                    };
                    var json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(webhookUrl, content);
                    var result = await response.Content.ReadAsStringAsync();

                    using var doc = JsonDocument.Parse(result);
                    var errcode = doc.RootElement.GetProperty("errcode").GetInt32();
                    if (errcode == 0)
                        return SendMessageResult.Ok("webhook");
                    var errmsg = doc.RootElement.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "未知错误";
                    return SendMessageResult.Fail($"Webhook发送失败: {errmsg}");
                }

                return SendMessageResult.Fail("钉钉Markdown消息需要Webhook配置");
            }
            catch (Exception ex)
            {
                return SendMessageResult.Fail(ex.Message);
            }
        }

        private Task<SendMessageResult> SendDingTalkOaAsync(PlatformMessage target, string title, string content)
        {
            // OA消息暂未实现
            return Task.FromResult(SendMessageResult.Fail("钉钉OA消息暂未实现"));
        }

        /// <summary>通过Webhook发送群消息</summary>
        private async Task<SendMessageResult> SendViaWebhookAsync(string text)
        {
            try
            {
                var webhookUrl = _config.WebhookUrl;

                // 如果配置了签名密钥，添加签名参数
                if (!string.IsNullOrWhiteSpace(_config.WebhookSecret))
                {
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                    var stringToSign = $"{timestamp}\n{_config.WebhookSecret}";
                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_config.WebhookSecret));
                    var signBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
                    var sign = Convert.ToBase64String(signBytes);
                    webhookUrl += $"&timestamp={timestamp}&sign={Uri.EscapeDataString(sign)}";
                }

                var payload = new
                {
                    msgtype = "text",
                    text = new { content = text }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(webhookUrl, content);
                var result = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(result);
                var root = doc.RootElement;
                var errcode = root.GetProperty("errcode").GetInt32();

                if (errcode == 0)
                    return SendMessageResult.Ok("webhook");
                else
                {
                    var errmsg = root.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "未知错误";
                    return SendMessageResult.Fail($"Webhook发送失败: {errmsg}");
                }
            }
            catch (Exception ex)
            {
                return SendMessageResult.Fail(ex.Message);
            }
        }

        /// <summary>获取access_token</summary>
        private async Task<bool> GetAccessTokenAsync()
        {
            try
            {
                var url = "https://oapi.dingtalk.com/gettoken";
                var queryParams = $"?appkey={_config.AppKey}&appsecret={_config.AppSecret}";
                var response = await _httpClient.GetAsync(url + queryParams);
                var result = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(result);
                var root = doc.RootElement;
                var errcode = root.GetProperty("errcode").GetInt32();

                if (errcode == 0)
                {
                    _accessToken = root.GetProperty("access_token").GetString();
                    var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 7200;
                    _tokenExpireTime = DateTime.Now.AddSeconds(expiresIn - 60); // 提前60秒刷新
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>确保token有效</summary>
        private async Task EnsureTokenAsync()
        {
            if (_accessToken == null || DateTime.Now >= _tokenExpireTime)
            {
                await GetAccessTokenAsync();
            }
        }

        /// <summary>Stream消息回调</summary>
        private void OnStreamMessageReceived(DingTalkStreamMessage streamMsg)
        {
            var message = ConvertToPlatformMessage(streamMsg);
            if (message != null)
                OnMessageReceived?.Invoke(this, message);
        }

        /// <summary>Stream断开回调</summary>
        private void OnStreamDisconnected(string reason)
        {
            _isAuthenticated = false;
            SetConnectionState(PlatformConnectionState.Disconnected);
            OnDisconnected?.Invoke(this, reason);
        }

        private PlatformMessage? ConvertToPlatformMessage(DingTalkStreamMessage streamMsg)
        {
            if (streamMsg?.Data == null) return null;

            var data = streamMsg.Data;
            var message = new PlatformMessage
            {
                Platform = PlatformId.DingTalk,
                MessageId = data.MessageId ?? Guid.NewGuid().ToString(),
                SenderId = data.SenderId ?? data.SenderNick ?? "",
                SenderName = data.SenderNick ?? data.SenderId ?? "",
                SelfId = data.ChatbotUserId ?? "",
                PlainText = data.Text?.Content ?? "",
                RawMessage = streamMsg
            };

            // 判断消息类型
            if (data.ConversationType == "1")
            {
                message.MessageType = UnifiedMessageType.Private;
            }
            else if (data.ConversationType == "2")
            {
                message.MessageType = UnifiedMessageType.Group;
                message.GroupId = data.ConversationId;
                message.GroupName = data.ConversationTitle;
            }

            // 是否@了机器人
            message.IsAtBot = data.IsAdminInRobot == true;

            // 文本消息段
            if (!string.IsNullOrEmpty(data.Text?.Content))
                message.Segments.Add(MessageSegment.Text(data.Text.Content));

            return message;
        }

        private void SetConnectionState(PlatformConnectionState state)
        {
            if (_connectionState != state)
            {
                _connectionState = state;
                OnConnectionStateChanged?.Invoke(this, state);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _streamClient?.Disconnect();
            _httpClient.Dispose();
        }
    }

    #region 钉钉Stream客户端

    /// <summary>
    /// 钉钉Stream模式客户端 - 通过长连接接收消息
    /// </summary>
    internal class DingTalkStreamClient
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly Action<DingTalkStreamMessage> _onMessage;
        private readonly Action<string> _onDisconnected;
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _cts;
        private bool _running;

        public DingTalkStreamClient(string clientId, string clientSecret,
            Action<DingTalkStreamMessage> onMessage, Action<string> onDisconnected)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
            _onMessage = onMessage;
            _onDisconnected = onDisconnected;
            _httpClient = new HttpClient();
        }

        public async Task ConnectAsync()
        {
            _cts = new CancellationTokenSource();
            _running = true;

            try
            {
                // 1. 获取Stream连接端点
                var endpoint = await GetStreamEndpointAsync();
                if (endpoint == null)
                {
                    _onDisconnected("获取Stream端点失败");
                    return;
                }

                // 2. 通过WebSocket连接到Stream端点
                // 这里使用简单的轮询模式作为降级方案
                await PollMessagesAsync(endpoint);
            }
            catch (Exception ex)
            {
                _onDisconnected($"Stream连接异常: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            _running = false;
            _cts?.Cancel();
        }

        private async Task<string?> GetStreamEndpointAsync()
        {
            try
            {
                var url = "https://api.dingtalk.com/v1.0/gateway/connections/open";
                var payload = new
                {
                    clientId = _clientId,
                    clientSecret = _clientSecret,
                    subscriptions = new[]
                    {
                        new
                        {
                            type = "EVENT",
                            topic = "/v1.0/im/bot/messages/get"
                        }
                    },
                    ua = "MorningCat-DingTalk-Adapter/1.0"
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                var result = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(result);
                if (doc.RootElement.TryGetProperty("endpoint", out var ep))
                    return ep.GetString();

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task PollMessagesAsync(string endpoint)
        {
            while (_running && !_cts!.Token.IsCancellationRequested)
            {
                try
                {
                    // 钉钉Stream API长轮询
                    var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    request.Headers.Add("x-acs-dingtalk-access-token", await GetStreamTokenAsync());

                    var payload = new
                    {
                        clientId = _clientId,
                        clientSecret = _clientSecret
                    };
                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request, _cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadAsStringAsync(_cts.Token);
                        ProcessStreamResponse(result);
                    }

                    await Task.Delay(1000, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(5000, _cts.Token);
                }
            }
        }

        private async Task<string> GetStreamTokenAsync()
        {
            var url = "https://api.dingtalk.com/v1.0/oauth2/accessToken";
            var payload = new
            {
                appKey = _clientId,
                appSecret = _clientSecret
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            var result = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(result);
            return doc.RootElement.GetProperty("accessToken").GetString() ?? "";
        }

        private void ProcessStreamResponse(string responseJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        var streamMsg = JsonSerializer.Deserialize<DingTalkStreamMessage>(item.GetRawText());
                        if (streamMsg != null)
                            _onMessage(streamMsg);
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    var streamMsg = JsonSerializer.Deserialize<DingTalkStreamMessage>(responseJson);
                    if (streamMsg != null)
                        _onMessage(streamMsg);
                }
            }
            catch { }
        }
    }

    #endregion

    #region 钉钉消息模型

    /// <summary>
    /// 钉钉Stream消息
    /// </summary>
    internal class DingTalkStreamMessage
    {
        public string? Type { get; set; }
        public string? Topic { get; set; }
        public DingTalkMessageData? Data { get; set; }
        public string? MessageId { get; set; }
    }

    /// <summary>
    /// 钉钉消息数据
    /// </summary>
    internal class DingTalkMessageData
    {
        public string? MessageId { get; set; }
        public string? ConversationType { get; set; }
        public string? ConversationId { get; set; }
        public string? ConversationTitle { get; set; }
        public string? SenderId { get; set; }
        public string? SenderNick { get; set; }
        public string? ChatbotUserId { get; set; }
        public bool? IsAdminInRobot { get; set; }
        public DingTalkTextContent? Text { get; set; }
    }

    /// <summary>
    /// 钉钉文本内容
    /// </summary>
    internal class DingTalkTextContent
    {
        public string? Content { get; set; }
    }

    #endregion
}
