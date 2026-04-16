using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MorningCat.WebUI.Services;

namespace MorningCat.WebUI
{
    public class WebUIServer : IWebUIServer
    {
        private IHost? _host;
        private AccountService _accountService;
        private IConfigProvider? _configProvider;
        private ISystemInfoProvider? _systemInfoProvider;
        private IBotInfoProvider? _botInfoProvider;
        private IPluginInfoProvider? _pluginInfoProvider;
        private ILogProvider? _logProvider;
        private CancellationTokenSource? _shutdownCts;
        
        public Action<string, string>? OnCredentialsChanged { get; set; }
        
        public int Port { get; private set; }
        public bool IsRunning => _host != null;

        public WebUIServer(string username = "admin", string password = "admin123")
        {
            _accountService = new AccountService(username, password);
            _accountService.CredentialsChanged += (user, pass) => OnCredentialsChanged?.Invoke(user, pass);
        }

        public void SetSystemInfoProvider(ISystemInfoProvider provider)
        {
            _systemInfoProvider = provider;
        }

        public void SetBotInfoProvider(IBotInfoProvider provider)
        {
            _botInfoProvider = provider;
        }

        public void SetPluginInfoProvider(IPluginInfoProvider provider)
        {
            _pluginInfoProvider = provider;
        }

        public void SetLogProvider(ILogProvider provider)
        {
            _logProvider = provider;
        }

        public void SetConfigProvider(IConfigProvider configProvider)
        {
            _configProvider = configProvider;
        }

        public AccountService GetAccountService()
        {
            return _accountService;
        }

