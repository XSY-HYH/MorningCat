import { Button } from '@heroui/button';
import { Card, CardBody } from '@heroui/card';
import { Chip } from '@heroui/chip';
import { Divider } from '@heroui/divider';
import { Input } from '@heroui/input';
import { Listbox, ListboxItem } from '@heroui/listbox';
import { Modal, ModalContent, ModalHeader, ModalBody, ModalFooter } from '@heroui/modal';
import { ScrollShadow } from '@heroui/scroll-shadow';
import { Spinner } from '@heroui/spinner';
import { useLocalStorage } from '@uidotdev/usehooks';
import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';

import key from '@/const/key';

interface PluginInfo {
  moduleName: string;
  displayName: string | null;
  author: string | null;
  description: string | null;
  status: string;
  isBuiltin: boolean;
  iconBase64: string | null;
}

interface PluginConfigInfo {
  configName: string;
  filePath: string;
  lastModified: string;
  fileSize: number;
}

export default function PluginConfigPage () {
  const [plugins, setPlugins] = useState<PluginInfo[]>([]);
  const [selectedPlugin, setSelectedPlugin] = useState<string | null>(null);
  const [configs, setConfigs] = useState<PluginConfigInfo[]>([]);
  const [selectedConfig, setSelectedConfig] = useState<string | null>(null);
  const [configData, setConfigData] = useState<Record<string, unknown> | null>(null);
  const [loading, setLoading] = useState(true);
  const [configLoading, setConfigLoading] = useState(false);
  const [editModalOpen, setEditModalOpen] = useState(false);
  const [editConfig, setEditConfig] = useState<string>('');

  useEffect(() => {
    loadPlugins();
  }, []);

  useEffect(() => {
    if (selectedPlugin) {
      loadConfigs(selectedPlugin);
    } else {
      setConfigs([]);
      setSelectedConfig(null);
      setConfigData(null);
    }
  }, [selectedPlugin]);

  useEffect(() => {
    if (selectedPlugin && selectedConfig) {
      loadConfigData(selectedPlugin, selectedConfig);
    } else {
      setConfigData(null);
    }
  }, [selectedPlugin, selectedConfig]);

  const loadPlugins = async () => {
    try {
      const response = await fetch('/api/plugins');
      const result = await response.json();
      if (result.code === 0) {
        setPlugins(result.data || []);
        if (result.data && result.data.length > 0) {
          setSelectedPlugin(result.data[0].moduleName);
        }
      }
    } catch (error) {
      toast.error('加载插件列表失败');
    } finally {
      setLoading(false);
    }
  };

  const loadConfigs = async (moduleName: string) => {
    try {
      const response = await fetch(`/api/plugins/configs?name=${encodeURIComponent(moduleName)}`);
      const result = await response.json();
      if (result.code === 0) {
        setConfigs(result.data || []);
        if (result.data && result.data.length > 0) {
          setSelectedConfig(result.data[0].configName);
        } else {
          setSelectedConfig(null);
        }
      }
    } catch (error) {
      toast.error('加载配置列表失败');
    }
  };

  const loadConfigData = async (moduleName: string, configName: string) => {
    setConfigLoading(true);
    try {
      const response = await fetch(`/api/plugins/config?module=${encodeURIComponent(moduleName)}&config=${encodeURIComponent(configName)}`);
      const result = await response.json();
      if (result.code === 0) {
        setConfigData(result.data);
      }
    } catch (error) {
      toast.error('加载配置数据失败');
    } finally {
      setConfigLoading(false);
    }
  };

  const handleEditConfig = () => {
    if (configData) {
      setEditConfig(JSON.stringify(configData, null, 2));
      setEditModalOpen(true);
    }
  };

  const handleSaveConfig = async () => {
    if (!selectedPlugin || !selectedConfig) return;
    
    try {
      const parsed = JSON.parse(editConfig);
      const response = await fetch(`/api/plugins/config?module=${encodeURIComponent(selectedPlugin)}&config=${encodeURIComponent(selectedConfig)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(parsed),
      });
      const result = await response.json();
      if (result.code === 0) {
        toast.success('配置保存成功');
        setConfigData(parsed);
        setEditModalOpen(false);
      } else {
        toast.error(result.message || '保存失败');
      }
    } catch (error) {
      toast.error('JSON 格式错误');
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const formatDate = (dateStr: string) => {
    try {
      return new Date(dateStr).toLocaleString('zh-CN');
    } catch {
      return dateStr;
    }
  };

  const ConfigPageItem = ({ children, size = 'md' }: { children: React.ReactNode; size?: 'sm' | 'md' | 'lg' }) => {
    const [backgroundImage] = useLocalStorage<string>(key.backgroundImage, '');
    const hasBackground = !!backgroundImage;

    return (
      <Card className={`w-full mx-auto backdrop-blur-sm border border-white/40 dark:border-white/10 shadow-sm rounded-2xl transition-all ${hasBackground ? 'bg-white/20 dark:bg-black/10' : 'bg-white/60 dark:bg-black/40'} ${size === 'sm' ? 'max-w-xl' : size === 'md' ? 'max-w-3xl' : 'max-w-6xl'}`}>
        <CardBody className='py-6 px-4 md:py-8 md:px-12'>
          <div className='w-full flex flex-col gap-5'>
            {children}
          </div>
        </CardBody>
      </Card>
    );
  };

  if (loading) {
    return (
      <section className='w-full max-w-[1200px] mx-auto py-4 md:py-8 px-2 md:px-6 relative flex items-center justify-center min-h-[400px]'>
        <Spinner size='lg' />
      </section>
    );
  }

  return (
    <section className='w-full max-w-[1200px] mx-auto py-4 md:py-8 px-2 md:px-6 relative'>
      <title>插件配置 - MorningCat WebUI</title>
      
      <div className='flex flex-col md:flex-row gap-4'>
        <div className='w-full md:w-64 shrink-0'>
          <Card className='backdrop-blur-sm border border-white/40 dark:border-white/10 shadow-sm rounded-2xl bg-white/60 dark:bg-black/40'>
            <CardBody className='p-2'>
              <h3 className='text-sm font-semibold px-2 py-1 text-default-500'>插件列表</h3>
              <ScrollShadow className='h-[400px]'>
                <Listbox
                  aria-label='插件列表'
                  selectionMode='single'
                  selectedKeys={selectedPlugin ? new Set([selectedPlugin]) : new Set()}
                  onSelectionChange={(keys) => {
                    const selected = Array.from(keys)[0] as string;
                    setSelectedPlugin(selected);
                  }}
                >
                  {plugins.map((plugin) => (
                    <ListboxItem
                      key={plugin.moduleName}
                      textValue={plugin.displayName || plugin.moduleName}
                    >
                      <div className='flex items-center gap-2'>
                        <span className='truncate'>{plugin.displayName || plugin.moduleName}</span>
                        <Chip size='sm' variant='flat' color={plugin.status === 'Running' ? 'success' : plugin.status === 'Disabled' ? 'danger' : 'default'}>
                          {plugin.status}
                        </Chip>
                      </div>
                    </ListboxItem>
                  ))}
                </Listbox>
              </ScrollShadow>
            </CardBody>
          </Card>
        </div>

        <div className='flex-1 flex flex-col gap-4'>
          {selectedPlugin && (
            <>
              <Card className='backdrop-blur-sm border border-white/40 dark:border-white/10 shadow-sm rounded-2xl bg-white/60 dark:bg-black/40'>
                <CardBody className='p-2'>
                  <h3 className='text-sm font-semibold px-2 py-1 text-default-500'>配置文件</h3>
                  {configs.length > 0 ? (
                    <ScrollShadow className='h-[150px]'>
                      <Listbox
                        aria-label='配置列表'
                        selectionMode='single'
                        selectedKeys={selectedConfig ? new Set([selectedConfig]) : new Set()}
                        onSelectionChange={(keys) => {
                          const selected = Array.from(keys)[0] as string;
                          setSelectedConfig(selected);
                        }}
                      >
                        {configs.map((config) => (
                          <ListboxItem
                            key={config.configName}
                            textValue={config.configName}
                          >
                            <div className='flex items-center justify-between'>
                              <span>{config.configName}</span>
                              <span className='text-xs text-default-400'>{formatFileSize(config.fileSize)}</span>
                            </div>
                          </ListboxItem>
                        ))}
                      </Listbox>
                    </ScrollShadow>
                  ) : (
                    <div className='p-4 text-center text-default-400'>
                      该插件暂无配置文件
                    </div>
                  )}
                </CardBody>
              </Card>

              {selectedConfig && (
                <Card className='backdrop-blur-sm border border-white/40 dark:border-white/10 shadow-sm rounded-2xl bg-white/60 dark:bg-black/40 flex-1'>
                  <CardBody className='p-4'>
                    <div className='flex items-center justify-between mb-4'>
                      <h3 className='text-lg font-semibold'>{selectedConfig}</h3>
                      <Button color='primary' size='sm' onPress={handleEditConfig}>
                        编辑配置
                      </Button>
                    </div>
                    <Divider className='mb-4' />
                    {configLoading ? (
                      <div className='flex items-center justify-center h-[200px]'>
                        <Spinner />
                      </div>
                    ) : configData ? (
                      <pre className='bg-default-100 dark:bg-default-50/10 p-4 rounded-lg overflow-auto text-sm font-mono'>
                        {JSON.stringify(configData, null, 2)}
                      </pre>
                    ) : (
                      <div className='p-4 text-center text-default-400'>
                        无法加载配置数据
                      </div>
                    )}
                  </CardBody>
                </Card>
              )}
            </>
          )}
        </div>
      </div>

      <Modal isOpen={editModalOpen} onClose={() => setEditModalOpen(false)} size='3xl'>
        <ModalContent>
          <ModalHeader>编辑配置 - {selectedConfig}</ModalHeader>
          <ModalBody>
            <textarea
              value={editConfig}
              onChange={(e) => setEditConfig(e.target.value)}
              className='w-full h-[400px] font-mono text-sm p-4 bg-default-100 dark:bg-default-50/10 rounded-lg resize-none focus:outline-none focus:ring-2 focus:ring-primary'
              spellCheck={false}
            />
          </ModalBody>
          <ModalFooter>
            <Button variant='light' onPress={() => setEditModalOpen(false)}>
              取消
            </Button>
            <Button color='primary' onPress={handleSaveConfig}>
              保存
            </Button>
          </ModalFooter>
        </ModalContent>
      </Modal>
    </section>
  );
}
