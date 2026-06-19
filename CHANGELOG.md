# Changelog

All notable changes to MorningCat will be documented in this file.

---

## v6.0.0

### New Features
- **Config Backup/Restore**: WebUI now supports exporting and importing configuration as zip archive / WebUI 新增配置备份导出与导入功能
- **Bot Self-Permission**: Bot account now has owner-level permission when executing commands / 机器人自身执行命令时等同于持有者权限

### Improvements
- **Startup i18n**: Initialize i18n default locale (en) before logging to prevent raw translation keys in output / 启动时先初始化国际化默认值，避免日志打印翻译键
- **Remove redundant Log.Name()**: Clean up duplicate `Log.Name("MorningCat")` calls in exception handlers / 移除异常处理器中冗余的 Log.Name 调用
- **WebUI Backup Tab**: Add backup configuration tab in WebUI settings page / WebUI 配置页新增备份标签页

### Cleanup
- **Remove NC Legacy**: Delete `bypass.tsx` (NapCat anti-detect config) and `napcat_conf.d.ts` type definitions / 删除 NapCat 遗留的反检测配置页和类型定义
- **i18n Key Cleanup**: Remove 74 unused translation keys from zh.yml and en.yml / 清理 74 个未使用的翻译键
- **Documentation Update**: Sync MCT startup flow documentation with i18n initialization and signature verification / 同步启动流程文档，补充国际化初始化与签名验证

---

## v5.0.5

### Improvements
- **i18n Refactoring**: Migrate hardcoded Chinese strings to i18n translation keys / 全后端硬编码中文迁移至i18n翻译键
- **Log.Name Standardization**: Replace manual Log prefixes with `Log.Name()` / 所有Log手动前缀改为Log.Name()
- **ModuleBase Removal**: Remove ModuleBase inheritance, use new module system / 移除所有插件ModuleBase继承，改用新版模块系统
- **Plugin Compatibility**: Fix PetPet, ErrorMinefieldPlugin, PluginHost(PPC), GroupVerificationPlugin / 修复四个插件兼容性
- **Frontend i18n**: WebUI i18n improvements and enhancements / 前端i18n完善、WebUI功能增强