        public async Task StartAsync(int port = 8080)
        {
            if (_host != null)
                return;

            Port = port;
            _shutdownCts = new CancellationTokenSource();

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Warning);
                    logging.AddFilter("Microsoft", LogLevel.Warning);
                    logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);
                    logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                    logging.AddFilter("System", LogLevel.Warning);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSingleton(_accountService);
                        services.AddSingleton(this);
                    });
                    
                    webBuilder.Configure((context, app) =>
                    {
                        app.Use(async (ctx, next) =>
                        {
                            var remoteIp = ctx.Connection.RemoteIpAddress?.ToString();
                            if (remoteIp != null && !IsLocalRequest(remoteIp))
                            {
                                ctx.Response.StatusCode = 403;
                                await ctx.Response.WriteAsync("Access denied. Only localhost access is allowed.");
                                return;
                            }
                            await next();
                        });

                        var assembly = typeof(WebUIServer).Assembly;
                        var webuiFileProvider = new EmbeddedFileProvider(assembly, "MorningCat.WebUI.wwwroot.webui");
                        var rootFileProvider = new EmbeddedFileProvider(assembly, "MorningCat.WebUI.wwwroot");
                        
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = webuiFileProvider,
                            RequestPath = "/webui"
                        });

                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = rootFileProvider,
                            RequestPath = "/static"
                        });

                        ConfigureRoutes(app);
                    });
                    
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.ListenLocalhost(port);
                    });
                })
                .Build();

            await _host.StartAsync();
        }

        public async Task StopAsync()
        {
            if (_shutdownCts != null)
            {
                _shutdownCts.Cancel();
                _shutdownCts.Dispose();
                _shutdownCts = null;
            }
            
            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
                _host = null;
            }
        }

        private void ConfigureRoutes(IApplicationBuilder app)
        {
            ConfigureApiRoutes(app);

            app.Run(async context =>
            {
                var path = context.Request.Path.Value ?? "/";
                
                if (path == "/" || path == "/webui")
                {
                    context.Response.Redirect("/webui/");
                    return;
                }
                
                if (path == "/webui/")
                {
                    var html = GetEmbeddedResource("webui.index.html");
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(html);
                }
                else if (path.StartsWith("/webui/") && !path.Contains("."))
                {
                    var html = GetEmbeddedResource("webui.index.html");
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(html);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Not found");
                }
            });
        }

        private void ConfigureApiRoutes(IApplicationBuilder app)
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.ContentType = "application/json";
                }
                await next();
            });

            // Auth endpoints
            app.Map("/api/auth/check", authCheckApp =>
            {
                authCheckApp.Run(async context =>
                {
                    var sessionToken = context.Request.Cookies["session_token"];
                    var authHeader = context.Request.Headers["Authorization"].ToString();
                    
                    bool isAuthenticated = false;
                    if (!string.IsNullOrEmpty(sessionToken) && _accountService.ValidateToken(sessionToken))
                    {
                        isAuthenticated = true;
                    }
                    else if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                    {
                        var token = authHeader.Substring(7);
                        if (_accountService.ValidateToken(token))
                        {
                            isAuthenticated = true;
                        }
                    }

                    await context.Response.WriteAsync(JsonResponse(isAuthenticated));
                });
            });

            app.Map("/api/auth/login", authLoginApp =>
            {
                authLoginApp.Run(async context =>
                {
                    if (context.Request.Method != "POST")
                    {
                        context.Response.StatusCode = 405;
                        return;
                    }

                    using var reader = new StreamReader(context.Request.Body);
                    var body = await reader.ReadToEndAsync();
                    var json = JsonDocument.Parse(body);
                    
                    string? username = null;
                    string? password = null;
                    string? hash = null;
                    
                    if (json.RootElement.TryGetProperty("username", out var userEl))
                        username = userEl.GetString();
                    if (json.RootElement.TryGetProperty("password", out var passEl))
                        password = passEl.GetString();
                    if (json.RootElement.TryGetProperty("hash", out var hashEl))
                        hash = hashEl.GetString();

                    bool valid = false;
                    if (!string.IsNullOrEmpty(hash))
                    {
                        valid = ValidateHashLogin(hash);
                    }
                    else if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                    {
                        valid = _accountService.ValidateCredentials(username, password);
                    }

                    if (valid)
                    {
                        var token = _accountService.GenerateToken();
                        context.Response.Cookies.Append("session_token", token, new CookieOptions
                        {
                            HttpOnly = true,
                            SameSite = SameSiteMode.Strict,
                            MaxAge = TimeSpan.FromHours(24)
                        });
                        
                        await context.Response.WriteAsync(JsonResponse(new { Credential = token }));
                    }
                    else
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsync(JsonResponse(null, "Invalid credentials", 401));
                    }
                });
            });

            // Passkey endpoints (not supported - return 404 to fallback to password login)
            app.Map("/api/auth/passkey/generate-authentication-options", passkeyAuthApp =>
            {
                passkeyAuthApp.Run(async context =>
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync(JsonResponse(null, "Passkey not supported", 404));
                });
            });

            app.Map("/api/auth/passkey/generate-registration-options", passkeyRegApp =>
            {
                passkeyRegApp.Run(async context =>
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync(JsonResponse(null, "Passkey not supported", 404));
                });
            });

            app.Map("/api/auth/passkey/verify-registration", passkeyVerifyRegApp =>
            {
                passkeyVerifyRegApp.Run(async context =>
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync(JsonResponse(null, "Passkey not supported", 404));
                });
            });

            app.Map("/api/auth/passkey/verify-authentication", passkeyVerifyAuthApp =>
            {
                passkeyVerifyAuthApp.Run(async context =>
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync(JsonResponse(null, "Passkey not supported", 404));
                });
            });

            // Password update endpoint
            app.Map("/api/auth/update_password", updatePasswordApp =>
            {
                updatePasswordApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    if (context.Request.Method != "POST")
                    {
                        context.Response.StatusCode = 405;
                        return;
                    }
                    
                    using var reader = new StreamReader(context.Request.Body);
                    var body = await reader.ReadToEndAsync();
                    var json = JsonDocument.Parse(body);
                    
                    if (!json.RootElement.TryGetProperty("oldPassword", out var oldPassEl) ||
                        !json.RootElement.TryGetProperty("newPassword", out var newPassEl))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "Missing oldPassword or newPassword", 400));
                        return;
                    }
                    
                    var oldPassword = oldPassEl.GetString();
                    var newPassword = newPassEl.GetString();
                    
                    if (!_accountService.ValidateCredentials(_accountService.CurrentUsername, oldPassword))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "旧密码错误", 401));
                        return;
                    }
                    
                    if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "新密码至少需要6个字符", 400));
                        return;
                    }
                    
                    _accountService.UpdatePassword(newPassword);
                    await context.Response.WriteAsync(JsonResponse(true, "密码修改成功"));
                });
            });

            // Config API
            app.Map("/api/config", configApp =>
            {
                configApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    if (context.Request.Method == "GET")
                    {
                        var config = _configProvider?.GetConfig();
                        if (config == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "Config not available", 500));
                            return;
                        }
                        
                        await context.Response.WriteAsync(JsonResponse(new
                        {
                            napCatServerUrl = config.NapCatServerUrl,
                            napCatToken = config.NapCatToken,
                            modulesDirectory = config.ModulesDirectory,
                            autoLoadModules = config.AutoLoadModules,
                            ownerQQ = config.OwnerQQ,
                            adminQQs = config.AdminQQs,
                            webui = new
                            {
                                enabled = config.WebUI.Enabled,
                                port = config.WebUI.Port,
                                username = config.WebUI.Username,
                                password = ""
                            }
                        }));
                    }
                    else if (context.Request.Method == "POST")
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();
                        var json = JsonDocument.Parse(body);
                        
                        _configProvider?.UpdateConfig(config =>
                        {
                            if (json.RootElement.TryGetProperty("napCatServerUrl", out var urlEl))
                                config.NapCatServerUrl = urlEl.GetString() ?? config.NapCatServerUrl;
                            if (json.RootElement.TryGetProperty("napCatToken", out var tokenEl))
                                config.NapCatToken = tokenEl.GetString() ?? config.NapCatToken;
                            if (json.RootElement.TryGetProperty("modulesDirectory", out var dirEl))
                                config.ModulesDirectory = dirEl.GetString() ?? config.ModulesDirectory;
                            if (json.RootElement.TryGetProperty("autoLoadModules", out var autoEl))
                                config.AutoLoadModules = autoEl.GetBoolean();
                            if (json.RootElement.TryGetProperty("ownerQQ", out var ownerEl))
                                config.OwnerQQ = ownerEl.GetInt64();
                            if (json.RootElement.TryGetProperty("adminQQs", out var adminsEl) && adminsEl.ValueKind == JsonValueKind.Array)
                            {
                                config.AdminQQs = adminsEl.EnumerateArray()
                                    .Select(e => e.GetInt64())
                                    .ToList();
                            }
                            if (json.RootElement.TryGetProperty("webui", out var webuiEl))
                            {
                                if (webuiEl.TryGetProperty("enabled", out var enabledEl))
                                    config.WebUI.Enabled = enabledEl.GetBoolean();
                                if (webuiEl.TryGetProperty("port", out var portEl))
                                    config.WebUI.Port = portEl.GetInt32();
                                if (webuiEl.TryGetProperty("username", out var userEl))
                                    config.WebUI.Username = userEl.GetString() ?? config.WebUI.Username;
                                if (webuiEl.TryGetProperty("password", out var passEl) && !string.IsNullOrEmpty(passEl.GetString()))
                                    config.WebUI.Password = passEl.GetString()!;
                            }
                        });
                        
                        await context.Response.WriteAsync(JsonResponse(true, "配置已保存"));
                    }
                    else
                    {
                        context.Response.StatusCode = 405;
                    }
                });
            });

            // About page content
            app.Map("/api/base/about", aboutApp =>
            {
                aboutApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    try
                    {
                        var assembly = typeof(WebUIServer).Assembly;
                        using var stream = assembly.GetManifestResourceStream("MorningCat.WebUI.Resources.about.md");
                        
                        if (stream == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "About content not found", 404));
                            return;
                        }
                        
                        using var reader = new StreamReader(stream);
                        var content = await reader.ReadToEndAsync();
                        
                        await context.Response.WriteAsync(JsonResponse(new { content }));
                    }
                    catch (Exception ex)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, $"Error loading about: {ex.Message}", 500));
                    }
                });
            });

            // Version info
            app.Map("/api/base/GetNapCatVersion", versionApp =>
            {
                versionApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var info = _systemInfoProvider?.GetSystemInfo();
                    await context.Response.WriteAsync(JsonResponse(new
                    {
                        name = "MorningCat",
                        version = info?.Version ?? "1.0.0",
                        description = "MorningCat Bot Framework"
                    }));
                });
            });

            app.Map("/api/base/CheckUpdate", updateApp =>
            {
                updateApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    try
                    {
                        var handler = new HttpClientHandler
                        {
                            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                        };
                        
                        using var client = new HttpClient(handler);
                        client.Timeout = TimeSpan.FromSeconds(10);
                        
                        var response = await client.GetAsync("https://110.42.98.47:59113/api/files?path=MorningCat%2FCore");
                        response.EnsureSuccessStatusCode();
                        
                        var content = await response.Content.ReadAsStringAsync();
                        await context.Response.WriteAsync(JsonResponse(new
                        {
                            success = true,
                            data = content
                        }));
                    }
                    catch (Exception ex)
                    {
                        await context.Response.WriteAsync(JsonResponse(new
                        {
                            success = false,
                            error = ex.Message
                        }));
                    }
                });
            });

            // Theme
            app.Map("/api/base/Theme", themeApp =>
            {
                themeApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    await context.Response.WriteAsync(JsonResponse(new
                    {
                        primary = "217.2 91.2% 59.8%",
                        radius = "0.75rem"
                    }));
                });
            });

            app.Map("/files/theme.css", themeCssApp =>
            {
                themeCssApp.Run(async context =>
                {
                    context.Response.Headers["Content-Type"] = "text/css";
                    context.Response.Headers["Cache-Control"] = "no-cache";
                    
                    var css = @"
:root {
  --font-family-base: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}
";
                    await context.Response.WriteAsync(css);
                });
            });

            app.Map("/api/base/SetTheme", setThemeApp =>
            {
                setThemeApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    await context.Response.WriteAsync(JsonResponse(true));
                });
            });

            // System status (SSE)
            app.Map("/api/base/GetSysStatusRealTime", sysStatusApp =>
            {
                sysStatusApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    context.Response.Headers["Content-Type"] = "text/event-stream";
                    context.Response.Headers["Cache-Control"] = "no-cache";
                    context.Response.Headers["Connection"] = "keep-alive";

                    var shutdownToken = _shutdownCts?.Token ?? CancellationToken.None;
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, context.RequestAborted);
                    var cancellationToken = linkedCts.Token;
                    
                    try
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            var info = _systemInfoProvider?.GetSystemInfo();
                            var status = new
                            {
                                cpu = new
                                {
                                    core = Environment.ProcessorCount,
                                    model = info?.CpuModel ?? "Unknown",
                                    speed = info?.CpuSpeed ?? "0",
                                    usage = new
                                    {
                                        system = (info?.CpuUsage ?? 0).ToString("F1"),
                                        qq = "0"
                                    }
                                },
                                memory = new
                                {
                                    total = ((info?.MemoryTotalMB ?? 0) * 1024 * 1024).ToString(),
                                    usage = new
                                    {
                                        system = ((info?.MemoryUsedMB ?? 0) * 1024 * 1024).ToString(),
                                        qq = "0"
                                    }
                                },
                                arch = info?.Arch ?? "Unknown"
                            };
                            
                            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(status, _jsonOptions)}\n\n");
                            await context.Response.Body.FlushAsync(cancellationToken);
                            
                            await Task.Delay(1000, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                });
            });

            // Logs
            app.Map("/api/Log/GetLogList", logListApp =>
            {
                logListApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var logs = _logProvider?.GetLogs(1000) ?? new List<LogEntry>();
                    var logFiles = logs.Select(l => l.Time.ToString("yyyy-MM-dd")).Distinct().ToList();
                    await context.Response.WriteAsync(JsonResponse(logFiles));
                });
            });

            app.Map("/api/Log/GetLog", getLogApp =>
            {
                getLogApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var logs = _logProvider?.GetLogs(1000) ?? new List<LogEntry>();
                    var content = string.Join("\n", logs.Select(l => $"{l.Time:yyyy-MM-dd HH:mm:ss} - {l.Level} - [{l.Source}] - {l.Message}"));
                    await context.Response.WriteAsync(JsonResponse(content));
                });
            });

            app.Map("/api/Log/GetLogRealTime", logRealTimeApp =>
            {
                logRealTimeApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    context.Response.Headers["Content-Type"] = "text/event-stream";
                    context.Response.Headers["Cache-Control"] = "no-cache";
                    context.Response.Headers["Connection"] = "keep-alive";

                    var tcs = new TaskCompletionSource<bool>();
                    Action<string>? rawLogCallback = null;
                    var shutdownToken = _shutdownCts?.Token ?? CancellationToken.None;
                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, context.RequestAborted);
                    
                    try
                    {
                        rawLogCallback = (rawLog) =>
                        {
                            try
                            {
                                if (linkedCts.Token.IsCancellationRequested)
                                {
                                    tcs.TrySetResult(true);
                                    return;
                                }
                                var logData = new { raw = rawLog };
                                var json = JsonSerializer.Serialize(logData, _jsonOptions);
                                var bytes = System.Text.Encoding.UTF8.GetBytes($"data: {json}\n\n");
                                context.Response.Body.WriteAsync(bytes, 0, bytes.Length, linkedCts.Token).Wait();
                                context.Response.Body.FlushAsync(linkedCts.Token).Wait();
                            }
                            catch
                            {
                                tcs.TrySetResult(true);
                            }
                        };

                        var recentLogs = _logProvider?.GetRecentRawLogs(50) ?? new List<string>();
                        foreach (var log in recentLogs)
                        {
                            var logData = new { raw = log };
                            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(logData, _jsonOptions)}\n\n", linkedCts.Token);
                            await context.Response.Body.FlushAsync(linkedCts.Token);
                        }

                        _logProvider?.SubscribeToRawLogs(rawLogCallback);

                        await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
                    }
                    finally
                    {
                        linkedCts.Dispose();
                        if (rawLogCallback != null)
                        {
                            _logProvider?.UnsubscribeFromRawLogs(rawLogCallback);
                        }
                    }
                });
            });

            // WebUI Config
            app.Map("/api/WebUIConfig/GetConfig", webuiConfigApp =>
            {
                webuiConfigApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    await context.Response.WriteAsync(JsonResponse(new
                    {
                        port = Port,
                        host = "127.0.0.1"
                    }));
                });
            });

            app.Map("/api/WebUIConfig/UpdateConfig", updateWebuiConfigApp =>
            {
                updateWebuiConfigApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    await context.Response.WriteAsync(JsonResponse(true));
                });
            });

            // QQ Login Info
            app.Map("/api/QQLogin/GetQQLoginInfo", getLoginInfoApp =>
            {
                getLoginInfoApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var botInfo = _botInfoProvider?.GetBotInfo();
                    var sysInfo = _systemInfoProvider?.GetSystemInfo();
                    
                    await context.Response.WriteAsync(JsonResponse(new
                    {
                        uid = botInfo?.UserId.ToString() ?? "0",
                        uin = botInfo?.UserId.ToString() ?? "0",
                        nick = botInfo?.Nickname ?? "MorningCat",
                        online = botInfo?.IsOnline ?? false,
                        level = botInfo?.Level ?? 0,
                        avatarUrl = botInfo != null ? $"https://q1.qlogo.cn/g?b=qq&nk={botInfo.UserId}&s=0" : "",
                        startTime = sysInfo?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                        uptime = sysInfo?.Uptime.ToString(@"dd\.hh\:mm\:ss") ?? ""
                    }));
                });
            });

            // Process
            app.Map("/api/Process/Restart", restartApp =>
            {
                restartApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    _systemInfoProvider?.RequestRestart();
                    await context.Response.WriteAsync(JsonResponse(true, "Restart requested"));
                });
            });

            app.Map("/api/Process/Shutdown", shutdownApp =>
            {
                shutdownApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    _systemInfoProvider?.RequestShutdown();
                    await context.Response.WriteAsync(JsonResponse(true, "Shutdown requested"));
                });
            });

            // Plugins - 注意：更具体的路由要放在前面，因为 Map 是前缀匹配
            app.Map("/api/plugins/disable", disablePluginApp =>
            {
                disablePluginApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var moduleName = context.Request.Query["name"].ToString();
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        await context.Response.WriteAsync(JsonResponse(false, "Module name required"));
                        return;
                    }

                    var success = _pluginInfoProvider?.DisablePlugin(moduleName) ?? false;
                    await context.Response.WriteAsync(JsonResponse(success));
                });
            });

            app.Map("/api/plugins/enable", enablePluginApp =>
            {
                enablePluginApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var moduleName = context.Request.Query["name"].ToString();
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        await context.Response.WriteAsync(JsonResponse(false, "Module name required"));
                        return;
                    }

                    var success = _pluginInfoProvider?.EnablePlugin(moduleName) ?? false;
                    await context.Response.WriteAsync(JsonResponse(success));
                });
            });

            app.Map("/api/plugins/unload", unloadPluginApp =>
            {
                unloadPluginApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var moduleName = context.Request.Query["name"].ToString();
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        await context.Response.WriteAsync(JsonResponse(false, "Module name required"));
                        return;
                    }

                    var success = _pluginInfoProvider?.UnloadPlugin(moduleName) ?? false;
                    await context.Response.WriteAsync(JsonResponse(success));
                });
            });

            app.Map("/api/plugins/detail", pluginDetailApp =>
            {
                pluginDetailApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var moduleName = context.Request.Query["name"].ToString();
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "Module name required"));
                        return;
                    }

                    var detail = _pluginInfoProvider?.GetPluginDetail(moduleName);
                    await context.Response.WriteAsync(JsonResponse(detail));
                });
            });

            // Plugin configs
            app.Map("/api/plugins/configs", pluginConfigsApp =>
            {
                pluginConfigsApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var moduleName = context.Request.Query["name"].ToString();
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "Module name required"));
                        return;
                    }

                    var configs = _pluginInfoProvider?.GetPluginConfigs(moduleName) ?? new List<PluginConfigInfo>();
                    await context.Response.WriteAsync(JsonResponse(configs));
                });
            });

            app.Map("/api/plugins/config", pluginConfigApp =>
            {
                pluginConfigApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var moduleName = context.Request.Query["module"].ToString();
                    var configName = context.Request.Query["config"].ToString();
                    
                    if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(configName))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "Module name and config name required"));
                        return;
                    }

                    if (context.Request.Method == "GET")
                    {
                        var config = _pluginInfoProvider?.GetPluginConfig(moduleName, configName);
                        await context.Response.WriteAsync(JsonResponse(config));
                    }
                    else if (context.Request.Method == "POST")
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();
                        var config = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
                        
                        if (config == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "Invalid config data"));
                            return;
                        }

                        var success = _pluginInfoProvider?.SavePluginConfig(moduleName, configName, config) ?? false;
                        await context.Response.WriteAsync(JsonResponse(success));
                    }
                    else
                    {
                        context.Response.StatusCode = 405;
                    }
                });
            });

            // /api/plugins 必须放在最后，因为 Map 是前缀匹配
            app.Map("/api/plugins", pluginsApp =>
            {
                pluginsApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var plugins = _pluginInfoProvider?.GetPlugins() ?? new List<PluginInfo>();
                    await context.Response.WriteAsync(JsonResponse(plugins));
                });
            });
        }

        private async Task<bool> CheckApiAuthAsync(HttpContext context)
        {
            var sessionToken = context.Request.Cookies["session_token"];
            var authHeader = context.Request.Headers["Authorization"].ToString();
            
            bool isAuthenticated = false;
            if (!string.IsNullOrEmpty(sessionToken) && _accountService.ValidateToken(sessionToken))
            {
                isAuthenticated = true;
            }
            else if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring(7);
                if (_accountService.ValidateToken(token))
                {
                    isAuthenticated = true;
                }
            }

            if (!isAuthenticated)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync(JsonResponse(null, "Unauthorized", 401));
                return false;
            }

            return true;
        }

        private bool IsLocalRequest(string remoteIp)
        {
            return remoteIp == "127.0.0.1" || 
                   remoteIp == "::1" || 
                   remoteIp == "localhost" ||
                   remoteIp.StartsWith("::ffff:127.");
        }

        private bool ValidateHashLogin(string hash)
        {
            try
            {
                var expectedHash = ComputeSha256Hash(_accountService.GetPassword() + ".napcat");
                return hash == expectedHash;
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeSha256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private string JsonResponse(object? data, string? message = null, int code = 0)
        {
            var response = new Dictionary<string, object?>
            {
                { "code", code },
                { "data", data },
                { "message", message ?? "" }
            };
            
            return JsonSerializer.Serialize(response, _jsonOptions);
        }

        private string GetEmbeddedResource(string resourceName)
        {
            var assembly = typeof(WebUIServer).Assembly;
            var fullResourceName = $"MorningCat.WebUI.wwwroot.{resourceName}";
            
            using var stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
            {
                throw new FileNotFoundException($"Embedded resource not found: {fullResourceName}");
            }
            
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
