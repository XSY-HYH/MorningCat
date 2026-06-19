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
using Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MorningCat.I18n;
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
        private IMessageProvider? _messageProvider;
        private IDatabaseInfoProvider? _databaseInfoProvider;
        private IMessageSendProvider? _messageSendProvider;
        private II18nProvider? _i18nProvider;
        private Action? _updateCallback;
        private CancellationTokenSource? _shutdownCts;
        private PluginMarketService _pluginMarketService;
        
        public Action<string, string>? OnCredentialsChanged { get; set; }
        
        public int Port { get; private set; }
        public bool IsRunning => _host != null;

        public WebUIServer(string username = "admin", string password = "admin123")
        {
            _accountService = new AccountService(username, password);
            _accountService.CredentialsChanged += (user, pass) => OnCredentialsChanged?.Invoke(user, pass);
            _pluginMarketService = new PluginMarketService();
        }

        public void UpdatePluginMarketUrl(string? marketUrl)
        {
            Log.Debug($"[WebUIServer] UpdatePluginMarketUrl 被调用，传入值: '{marketUrl}'");
            _pluginMarketService.UpdateMarketUrl(marketUrl);
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

        public void SetMessageProvider(IMessageProvider provider)
        {
            _messageProvider = provider;
        }

        public void SetDatabaseInfoProvider(IDatabaseInfoProvider provider)
        {
            _databaseInfoProvider = provider;
        }

        public void SetMessageSendProvider(IMessageSendProvider provider)
        {
            _messageSendProvider = provider;
        }

        public void SetI18nProvider(II18nProvider provider)
        {
            _i18nProvider = provider;
        }

        public void SetConfigProvider(IConfigProvider configProvider)
        {
            _configProvider = configProvider;
        }

        public void SetUpdateCallback(Action? updateCallback)
        {
            _updateCallback = updateCallback;
        }

        public AccountService GetAccountService()
        {
            return _accountService;
        }

        public async Task StartAsync(int port = 8080, string listenAddress = "127.0.0.1")
        {
            if (_host != null)
                return;

            Port = port;
            _shutdownCts = new CancellationTokenSource();

            var isLocalOnly = listenAddress == "127.0.0.1" || listenAddress == "localhost" || listenAddress == "::1";

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
                    logging.AddFilter("Microsoft", Microsoft.Extensions.Logging.LogLevel.Warning);
                    logging.AddFilter("Microsoft.Hosting", Microsoft.Extensions.Logging.LogLevel.Warning);
                    logging.AddFilter("Microsoft.AspNetCore", Microsoft.Extensions.Logging.LogLevel.Warning);
                    logging.AddFilter("System", Microsoft.Extensions.Logging.LogLevel.Warning);
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
                        if (isLocalOnly)
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
                        }

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
                        if (listenAddress == "0.0.0.0")
                        {
                            options.ListenAnyIP(port);
                        }
                        else if (System.Net.IPAddress.TryParse(listenAddress, out var ipAddress))
                        {
                            options.Listen(ipAddress, port);
                        }
                        else
                        {
                            options.ListenLocalhost(port);
                        }
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
                        
                        var groups = new[]
                        {
                            new { key = "onebot", label = "webui.config.group.onebot", icon = "link" },
                            new { key = "core", label = "webui.config.group.core", icon = "settings" },
                            new { key = "permission", label = "webui.config.group.permission", icon = "shield" },
                            new { key = "webui", label = "webui.config.group.webui", icon = "globe" },
                            new { key = "i18n", label = "webui.config.group.i18n", icon = "languages" },
                            new { key = "database", label = "webui.config.group.database", icon = "database" },
                            new { key = "pluginstore", label = "webui.config.group.pluginstore", icon = "package" },
                            new { key = "backup", label = "webui.config.group.backup", icon = "archive" }
                        };

                        var items = new object[]
                        {
                            new { key = "onebotServerUrl", label = "webui.config.label.onebot_server_url", type = "string", group = "onebot", description = "webui.config.desc.onebot_server_url", placeholder = "ws://127.0.0.1:7892", required = true, min = (int?)null, max = (int?)null, value = config.OneBotServerUrl },
                            new { key = "onebotToken", label = "webui.config.label.onebot_token", type = "password", group = "onebot", description = "webui.config.desc.onebot_token", placeholder = (string?)null, required = false, min = (int?)null, max = (int?)null, value = config.OneBotToken },
                            new { key = "modulesDirectory", label = "webui.config.label.modules_directory", type = "string", group = "core", description = "webui.config.desc.modules_directory", placeholder = "Modules", required = false, min = (int?)null, max = (int?)null, value = config.ModulesDirectory },
                            new { key = "autoLoadModules", label = "webui.config.label.auto_load_modules", type = "boolean", group = "core", description = "webui.config.desc.auto_load_modules", placeholder = (string?)null, required = false, min = (int?)null, max = (int?)null, value = config.AutoLoadModules },
                            new { key = "enableMctStatus", label = "webui.config.label.enable_mct_status", type = "boolean", group = "core", description = "webui.config.desc.enable_mct_status", placeholder = (string?)null, required = false, min = (int?)null, max = (int?)null, value = config.EnableMctStatus },
                            new { key = "ownerQQ", label = "webui.config.label.owner_qq", type = "number", group = "permission", description = "webui.config.desc.owner_qq", placeholder = "0", required = false, min = (int?)0, max = (int?)null, value = config.OwnerQQ },
                            new { key = "blockedUsers", label = "webui.config.label.blocked_users", type = "number_array", group = "permission", description = "webui.config.desc.blocked_users", placeholder = (string?)null, required = false, min = (int?)null, max = (int?)null, value = config.BlockedUsers },
                            new { key = "blockedGroups", label = "webui.config.label.blocked_groups", type = "number_array", group = "permission", description = "webui.config.desc.blocked_groups", placeholder = (string?)null, required = false, min = (int?)null, max = (int?)null, value = config.BlockedGroups },
                            new { key = "lang", label = "webui.config.label.lang", type = "select", group = "i18n", description = "webui.config.desc.lang", placeholder = "zh", required = true, min = (int?)null, max = (int?)null, value = config.Lang, options = _i18nProvider?.GetAvailableLanguages() ?? new List<string> { "zh", "en" } },
                            new { key = "database.type", label = "webui.config.label.database_type", type = "select", group = "database", description = "webui.config.desc.database_type", placeholder = "sqlite", required = false, min = (int?)null, max = (int?)null, value = config.Database?.Type ?? "sqlite", options = new[] { "sqlite", "sql" } },
                            new { key = "database.connectionString", label = "webui.config.label.database_connection_string", type = "string", group = "database", description = "webui.config.desc.database_connection_string", placeholder = (string?)null, required = false, min = (int?)null, max = (int?)null, value = config.Database?.ConnectionString ?? "" },
                            new { key = "pluginStoreUrl", label = "webui.config.label.plugin_store_url", type = "string", group = "pluginstore", description = "webui.config.desc.plugin_store_url", placeholder = (string?)null, required = false, min = (int?)null, max = (int?)null, value = config.PluginStoreUrl },
                            new { key = "webui.enabled", label = "webui.config.label.webui_enabled", type = "boolean", group = "webui", description = "webui.config.desc.webui_enabled", placeholder = (string?)null, required = false, min = (int?)null, max = (int?)null, value = config.WebUI.Enabled },
                            new { key = "webui.listenAddress", label = "webui.config.label.webui_listen_address", type = "string", group = "webui", description = "webui.config.desc.webui_listen_address", placeholder = "127.0.0.1", required = false, min = (int?)null, max = (int?)null, value = config.WebUI.ListenAddress },
                            new { key = "webui.port", label = "webui.config.label.webui_port", type = "number", group = "webui", description = "webui.config.desc.webui_port", placeholder = "8080", required = false, min = (int?)1, max = (int?)65535, value = config.WebUI.Port },
                            new { key = "webui.username", label = "webui.config.label.webui_username", type = "string", group = "webui", description = "webui.config.desc.webui_username", placeholder = "admin", required = true, min = (int?)null, max = (int?)null, value = config.WebUI.Username },
                            new { key = "webui.password", label = "webui.config.label.webui_password", type = "password", group = "webui", description = "webui.config.desc.webui_password", placeholder = (string?)null, required = false, min = (int?)null, max = (int?)null, value = "" }
                        };

                        await context.Response.WriteAsync(JsonResponse(new { groups, items }));
                    }
                    else if (context.Request.Method == "POST")
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();
                        var json = JsonDocument.Parse(body);
                        
                        _configProvider?.UpdateConfig(config =>
                        {
                            if (json.RootElement.TryGetProperty("onebotServerUrl", out var urlEl))
                                config.OneBotServerUrl = urlEl.GetString() ?? config.OneBotServerUrl;
                            if (json.RootElement.TryGetProperty("onebotToken", out var tokenEl))
                                config.OneBotToken = tokenEl.GetString() ?? config.OneBotToken;
                            if (json.RootElement.TryGetProperty("modulesDirectory", out var dirEl))
                                config.ModulesDirectory = dirEl.GetString() ?? config.ModulesDirectory;
                            if (json.RootElement.TryGetProperty("autoLoadModules", out var autoEl))
                                config.AutoLoadModules = autoEl.GetBoolean();
                            if (json.RootElement.TryGetProperty("enableMctStatus", out var mctStatusEl))
                                config.EnableMctStatus = mctStatusEl.GetBoolean();
                            if (json.RootElement.TryGetProperty("ownerQQ", out var ownerEl))
                                config.OwnerQQ = ownerEl.GetInt64();
                            if (json.RootElement.TryGetProperty("blockedUsers", out var blockedUsersEl) && blockedUsersEl.ValueKind == JsonValueKind.Array)
                            {
                                config.BlockedUsers = blockedUsersEl.EnumerateArray()
                                    .Select(e => e.GetInt64())
                                    .ToList();
                            }
                            if (json.RootElement.TryGetProperty("blockedGroups", out var blockedGroupsEl) && blockedGroupsEl.ValueKind == JsonValueKind.Array)
                            {
                                config.BlockedGroups = blockedGroupsEl.EnumerateArray()
                                    .Select(e => e.GetInt64())
                                    .ToList();
                            }
                            if (json.RootElement.TryGetProperty("lang", out var langEl))
                                config.Lang = langEl.GetString() ?? config.Lang;
                            if (json.RootElement.TryGetProperty("database", out var dbEl))
                            {
                                if (dbEl.TryGetProperty("type", out var dbTypeEl))
                                    config.Database.Type = dbTypeEl.GetString() ?? config.Database.Type;
                                if (dbEl.TryGetProperty("connectionString", out var dbConnEl))
                                    config.Database.ConnectionString = dbConnEl.GetString() ?? config.Database.ConnectionString;
                            }
                            if (json.RootElement.TryGetProperty("pluginStoreUrl", out var storeUrlEl))
                                config.PluginStoreUrl = storeUrlEl.GetString() ?? "";
                            if (json.RootElement.TryGetProperty("webui", out var webuiEl))
                            {
                                if (webuiEl.TryGetProperty("enabled", out var enabledEl))
                                    config.WebUI.Enabled = enabledEl.GetBoolean();
                                if (webuiEl.TryGetProperty("listenAddress", out var listenEl))
                                    config.WebUI.ListenAddress = listenEl.GetString() ?? config.WebUI.ListenAddress;
                                if (webuiEl.TryGetProperty("port", out var portEl))
                                    config.WebUI.Port = portEl.GetInt32();
                                if (webuiEl.TryGetProperty("username", out var userEl))
                                    config.WebUI.Username = userEl.GetString() ?? config.WebUI.Username;
                                if (webuiEl.TryGetProperty("password", out var passEl) && !string.IsNullOrEmpty(passEl.GetString()))
                                    config.WebUI.Password = passEl.GetString()!;
                            }
                        });
                        
                        await context.Response.WriteAsync(JsonResponse(true, I18nManager.S("webui.config_saved")));
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
            app.Map("/api/base/GetOneBotVersion", versionApp =>
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

            app.Map("/api/i18n/translations", i18nApp =>
            {
                i18nApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;

                    var lang = context.Request.Query["lang"].ToString();
                    Dictionary<string, string>? allTranslations;

                    if (!string.IsNullOrEmpty(lang) && lang != _i18nProvider?.CurrentLang)
                    {
                        allTranslations = _i18nProvider?.GetTranslationsForLang(lang);
                    }
                    else
                    {
                        allTranslations = _i18nProvider?.GetTranslations();
                    }

                    // 只返回 webui. 前缀的翻译给前端
                    var webuiTranslations = allTranslations?
                        .Where(kv => kv.Key.StartsWith("webui."))
                        .ToDictionary(kv => kv.Key, kv => kv.Value)
                        ?? new Dictionary<string, string>();

                    await context.Response.WriteAsync(JsonResponse(new
                    {
                        lang = _i18nProvider?.CurrentLang ?? "zh",
                        translations = webuiTranslations
                    }));
                });
            });

            app.Map("/api/i18n/languages", i18nApp =>
            {
                i18nApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;

                    await context.Response.WriteAsync(JsonResponse(new
                    {
                        current = _i18nProvider?.CurrentLang ?? "zh",
                        available = _i18nProvider?.GetAvailableLanguages() ?? new List<string>()
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
                        
                        using var doc = JsonDocument.Parse(content);
                        var remoteVersions = new List<string>();
                        
                        if (doc.RootElement.TryGetProperty("directories", out var dirs))
                        {
                            foreach (var dir in dirs.EnumerateArray())
                            {
                                if (dir.TryGetProperty("name", out var nameEl))
                                {
                                    var name = nameEl.GetString();
                                    if (!string.IsNullOrEmpty(name))
                                        remoteVersions.Add(name);
                                }
                            }
                        }
                        
                        var latestRemote = remoteVersions
                            .OrderByDescending(v => v, new VersionStringComparer())
                            .FirstOrDefault();
                        
                        var info = _systemInfoProvider?.GetSystemInfo();
                        var localVersion = info?.Version ?? "0.0.0";
                        var normalizedRemote = latestRemote?.TrimStart('v') ?? "";
                        var normalizedLocal = localVersion.TrimStart('v');
                        
                        bool hasUpdate = false;
                        if (!string.IsNullOrEmpty(normalizedRemote))
                        {
                            hasUpdate = new VersionStringComparer().Compare(normalizedRemote, normalizedLocal) > 0;
                        }
                        
                        await context.Response.WriteAsync(JsonResponse(new
                        {
                            success = true,
                            local_version = localVersion,
                            remote_version = latestRemote ?? "",
                            has_update = hasUpdate
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

            app.Map("/api/base/PerformUpdate", performUpdateApp =>
            {
                performUpdateApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    if (context.Request.Method != "POST")
                    {
                        context.Response.StatusCode = 405;
                        return;
                    }
                    
                    try
                    {
                        if (_updateCallback == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(new
                            {
                                success = false,
                                message = "Update callback not available"
                            }));
                            return;
                        }
                        
                        _updateCallback();
                        
                        await context.Response.WriteAsync(JsonResponse(new
                        {
                            success = true,
                            message = "Restart signal sent"
                        }));
                    }
                    catch (Exception ex)
                    {
                        await context.Response.WriteAsync(JsonResponse(new
                        {
                            success = false,
                            message = $"Failed to trigger update: {ex.Message}"
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

            // Messages SSE
            app.Map("/api/messages/realtime", messagesRealtimeApp =>
            {
                messagesRealtimeApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;

                    context.Response.StatusCode = 200;
                    context.Response.Headers["Content-Type"] = "text/event-stream";
                    context.Response.Headers["Cache-Control"] = "no-cache";
                    context.Response.Headers["Connection"] = "keep-alive";

                    var connectedMsg = new MessageEntry
                    {
                        GroupName = "MctCore",
                        SenderName = "核心",
                        Content = "sse成功",
                        MessageType = "system",
                        UserId = 0,
                        GroupId = null,
                        Time = DateTime.Now,
                        HasUnsupportedContent = false
                    };
                    var connectedJson = JsonSerializer.Serialize(connectedMsg, _jsonOptions);
                    await context.Response.WriteAsync($"data: {connectedJson}\n\n");
                    await context.Response.Body.FlushAsync();

                    var tcs = new TaskCompletionSource<bool>();
                    Action<MessageEntry>? messageCallback = null;
                    var shutdownToken = _shutdownCts?.Token ?? CancellationToken.None;
                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, context.RequestAborted);

                    try
                    {
                        var recentMessages = _messageProvider?.GetRecentMessages(50) ?? new List<MessageEntry>();
                        foreach (var msg in recentMessages)
                        {
                            var json = JsonSerializer.Serialize(msg, _jsonOptions);
                            await context.Response.WriteAsync($"data: {json}\n\n", linkedCts.Token);
                            await context.Response.Body.FlushAsync(linkedCts.Token);
                        }

                        messageCallback = (entry) =>
                        {
                            try
                            {
                                if (linkedCts.Token.IsCancellationRequested)
                                {
                                    tcs.TrySetResult(true);
                                    return;
                                }
                                var json = JsonSerializer.Serialize(entry, _jsonOptions);
                                var bytes = System.Text.Encoding.UTF8.GetBytes($"data: {json}\n\n");
                                context.Response.Body.WriteAsync(bytes, 0, bytes.Length, linkedCts.Token).Wait();
                                context.Response.Body.FlushAsync(linkedCts.Token).Wait();
                            }
                            catch
                            {
                                tcs.TrySetResult(true);
                            }
                        };

                        _messageProvider?.SubscribeToMessages(messageCallback);

                        await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
                    }
                    finally
                    {
                        linkedCts.Dispose();
                        if (messageCallback != null)
                        {
                            _messageProvider?.UnsubscribeFromMessages(messageCallback);
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

            app.Map("/api/plugins/delete", deletePluginApp =>
            {
                deletePluginApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var moduleName = context.Request.Query["name"].ToString();
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        await context.Response.WriteAsync(JsonResponse(false, "Module name required"));
                        return;
                    }

                    var success = _pluginInfoProvider?.DeletePlugin(moduleName) ?? false;
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
                    Log.Debug($"[API] /api/plugins/configs 请求: moduleName='{moduleName}'");
                    
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "Module name required"));
                        return;
                    }

                    var configs = _pluginInfoProvider?.GetPluginConfigs(moduleName) ?? new List<PluginConfigInfo>();
                    Log.Debug($"[API] /api/plugins/configs 返回 {configs.Count} 个配置");
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
                    Log.Debug($"[API] /api/plugins/config 请求: module='{moduleName}', config='{configName}', method={context.Request.Method}");
                    
                    if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(configName))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "Module name and config name required"));
                        return;
                    }

                    if (context.Request.Method == "GET")
                    {
                        var config = _pluginInfoProvider?.GetPluginConfig(moduleName, configName);
                        Log.Debug($"[API] /api/plugins/config GET 返回: {(config != null ? $"{config.Count} 个键" : "null")}");
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
                        Log.Debug($"[API] /api/plugins/config POST 保存结果: {success}");
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

            // Plugin Market APIs
            app.Map("/api/market/list", marketListApp =>
            {
                marketListApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var plugins = await _pluginMarketService.GetPluginListAsync();
                    if (plugins == null)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "无法连接到插件市场", 500));
                        return;
                    }
                    await context.Response.WriteAsync(JsonResponse(plugins));
                });
            });

            app.Map("/api/market/detail", marketDetailApp =>
            {
                marketDetailApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    var pluginId = context.Request.Query["id"].ToString();
                    if (string.IsNullOrEmpty(pluginId))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "Plugin ID required"));
                        return;
                    }
                    
                    var detail = await _pluginMarketService.GetPluginDetailAsync(pluginId);
                    if (detail == null)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "插件不存在", 404));
                        return;
                    }
                    await context.Response.WriteAsync(JsonResponse(detail));
                });
            });

            app.Map("/api/market/install", marketInstallApp =>
            {
                marketInstallApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    if (context.Request.Method != "POST")
                    {
                        context.Response.StatusCode = 405;
                        return;
                    }
                    
                    try
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();
                        var json = JsonDocument.Parse(body);
                        
                        if (!json.RootElement.TryGetProperty("pluginId", out var idEl))
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "Plugin ID required"));
                            return;
                        }
                        
                        var pluginId = idEl.GetString() ?? "";
                        if (string.IsNullOrEmpty(pluginId))
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "Plugin ID required"));
                            return;
                        }
                        
                        var detail = await _pluginMarketService.GetPluginDetailAsync(pluginId);
                        if (detail == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "插件不存在或无法连接到插件市场", 404));
                            return;
                        }
                        
                        var installedPlugins = _pluginInfoProvider?.GetPlugins() ?? new List<PluginInfo>();
                        var installedPlugin = installedPlugins.FirstOrDefault(p => p.ModuleName == detail.Id || p.DisplayName == detail.Name);
                        if (installedPlugin != null)
                        {
                            await context.Response.WriteAsync(JsonResponse(new
                            {
                                success = false,
                                alreadyInstalled = true,
                                pluginName = detail.Name,
                                installedVersion = "",
                                message = $"插件 {detail.Name} 已安装，如需更新请先卸载后重新安装"
                            }));
                            return;
                        }
                        
                        var modulesDir = _configProvider?.GetConfig()?.ModulesDirectory ?? "Modules";
                        if (!Path.IsPathRooted(modulesDir))
                        {
                            modulesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modulesDir);
                        }
                        if (!Directory.Exists(modulesDir))
                        {
                            Directory.CreateDirectory(modulesDir);
                        }
                        
                        var dllPath = Path.Combine(modulesDir, detail.Id + ".dll");
                        var disabledPath = dllPath + ".disabled";
                        if (File.Exists(dllPath) || File.Exists(disabledPath))
                        {
                            await context.Response.WriteAsync(JsonResponse(new
                            {
                                success = false,
                                alreadyInstalled = true,
                                pluginName = detail.Name,
                                installedVersion = "",
                                message = $"插件 {detail.Name} 文件已存在，如需重新安装请先卸载"
                            }));
                            return;
                        }
                        
                        var libraryDir = Path.Combine(modulesDir, "Library");
                        if (!Directory.Exists(libraryDir))
                        {
                            Directory.CreateDirectory(libraryDir);
                        }
                        
                        var warnings = new List<string>();
                        
                        if (detail.Dependencies != null && detail.Dependencies.Count > 0)
                        {
                            warnings.Add($"此插件依赖其他插件: {string.Join(", ", detail.Dependencies)}，请手动安装这些前置插件");
                        }
                        
                        if (detail.NugetDependencies != null && detail.NugetDependencies.Count > 0)
                        {
                            warnings.Add($"此插件需要NuGet包: {string.Join(", ", detail.NugetDependencies)}，将自动还原");
                            
                            foreach (var nugetPkg in detail.NugetDependencies)
                            {
                                var nugetResult = await RestoreNuGetPackageAsync(nugetPkg, libraryDir);
                                if (!nugetResult)
                                {
                                    warnings.Add($"NuGet包 {nugetPkg} 还原失败，请手动安装");
                                }
                            }
                        }
                        
                        if (detail.LibraryDependencies != null && detail.LibraryDependencies.Count > 0)
                        {
                            foreach (var lib in detail.LibraryDependencies)
                            {
                                var libData = await _pluginMarketService.DownloadDependencyAsync(pluginId, lib.FileName);
                                if (libData != null)
                                {
                                    var libPath = Path.Combine(libraryDir, lib.FileName);
                                    await File.WriteAllBytesAsync(libPath, libData);
                                }
                                else
                                {
                                    warnings.Add($"依赖库 {lib.FileName} 下载失败");
                                }
                            }
                        }
                        
                        var dllData = await _pluginMarketService.DownloadPluginDllAsync(pluginId);
                        if (dllData == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "插件DLL下载失败", 500));
                            return;
                        }
                        
                        await File.WriteAllBytesAsync(dllPath, dllData);
                        
                        var result = new
                        {
                            success = true,
                            pluginName = detail.Name,
                            warnings = warnings
                        };
                        
                        await context.Response.WriteAsync(JsonResponse(result));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[Market] 安装插件失败: {ex.Message}");
                        await context.Response.WriteAsync(JsonResponse(null, $"安装失败: {ex.Message}", 500));
                    }
                });
            });

            app.Map("/api/market/update", marketUpdateApp =>
            {
                marketUpdateApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    
                    if (context.Request.Method != "POST")
                    {
                        context.Response.StatusCode = 405;
                        return;
                    }
                    
                    try
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();
                        var json = JsonDocument.Parse(body);
                        
                        if (!json.RootElement.TryGetProperty("pluginId", out var idEl))
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "Plugin ID required"));
                            return;
                        }
                        
                        var pluginId = idEl.GetString() ?? "";
                        if (string.IsNullOrEmpty(pluginId))
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "Plugin ID required"));
                            return;
                        }
                        
                        var detail = await _pluginMarketService.GetPluginDetailAsync(pluginId);
                        if (detail == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "插件不存在或无法连接到插件市场", 404));
                            return;
                        }
                        
                        var installedPlugins = _pluginInfoProvider?.GetPlugins() ?? new List<PluginInfo>();
                        var installedPlugin = installedPlugins.FirstOrDefault(p => p.ModuleName == detail.Id || p.DisplayName == detail.Name);
                        if (installedPlugin == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "插件未安装，无法更新", 400));
                            return;
                        }
                        
                        Log.Debug($"[Market] 开始更新插件 {detail.Name}...");
                        
                        if (installedPlugin.Status == "Running")
                        {
                            var unloadSuccess = _pluginInfoProvider?.UnloadPlugin(installedPlugin.ModuleName) ?? false;
                            if (!unloadSuccess)
                            {
                                await context.Response.WriteAsync(JsonResponse(null, "卸载旧版本失败，请手动卸载后重新安装", 500));
                                return;
                            }
                            Log.Debug($"[Market] 旧版本已卸载");
                        }
                        
                        var modulesDir = _configProvider?.GetConfig()?.ModulesDirectory ?? "Modules";
                        if (!Path.IsPathRooted(modulesDir))
                        {
                            modulesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modulesDir);
                        }
                        if (!Directory.Exists(modulesDir))
                        {
                            Directory.CreateDirectory(modulesDir);
                        }
                        
                        var dllPath = Path.Combine(modulesDir, detail.Id + ".dll");
                        
                        var libraryDir = Path.Combine(modulesDir, "Library");
                        if (!Directory.Exists(libraryDir))
                        {
                            Directory.CreateDirectory(libraryDir);
                        }
                        
                        var warnings = new List<string>();
                        
                        if (detail.Dependencies != null && detail.Dependencies.Count > 0)
                        {
                            warnings.Add($"此插件依赖其他插件: {string.Join(", ", detail.Dependencies)}，请手动安装这些前置插件");
                        }
                        
                        if (detail.NugetDependencies != null && detail.NugetDependencies.Count > 0)
                        {
                            warnings.Add($"此插件需要NuGet包: {string.Join(", ", detail.NugetDependencies)}，将自动还原");
                            
                            foreach (var nugetPkg in detail.NugetDependencies)
                            {
                                var nugetResult = await RestoreNuGetPackageAsync(nugetPkg, libraryDir);
                                if (!nugetResult)
                                {
                                    warnings.Add($"NuGet包 {nugetPkg} 还原失败，请手动安装");
                                }
                            }
                        }
                        
                        if (detail.LibraryDependencies != null && detail.LibraryDependencies.Count > 0)
                        {
                            warnings.Add($"此插件需要依赖库: {string.Join(", ", detail.LibraryDependencies.Select(l => l.FileName))}，将自动下载");
                            
                            foreach (var libDep in detail.LibraryDependencies)
                            {
                                var libResult = await _pluginMarketService.DownloadDependencyAsync(pluginId, libDep.FileName);
                                if (libResult != null)
                                {
                                    var libPath = Path.Combine(libraryDir, libDep.FileName);
                                    await File.WriteAllBytesAsync(libPath, libResult);
                                }
                                else
                                {
                                    warnings.Add($"依赖库 {libDep.FileName} 下载失败，请手动下载");
                                }
                            }
                        }
                        
                        var dllData = await _pluginMarketService.DownloadPluginDllAsync(pluginId);
                        if (dllData == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "插件DLL下载失败", 500));
                            return;
                        }
                        
                        await File.WriteAllBytesAsync(dllPath, dllData);
                        
                        Log.Info($"[Market] 插件 {detail.Name} 更新完成");
                        
                        var result = new
                        {
                            success = true,
                            pluginName = detail.Name,
                            newVersion = detail.Version,
                            warnings = warnings
                        };
                        
                        await context.Response.WriteAsync(JsonResponse(result));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[Market] 更新插件失败: {ex.Message}");
                        await context.Response.WriteAsync(JsonResponse(null, $"更新失败: {ex.Message}", 500));
                    }
                });
            });

            // Database API
            app.Map("/api/database/list", dbListApp =>
            {
                dbListApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;

                    var databases = _databaseInfoProvider?.GetDatabases() ?? new List<DatabaseEntryInfo>();
                    await context.Response.WriteAsync(JsonResponse(databases));
                });
            });

            app.Map("/api/database/detail", dbDetailApp =>
            {
                dbDetailApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;

                    var key = context.Request.Query["key"].ToString();
                    if (string.IsNullOrEmpty(key))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "缺少key参数", 400));
                        return;
                    }

                    var detail = _databaseInfoProvider?.GetDatabaseDetail(key);
                    if (detail == null)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "数据库不存在", 404));
                        return;
                    }

                    await context.Response.WriteAsync(JsonResponse(detail));
                });
            });

            app.Map("/api/database/query", dbQueryApp =>
            {
                dbQueryApp.Run(async context =>
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

                    if (!json.RootElement.TryGetProperty("key", out var keyEl) ||
                        !json.RootElement.TryGetProperty("sql", out var sqlEl))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "缺少key或sql参数", 400));
                        return;
                    }

                    var key = keyEl.GetString();
                    var sql = sqlEl.GetString();

                    try
                    {
                        var results = _databaseInfoProvider?.QueryDatabase(key!, sql!);
                        await context.Response.WriteAsync(JsonResponse(results));
                    }
                    catch (Exception ex)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, $"查询失败: {ex.Message}", 500));
                    }
                });
            });

            app.Map("/api/database/execute", dbExecuteApp =>
            {
                dbExecuteApp.Run(async context =>
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

                    if (!json.RootElement.TryGetProperty("key", out var keyEl) ||
                        !json.RootElement.TryGetProperty("sql", out var sqlEl))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "缺少key或sql参数", 400));
                        return;
                    }

                    var key = keyEl.GetString();
                    var sql = sqlEl.GetString();

                    try
                    {
                        var affected = _databaseInfoProvider?.ExecuteDatabaseNonQuery(key!, sql!) ?? 0;
                        await context.Response.WriteAsync(JsonResponse(new { affected }));
                    }
                    catch (Exception ex)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, $"执行失败: {ex.Message}", 500));
                    }
                });
            });

            // Message Send API
            app.Map("/api/message/send", msgSendApp =>
            {
                msgSendApp.Run(async context =>
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

                    if (!json.RootElement.TryGetProperty("type", out var typeEl) ||
                        !json.RootElement.TryGetProperty("target", out var targetEl) ||
                        !json.RootElement.TryGetProperty("message", out var msgEl))
                    {
                        await context.Response.WriteAsync(JsonResponse(null, "缺少type/target/message参数", 400));
                        return;
                    }

                    var type = typeEl.GetString();
                    var target = targetEl.GetInt64();
                    var message = msgEl.GetString();

                    try
                    {
                        bool success;
                        if (type == "private")
                        {
                            success = await (_messageSendProvider?.SendPrivateMessageAsync(target, message!) ?? Task.FromResult(false));
                        }
                        else if (type == "group")
                        {
                            success = await (_messageSendProvider?.SendGroupMessageAsync(target, message!) ?? Task.FromResult(false));
                        }
                        else
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "无效的type，仅支持private/group", 400));
                            return;
                        }

                        await context.Response.WriteAsync(JsonResponse(new { success }));
                    }
                    catch (Exception ex)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, $"发送失败: {ex.Message}", 500));
                    }
                });
            });

            app.Map("/api/message/group_list", groupListApp =>
            {
                groupListApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    var list = await (_messageSendProvider?.GetGroupListAsync() ?? Task.FromResult(new List<GroupInfo>()));
                    await context.Response.WriteAsync(JsonResponse(list));
                });
            });

            app.Map("/api/message/friend_list", friendListApp =>
            {
                friendListApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;
                    var list = await (_messageSendProvider?.GetFriendListAsync() ?? Task.FromResult(new List<FriendInfo>()));
                    await context.Response.WriteAsync(JsonResponse(list));
                });
            });

            // 备份导出
            app.Map("/api/backup/export", backupExportApp =>
            {
                backupExportApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;

                    try
                    {
                        var data = _configProvider?.ExportConfig();
                        if (data == null || data.Length == 0)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "Export failed", 500));
                            return;
                        }

                        var fileName = $"mct_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                        context.Response.ContentType = "application/zip";
                        context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
                        await context.Response.Body.WriteAsync(data);
                    }
                    catch (Exception ex)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, $"Export failed: {ex.Message}", 500));
                    }
                });
            });

            // 备份导入
            app.Map("/api/backup/import", backupImportApp =>
            {
                backupImportApp.Run(async context =>
                {
                    if (!await CheckApiAuthAsync(context)) return;

                    try
                    {
                        if (!context.Request.HasFormContentType)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "Expected multipart form data", 400));
                            return;
                        }

                        var form = await context.Request.ReadFormAsync();
                        var file = form.Files.FirstOrDefault();
                        if (file == null)
                        {
                            await context.Response.WriteAsync(JsonResponse(null, "No file uploaded", 400));
                            return;
                        }

                        using var stream = file.OpenReadStream();
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        var zipData = ms.ToArray();

                        var result = _configProvider?.ImportConfig(zipData);
                        await context.Response.WriteAsync(JsonResponse(new { message = $"Imported successfully. Restart required. Files: {result}" }));
                    }
                    catch (InvalidOperationException ex)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, ex.Message, 400));
                    }
                    catch (Exception ex)
                    {
                        await context.Response.WriteAsync(JsonResponse(null, $"Import failed: {ex.Message}", 500));
                    }
                });
            });
        }

        private async Task<bool> RestoreNuGetPackageAsync(string packageName, string libraryDir)
        {
            try
            {
                var expectedDllName = packageName + ".dll";
                var expectedDllPath = Path.Combine(libraryDir, expectedDllName);
                if (File.Exists(expectedDllPath))
                {
                    var delPath = expectedDllPath + ".del";
                    try
                    {
                        Log.Debug($"[PluginMarket] NuGet包 {packageName} 已存在，尝试重命名为 .del");
                        File.Move(expectedDllPath, delPath);
                    }
                    catch (Exception moveEx)
                    {
                        Log.Debug($"[PluginMarket] 重命名失败: {moveEx.Message}，跳过还原");
                        return true;
                    }
                }

                var lowerPkg = packageName.ToLowerInvariant();
                var registrationUrl = $"https://api.nuget.org/v3/registration5-semver2/{lowerPkg}/index.json";
                
                var regResponse = await _pluginMarketService.GetHttpClient().GetAsync(registrationUrl);
                if (!regResponse.IsSuccessStatusCode)
                {
                    Log.Warning($"[PluginMarket] NuGet包 {packageName} 查询注册信息失败: {regResponse.StatusCode}");
                    return false;
                }
                
                var regBytes = await regResponse.Content.ReadAsByteArrayAsync();
                string regContent;
                if (regBytes.Length > 2 && regBytes[0] == 0x1F && regBytes[1] == 0x8B)
                {
                    using var gzipStream = new System.IO.Compression.GZipStream(new MemoryStream(regBytes), System.IO.Compression.CompressionMode.Decompress);
                    using var reader = new StreamReader(gzipStream, Encoding.UTF8);
                    regContent = await reader.ReadToEndAsync();
                }
                else
                {
                    regContent = Encoding.UTF8.GetString(regBytes);
                }
                
                using var regDoc = System.Text.Json.JsonDocument.Parse(regContent);
                
                var items = regDoc.RootElement.GetProperty("items");
                if (items.GetArrayLength() == 0)
                {
                    Log.Warning($"[PluginMarket] NuGet包 {packageName} 无可用版本");
                    return false;
                }
                
                var latestPage = items[items.GetArrayLength() - 1];
                var pageItems = latestPage.TryGetProperty("items", out var innerItems) ? innerItems : default;
                
                string? contentUrl = null;
                if (pageItems.ValueKind == System.Text.Json.JsonValueKind.Array && pageItems.GetArrayLength() > 0)
                {
                    var latestItem = pageItems[pageItems.GetArrayLength() - 1];
                    if (latestItem.TryGetProperty("catalogEntry", out var catalogEntry))
                    {
                        contentUrl = catalogEntry.TryGetProperty("packageContent", out var pkgContent)
                            ? pkgContent.GetString() : null;
                    }
                }
                
                if (contentUrl == null && latestPage.TryGetProperty("catalogEntry", out var directEntry))
                {
                    contentUrl = directEntry.TryGetProperty("packageContent", out var pkgContent2)
                        ? pkgContent2.GetString() : null;
                }
                
                if (string.IsNullOrEmpty(contentUrl))
                {
                    Log.Warning($"[PluginMarket] NuGet包 {packageName} 未找到下载地址");
                    return false;
                }
                
                Log.Debug($"[PluginMarket] 正在下载 NuGet包 {packageName}...");
                var nupkgResponse = await _pluginMarketService.GetHttpClient().GetAsync(contentUrl);
                if (!nupkgResponse.IsSuccessStatusCode)
                {
                    Log.Warning($"[PluginMarket] NuGet包 {packageName} 下载失败: {nupkgResponse.StatusCode}");
                    return false;
                }
                
                var nupkgBytes = await nupkgResponse.Content.ReadAsByteArrayAsync();
                
                using var nupkgStream = new System.IO.MemoryStream(nupkgBytes);
                using var archive = new System.IO.Compression.ZipArchive(nupkgStream);
                
                var dllEntries = archive.Entries
                    .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                             && e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                var preferredDll = dllEntries.FirstOrDefault(e => e.FullName.Contains("net8.0"))
                    ?? dllEntries.FirstOrDefault(e => e.FullName.Contains("net6.0"))
                    ?? dllEntries.FirstOrDefault(e => e.FullName.Contains("netstandard2.1"))
                    ?? dllEntries.FirstOrDefault(e => e.FullName.Contains("netstandard2.0"))
                    ?? dllEntries.LastOrDefault();
                
                if (preferredDll == null)
                {
                    Log.Warning($"[PluginMarket] NuGet包 {packageName} 中未找到 DLL");
                    return false;
                }
                
                var destPath = Path.Combine(libraryDir, preferredDll.Name);
                using var dllStream = preferredDll.Open();
                using var fileStream = File.Create(destPath);
                await dllStream.CopyToAsync(fileStream);
                
                Log.Info($"[PluginMarket] NuGet包 {packageName} 还原成功: {preferredDll.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[PluginMarket] NuGet还原失败: {ex.Message}");
                return false;
            }
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

    public class VersionStringComparer : System.Collections.Generic.IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var xParts = x.Split('.', '-');
            var yParts = y.Split('.', '-');

            for (int i = 0; i < System.Math.Max(xParts.Length, yParts.Length); i++)
            {
                var xPart = i < xParts.Length ? xParts[i] : "0";
                var yPart = i < yParts.Length ? yParts[i] : "0";

                if (int.TryParse(xPart, out var xNum) && int.TryParse(yPart, out var yNum))
                {
                    if (xNum != yNum) return xNum.CompareTo(yNum);
                }
                else
                {
                    var cmp = string.Compare(xPart, yPart, System.StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }
            }

            return 0;
        }
    }
}
