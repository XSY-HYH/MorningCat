using Logging;
using MorningCat.Commands;
using MorningCat.Config;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat.Modules;

/// <summary>
/// Agent 模块 - 占位实现
/// 完整的 AI Agent 系统已移至独立项目 MorningCat.Agent，待后续集成
/// </summary>
public class AgentModule
{
    private MessageDistributionCore _mdc = null!;
    private CommandRegistry _commandRegistry = null!;
    private ConfigManager _configManager = null!;

    public void SetServices(MessageDistributionCore mdc, CommandRegistry commandRegistry, ConfigManager configManager)
    {
        _mdc = mdc;
        _commandRegistry = commandRegistry;
        _configManager = configManager;
    }

    public async Task InitAsync()
    {
        var config = _configManager.GetConfig().Agent;

        if (!config.Enabled)
        {
            Log.Info("Agent 模块未启用");
            return;
        }

        Log.Warning("Agent 模块已启用但 MorningCat.Agent 项目已暂时分离，请等待后续集成");
        await Task.CompletedTask;
    }

    public void UpdateMDC(MessageDistributionCore mdc)
    {
        _mdc = mdc;
    }
}
