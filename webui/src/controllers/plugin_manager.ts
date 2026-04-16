import { serverRequest } from '@/utils/request';

/** 插件状态 */
export type PluginStatus = 'Running' | 'Error' | 'Unloaded' | 'Initializing' | 'Scanned' | 'Disabled';

/** 插件信息 */
export interface PluginItem {
  /** 模块名称 */
  moduleName: string;
  /** 显示名称 */
  displayName?: string;
  /** 作者 */
  author?: string;
  /** 描述 */
  description?: string;
  /** 状态 */
  status: PluginStatus;
  /** 是否内置 */
  isBuiltin: boolean;
  /** 程序集路径 */
  assemblyPath?: string;
  /** Base64 图标 */
  iconBase64?: string;
}

/** 插件详情 */
export interface PluginDetail {
  moduleName: string;
  displayName?: string;
  author?: string;
  description?: string;
  website?: string;
  status: string;
  isBuiltin: boolean;
  moduleType?: string;
  assemblyPath?: string;
  hasInstance: boolean;
  dependencies: string[];
  dependents: string[];
  /** Base64 图标 */
  iconBase64?: string;
}

/** 服务端响应 */
export interface ServerResponse<T> {
  code: number;
  message: string;
  data: T;
}

/**
 * 插件管理器 API
 */
export default class PluginManager {
  /**
   * 获取插件列表
   */
  public static async getPluginList (): Promise<{ plugins: PluginItem[]; pluginManagerNotFound: boolean; }> {
    try {
      const { data } = await serverRequest.get<ServerResponse<PluginItem[]>>('/plugins');
      console.log('[PluginManager] 获取插件列表:', data.data);
      return {
        plugins: data.data || [],
        pluginManagerNotFound: false,
      };
    } catch (e: any) {
      console.error('[PluginManager] 获取插件列表失败:', e);
      return {
        plugins: [],
        pluginManagerNotFound: true,
      };
    }
  }

  /**
   * 获取插件详情
   */
  public static async getPluginDetail (name: string): Promise<PluginDetail | null> {
    try {
      const { data } = await serverRequest.get<ServerResponse<PluginDetail | null>>(
        `/plugins/detail?name=${encodeURIComponent(name)}`
      );
      console.log('[PluginManager] 获取插件详情:', name, data.data);
      return data.data;
    } catch (e: any) {
      console.error('[PluginManager] 获取插件详情失败:', e);
      return null;
    }
  }

  /**
   * 禁用插件
   */
  public static async disablePlugin (name: string): Promise<boolean> {
    try {
      const { data } = await serverRequest.get<ServerResponse<boolean>>(
        `/plugins/disable?name=${encodeURIComponent(name)}`
      );
      console.log('[PluginManager] 禁用插件:', name, data.data);
      return data.data;
    } catch (e: any) {
      console.error('[PluginManager] 禁用插件失败:', e);
      return false;
    }
  }

  /**
   * 启用插件
   */
  public static async enablePlugin (name: string): Promise<boolean> {
    try {
      const { data } = await serverRequest.get<ServerResponse<boolean>>(
        `/plugins/enable?name=${encodeURIComponent(name)}`
      );
      console.log('[PluginManager] 启用插件:', name, data.data);
      return data.data;
    } catch (e: any) {
      console.error('[PluginManager] 启用插件失败:', e);
      return false;
    }
  }

  /**
   * 卸载插件
   */
  public static async unloadPlugin (name: string): Promise<boolean> {
    try {
      const { data } = await serverRequest.get<ServerResponse<boolean>>(
        `/plugins/unload?name=${encodeURIComponent(name)}`
      );
      console.log('[PluginManager] 卸载插件:', name, data.data);
      return data.data;
    } catch (e: any) {
      console.error('[PluginManager] 卸载插件失败:', e);
      return false;
    }
  }
}
