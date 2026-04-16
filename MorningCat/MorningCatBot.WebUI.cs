using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Logging;
using MorningCat.Config;
using MorningCat.WebUI;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private WebUIManager? _webUIManager;
        
        public WebUIManager? WebUI => _webUIManager;
        
        public async Task StartWebUIAsync()
        {
            var webUIConfig = _configManager.GetConfig().WebUI;
            
            if (!webUIConfig.Enabled)
            {
                Log.Info("WebUI 已禁用");
                return;
            }
            
            if (_webUIManager == null)
            {
                _webUIManager = new WebUIManager(
                    _moduleManager, 
                    _pluginMetadata, 
                    webUIConfig, 
                    _configManager,
                    _assemblyNameToModuleName
                );
                _webUIManager.SetStartTime(DateTime.Now);
                _webUIManager.SetRestartCallback(RestartAsync);
                _webUIManager.SetShutdownCallback(RequestExit);
            }
            
            await _webUIManager.StartAsync();
        }
        
        public async Task StopWebUIAsync()
        {
            if (_webUIManager != null)
            {
                await _webUIManager.StopAsync();
            }
        }
        
        public string GetWebUILoginUrl()
        {
            return _webUIManager?.GetLoginUrl() ?? string.Empty;
        }
    }
}
