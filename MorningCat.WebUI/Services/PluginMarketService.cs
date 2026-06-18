using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Logging;
using MorningCat.I18n;

namespace MorningCat.WebUI.Services;

public class PluginMarketService
{
    private readonly HttpClient _httpClient;
    private string _marketBaseUrl;
    private readonly string _defaultMarketBaseUrl = "https://110.42.98.47:55000";
    private readonly JsonSerializerOptions _jsonOptions;

    public PluginMarketService(string? marketUrl = null)
    {
        _marketBaseUrl = string.IsNullOrWhiteSpace(marketUrl) ? _defaultMarketBaseUrl : marketUrl.TrimEnd('/');
        Log.Debug(I18nManager.S("webui.market.init", marketUrl, _marketBaseUrl));
        
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public void UpdateMarketUrl(string? marketUrl)
    {
        var oldUrl = _marketBaseUrl;
        _marketBaseUrl = string.IsNullOrWhiteSpace(marketUrl) ? _defaultMarketBaseUrl : marketUrl.TrimEnd('/');
        Log.Debug(I18nManager.S("webui.market.url_updated", oldUrl, _marketBaseUrl, marketUrl));
    }

    public async Task<List<MarketPluginItem>?> GetPluginListAsync()
    {
        try
        {
            var url = $"{_marketBaseUrl}/api/plugin/list";
            Log.Debug(I18nManager.S("webui.market.request_list", url));
            var response = await _httpClient.GetAsync(url);
            Log.Debug(I18nManager.S("webui.market.list_response", response.StatusCode));
            if (!response.IsSuccessStatusCode) return null;
            
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<List<MarketPluginItem>>(content, _jsonOptions);
            Log.Debug(I18nManager.S("webui.market.parsed_count", result?.Count ?? 0));
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(I18nManager.S("webui.market.get_list_failed", ex.Message));
            return null;
        }
    }

    public async Task<MarketPluginDetail?> GetPluginDetailAsync(string pluginId)
    {
        try
        {
            var url = $"{_marketBaseUrl}/api/plugin/getdata/{pluginId}";
            Log.Debug(I18nManager.S("webui.market.request_detail", url));
            var response = await _httpClient.GetAsync(url);
            Log.Debug(I18nManager.S("webui.market.detail_response", response.StatusCode));
            if (!response.IsSuccessStatusCode) return null;
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MarketPluginDetail>(content, _jsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warning(I18nManager.S("webui.market.get_detail_failed", ex.Message));
            return null;
        }
    }

    public async Task<byte[]?> DownloadPluginDllAsync(string pluginId)
    {
        try
        {
            var url = $"{_marketBaseUrl}/api/plugin/setup/{pluginId}";
            Log.Debug(I18nManager.S("webui.market.download_dll", url));
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(I18nManager.S("webui.market.download_plugin_failed", ex.Message));
            return null;
        }
    }

    public async Task<byte[]?> DownloadDependencyAsync(string pluginId, string fileName)
    {
        try
        {
            var url = $"{_marketBaseUrl}/api/plugin/dependency/{pluginId}/{fileName}";
            Log.Debug(I18nManager.S("webui.market.download_dep", url));
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(I18nManager.S("webui.market.download_dep_failed", ex.Message));
            return null;
        }
    }

    public string GetFullIconUrl(string? iconUrl)
    {
        if (string.IsNullOrEmpty(iconUrl))
            return string.Empty;
        
        if (iconUrl.StartsWith("http://") || iconUrl.StartsWith("https://"))
            return iconUrl;
        
        return $"{_marketBaseUrl}{iconUrl}";
    }

    public HttpClient GetHttpClient() => _httpClient;
}

public class MarketPluginItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
    
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";
    
    [JsonPropertyName("authorUuid")]
    public string? AuthorUuid { get; set; }
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
    
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }
    
    [JsonPropertyName("website")]
    public string? Website { get; set; }
    
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public class MarketPluginDetail
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
    
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";
    
    [JsonPropertyName("authorUuid")]
    public string? AuthorUuid { get; set; }
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
    
    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }
    
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }
    
    [JsonPropertyName("website")]
    public string? Website { get; set; }
    
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();
    
    [JsonPropertyName("nugetDependencies")]
    public List<string> NugetDependencies { get; set; } = new();
    
    [JsonPropertyName("libraryDependencies")]
    public List<MarketLibraryDependency> LibraryDependencies { get; set; } = new();
    
    [JsonPropertyName("hasDll")]
    public bool HasDll { get; set; }
    
    [JsonPropertyName("dllSize")]
    public long DllSize { get; set; }
    
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public class MarketLibraryDependency
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
    
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }
    
    [JsonPropertyName("size")]
    public long Size { get; set; }
}
