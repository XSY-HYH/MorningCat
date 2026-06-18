import { useEffect, useState } from 'react';

export interface ConfigItem {
  key: string;
  label: string;
  type: 'string' | 'password' | 'number' | 'boolean' | 'number_array';
  group: string;
  description?: string;
  placeholder?: string;
  required?: boolean;
  min?: number;
  max?: number;
  value: any;
}

export interface ConfigGroup {
  key: string;
  label: string;
  icon: string;
}

/**
 * 从后端 /api/config 获取配置项（含值和元数据），前端动态渲染
 */
export default function useConfigSchema () {
  const [items, setItems] = useState<ConfigItem[]>([]);
  const [groups, setGroups] = useState<ConfigGroup[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadConfig();
  }, []);

  const loadConfig = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch('/api/config');
      const data = await res.json();

      if (data.code === 0 && data.data) {
        setItems(data.data.items || []);
        setGroups(data.data.groups || []);
      } else {
        setError('加载配置失败');
      }
    } catch (err) {
      setError('加载配置失败');
      console.error('加载配置失败:', err);
    } finally {
      setLoading(false);
    }
  };

  const updateConfig = async (values: Record<string, any>) => {
    try {
      const nested = unflattenConfig(values);
      const response = await fetch('/api/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(nested),
      });
      const result = await response.json();
      if (result.code === 0) {
        await loadConfig();
        return true;
      }
      return false;
    } catch (err) {
      console.error('保存配置失败:', err);
      return false;
    }
  };

  return { items, groups, loading, error, updateConfig, reload: loadConfig };
}

/**
 * 将扁平 key-value 还原为嵌套结构
 * { 'webui.enabled': true, 'webui.port': 8080 } => { webui: { enabled: true, port: 8080 } }
 */
function unflattenConfig (flat: Record<string, any>): Record<string, any> {
  const result: Record<string, any> = {};
  for (const [key, value] of Object.entries(flat)) {
    const parts = key.split('.');
    let current = result;
    for (let i = 0; i < parts.length - 1; i++) {
      if (!(parts[i] in current)) {
        current[parts[i]] = {};
      }
      current = current[parts[i]];
    }
    current[parts[parts.length - 1]] = value;
  }
  return result;
}
