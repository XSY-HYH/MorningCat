import { Button } from '@heroui/button';
import { Card, CardBody, CardHeader } from '@heroui/card';
import { Chip } from '@heroui/chip';
import { Divider } from '@heroui/divider';
import { Input } from '@heroui/input';
import { Modal, ModalContent, ModalHeader, ModalBody, ModalFooter } from '@heroui/modal';
import { Spinner } from '@heroui/spinner';
import { Tooltip } from '@heroui/tooltip';
import { useEffect, useState, useCallback } from 'react';
import toast from 'react-hot-toast';
import { IoMdRefresh, IoMdSearch, IoMdDownload, IoMdInformationCircle, IoMdCloudUpload } from 'react-icons/io';
import clsx from 'clsx';
import key from '@/const/key';
import TailwindMarkdown from '@/components/tailwind_markdown';

interface MarketPluginItem {
  id: string;
  name: string;
  description: string;
  author: string;
  version: string;
  iconUrl?: string;
  tags?: string[];
  dependencies?: string[];
  nugetDependencies?: string[];
}

interface LibraryDependency {
  fileName: string;
  description: string;
  exists: boolean;
  size: number;
}

interface MarketPluginDetail extends MarketPluginItem {
  documentation?: string;
  website?: string;
  libraryDependencies?: LibraryDependency[];
  hasDll?: boolean;
  dllSize?: number;
}

interface InstalledPlugin {
  moduleName: string;
  displayName?: string;
  version?: string;
  status: string;
}

interface InstallResult {
  success: boolean;
  alreadyInstalled?: boolean;
  pluginName?: string;
  installedVersion?: string;
  warnings?: string[];
  message?: string;
}

interface UpdateResult {
  success: boolean;
  pluginName?: string;
  newVersion?: string;
  warnings?: string[];
  message?: string;
}

function formatSize (bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

const MARKET_BASE_URL = 'https://110.42.98.47:55000';

function getFullIconUrl (iconUrl?: string): string {
  if (!iconUrl) return '';
  if (iconUrl.startsWith('http://') || iconUrl.startsWith('https://')) {
    return iconUrl;
  }
  return `${MARKET_BASE_URL}${iconUrl}`;
}

export default function MarketPage () {
  const [plugins, setPlugins] = useState<MarketPluginItem[]>([]);
  const [installedPlugins, setInstalledPlugins] = useState<InstalledPlugin[]>([]);
  const [loading, setLoading] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [installing, setInstalling] = useState<string | null>(null);
  const [updating, setUpdating] = useState<string | null>(null);
  const [detailModalOpen, setDetailModalOpen] = useState(false);
  const [selectedPlugin, setSelectedPlugin] = useState<MarketPluginDetail | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);

  const loadInstalledPlugins = useCallback(async () => {
    try {
      const token = localStorage.getItem(key.token);
      if (!token) return;
      const _token = JSON.parse(token);

      const response = await fetch('/api/plugins', {
        headers: {
          Authorization: `Bearer ${_token}`,
        },
      });
      const result = await response.json();
      
      if (result.code === 0 && result.data) {
        setInstalledPlugins(result.data);
      }
    } catch (e) {
      console.error('加载已安装插件列表失败', e);
    }
  }, []);

  const loadPlugins = useCallback(async () => {
    setLoading(true);
    try {
      const token = localStorage.getItem(key.token);
      if (!token) {
        toast.error('未登录，请先登录');
        return;
      }
      const _token = JSON.parse(token);

      const response = await fetch('/api/market/list', {
        headers: {
          Authorization: `Bearer ${_token}`,
        },
      });
      const result = await response.json();
      
      if (result.code === 0 && result.data) {
        setPlugins(result.data);
      } else {
        toast.error(result.message || '加载插件列表失败');
      }
    } catch (e: any) {
      toast.error(e.message || '加载插件列表失败');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadPlugins();
    loadInstalledPlugins();
  }, [loadPlugins, loadInstalledPlugins]);

  const getInstalledPlugin = (plugin: MarketPluginItem): InstalledPlugin | undefined => {
    return installedPlugins.find(
      (p) => p.moduleName === plugin.id || p.displayName === plugin.name
    );
  };

  const hasUpdate = (plugin: MarketPluginItem): boolean => {
    const installed = getInstalledPlugin(plugin);
    if (!installed) return false;
    if (!installed.version) return true;
    return installed.version !== plugin.version;
  };

  const handleUpdate = async (plugin: MarketPluginItem) => {
    setUpdating(plugin.id);
    try {
      const token = localStorage.getItem(key.token);
      if (!token) {
        toast.error('未登录，请先登录');
        return;
      }
      const _token = JSON.parse(token);

      const response = await fetch('/api/market/update', {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${_token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ pluginId: plugin.id }),
      });
      const result = await response.json();
      
      if (result.code === 0 && result.data) {
        const data = result.data as UpdateResult;
        if (data.success) {
          if (data.warnings && data.warnings.length > 0) {
            toast.success(`插件 ${data.pluginName} 已更新到 v${data.newVersion}！\n警告: ${data.warnings.join(', ')}`, {
              duration: 5000,
            });
          } else {
            toast.success(`插件 ${data.pluginName} 已更新到 v${data.newVersion}！`);
          }
          loadInstalledPlugins();
        } else {
          toast.error(data.message || '更新失败');
        }
      } else {
        toast.error(result.message || '更新失败');
      }
    } catch (e: any) {
      toast.error(e.message || '更新失败');
    } finally {
      setUpdating(null);
    }
  };

  const handleInstall = async (plugin: MarketPluginItem) => {
    setInstalling(plugin.id);
    try {
      const token = localStorage.getItem(key.token);
      if (!token) {
        toast.error('未登录，请先登录');
        return;
      }
      const _token = JSON.parse(token);

      const response = await fetch('/api/market/install', {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${_token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ pluginId: plugin.id }),
      });
      const result = await response.json();
      
      if (result.code === 0 && result.data) {
        const data = result.data as InstallResult;
        if (data.success) {
          if (data.warnings && data.warnings.length > 0) {
            toast.success(`插件 ${data.pluginName} 安装成功！\n警告: ${data.warnings.join(', ')}`, {
              duration: 5000,
            });
          } else {
            toast.success(`插件 ${data.pluginName} 安装成功！`);
          }
          loadInstalledPlugins();
        } else if (data.alreadyInstalled) {
          toast(`插件 ${data.pluginName} v${data.installedVersion} 已安装`, {
            icon: 'ℹ️',
            duration: 3000,
          });
        } else {
          toast.error(data.message || '安装失败');
        }
      } else {
        toast.error(result.message || '安装失败');
      }
    } catch (e: any) {
      toast.error(e.message || '安装失败');
    } finally {
      setInstalling(null);
    }
  };

  const handleViewDetail = async (plugin: MarketPluginItem) => {
    setLoadingDetail(true);
    setDetailModalOpen(true);
    try {
      const token = localStorage.getItem(key.token);
      if (!token) {
        toast.error('未登录，请先登录');
        return;
      }
      const _token = JSON.parse(token);

      const response = await fetch(`/api/market/detail?id=${plugin.id}`, {
        headers: {
          Authorization: `Bearer ${_token}`,
        },
      });
      const result = await response.json();
      
      if (result.code === 0 && result.data) {
        setSelectedPlugin(result.data);
      } else {
        setSelectedPlugin({ ...plugin } as MarketPluginDetail);
        toast.error(result.message || '加载插件详情失败');
      }
    } catch (e: any) {
      setSelectedPlugin({ ...plugin } as MarketPluginDetail);
      toast.error(e.message || '加载插件详情失败');
    } finally {
      setLoadingDetail(false);
    }
  };

  const filteredPlugins = plugins.filter(
    (p) =>
      p.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      p.description.toLowerCase().includes(searchQuery.toLowerCase()) ||
      p.author.toLowerCase().includes(searchQuery.toLowerCase()) ||
      p.tags?.some((tag) => tag.toLowerCase().includes(searchQuery.toLowerCase()))
  );

  return (
    <div className='flex flex-col h-full w-full gap-4 p-2 md:p-4'>
      <title>插件市场 - MorningCat WebUI</title>

      <div className='flex flex-col md:flex-row items-start md:items-center justify-between gap-4'>
        <div className='flex items-center gap-3'>
          <h1 className='text-2xl font-bold'>插件市场</h1>
          <Tooltip content='刷新列表'>
            <Button
              isIconOnly
              size='sm'
              variant='flat'
              className='bg-default-100/50 hover:bg-default-200/50 text-default-700'
              radius='full'
              onPress={() => loadPlugins()}
              isLoading={loading}
            >
              <IoMdRefresh size={20} />
            </Button>
          </Tooltip>
        </div>

        <Input
          placeholder='搜索插件...'
          startContent={<IoMdSearch className='text-default-400' />}
          value={searchQuery}
          onValueChange={setSearchQuery}
          className='max-w-xs w-full'
          size='sm'
          isClearable
          classNames={{
            inputWrapper: 'bg-default-100/50 dark:bg-black/20 backdrop-blur-md border-white/20 dark:border-white/10',
          }}
        />
      </div>

      <Divider className='opacity-50' />

      {loading ? (
        <div className='flex items-center justify-center h-[200px]'>
          <Spinner size='lg' />
        </div>
      ) : filteredPlugins.length === 0 ? (
        <div className='flex items-center justify-center h-[200px] text-default-400'>
          {searchQuery ? '没有找到匹配的插件' : '暂无可用插件'}
        </div>
      ) : (
        <div className='grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4'>
          {filteredPlugins.map((plugin) => (
            <Card
              key={plugin.id}
              shadow='sm'
              className='bg-default/40 backdrop-blur-lg border-none hover:shadow-lg transition-shadow'
            >
              <CardHeader className='flex flex-col items-start gap-2 pb-0'>
                <div className='flex items-center justify-between w-full gap-2'>
                  <div className='flex items-center gap-2 min-w-0'>
                    {plugin.iconUrl ? (
                      <img
                        src={getFullIconUrl(plugin.iconUrl)}
                        alt={plugin.name}
                        className='w-8 h-8 rounded-lg object-cover flex-shrink-0'
                      />
                    ) : (
                      <div className='w-8 h-8 rounded-lg bg-primary/20 flex items-center justify-center flex-shrink-0'>
                        <span className='text-sm font-bold text-primary'>{plugin.name.charAt(0).toUpperCase()}</span>
                      </div>
                    )}
                    <h3 className='text-lg font-semibold truncate'>{plugin.name}</h3>
                  </div>
                  <Chip size='sm' variant='flat' color='primary' className='flex-shrink-0'>
                    v{plugin.version}
                  </Chip>
                </div>
                <p className='text-xs text-default-500'>作者: {plugin.author}</p>
              </CardHeader>
              <CardBody className='py-3'>
                <p className='text-sm text-default-600 line-clamp-2 mb-3'>
                  {plugin.description || '暂无描述'}
                </p>
                {plugin.tags && plugin.tags.length > 0 && (
                  <div className='flex flex-wrap gap-1 mb-3'>
                    {plugin.tags.slice(0, 3).map((tag) => (
                      <Chip key={tag} size='sm' variant='flat' className='bg-default-100/50 text-default-500'>
                        {tag}
                      </Chip>
                    ))}
                  </div>
                )}
                <div className='flex gap-2 mt-auto'>
                  {getInstalledPlugin(plugin) ? (
                    hasUpdate(plugin) ? (
                      <Button
                        size='sm'
                        color='success'
                        variant='flat'
                        startContent={<IoMdCloudUpload />}
                        onPress={() => handleUpdate(plugin)}
                        isLoading={updating === plugin.id}
                        className='flex-1'
                      >
                        更新
                      </Button>
                    ) : (
                      <Button
                        size='sm'
                        color='default'
                        variant='flat'
                        isDisabled
                        className='flex-1'
                      >
                        已安装
                      </Button>
                    )
                  ) : (
                    <Button
                      size='sm'
                      color='primary'
                      variant='flat'
                      startContent={<IoMdDownload />}
                      onPress={() => handleInstall(plugin)}
                      isLoading={installing === plugin.id}
                      className='flex-1'
                    >
                      安装
                    </Button>
                  )}
                  <Button
                    size='sm'
                    variant='light'
                    isIconOnly
                    onPress={() => handleViewDetail(plugin)}
                  >
                    <IoMdInformationCircle />
                  </Button>
                </div>
              </CardBody>
            </Card>
          ))}
        </div>
      )}

      <Modal
        isOpen={detailModalOpen}
        onClose={() => {
          setDetailModalOpen(false);
          setSelectedPlugin(null);
        }}
        size='3xl'
        scrollBehavior='inside'
      >
        <ModalContent>
          <ModalHeader>插件详情</ModalHeader>
          <ModalBody>
            {loadingDetail ? (
              <div className='flex items-center justify-center h-[200px]'>
                <Spinner size='lg' />
              </div>
            ) : selectedPlugin ? (
              <div className='space-y-4'>
                <div className='grid grid-cols-1 md:grid-cols-2 gap-6'>
                  <div className='space-y-4'>
                    <div>
                      <p className='text-small text-default-500'>版本</p>
                      <p>{selectedPlugin.version}</p>
                    </div>
                    <div>
                      <p className='text-small text-default-500'>作者</p>
                      <p>{selectedPlugin.author}</p>
                    </div>
                    <div>
                      <p className='text-small text-default-500'>描述</p>
                      <p>{selectedPlugin.description || '暂无描述'}</p>
                    </div>
                    {selectedPlugin.documentation && (
                      <div>
                        <Divider className='my-2' />
                        <p className='text-small text-default-500 mb-2'>文档</p>
                        <div className='rounded-lg border border-default-200 p-4 bg-default-50 max-h-80 overflow-y-auto'>
                          <TailwindMarkdown content={selectedPlugin.documentation} />
                        </div>
                      </div>
                    )}
                  </div>

                  <div className='space-y-4'>
                    {selectedPlugin.website && (
                      <div>
                        <p className='text-small text-default-500'>网址</p>
                        <a
                          href={selectedPlugin.website}
                          target='_blank'
                          rel='noopener noreferrer'
                          className='text-primary hover:underline break-all'
                        >
                          {selectedPlugin.website}
                        </a>
                      </div>
                    )}

                    {selectedPlugin.tags && selectedPlugin.tags.length > 0 && (
                      <div>
                        <p className='text-small text-default-500 mb-2'>标签</p>
                        <div className='flex flex-wrap gap-2'>
                          {selectedPlugin.tags.map((tag) => (
                            <Chip key={tag} size='sm' variant='flat'>{tag}</Chip>
                          ))}
                        </div>
                      </div>
                    )}

                    {selectedPlugin.dependencies && selectedPlugin.dependencies.length > 0 && (
                      <div>
                        <p className='text-small text-default-500 mb-2 text-warning'>插件依赖</p>
                        <p className='text-xs text-warning-500 mb-2'>需要手动安装以下前置插件:</p>
                        <div className='flex flex-wrap gap-2'>
                          {selectedPlugin.dependencies.map((dep) => (
                            <Chip key={dep} size='sm' variant='flat' color='warning'>{dep}</Chip>
                          ))}
                        </div>
                      </div>
                    )}

                    {selectedPlugin.nugetDependencies && selectedPlugin.nugetDependencies.length > 0 && (
                      <div>
                        <p className='text-small text-default-500 mb-2'>NuGet 依赖</p>
                        <p className='text-xs text-default-500 mb-2'>安装时会自动还原:</p>
                        <div className='flex flex-wrap gap-2'>
                          {selectedPlugin.nugetDependencies.map((dep) => (
                            <Chip key={dep} size='sm' variant='flat' color='secondary'>{dep}</Chip>
                          ))}
                        </div>
                      </div>
                    )}

                    {selectedPlugin.libraryDependencies && selectedPlugin.libraryDependencies.length > 0 && (
                      <div>
                        <p className='text-small text-default-500 mb-2'>依赖库文件</p>
                        <div className='space-y-2'>
                          {selectedPlugin.libraryDependencies.map((dep) => (
                            <div key={dep.fileName} className='flex items-center justify-between bg-content3 p-2 rounded-lg'>
                              <div className='flex-1 min-w-0'>
                                <p className='text-sm font-medium truncate'>{dep.fileName}</p>
                                {dep.description && (
                                  <p className='text-xs text-default-500 truncate'>{dep.description}</p>
                                )}
                                <p className='text-xs text-default-400'>
                                  {dep.exists ? formatSize(dep.size) : '文件不存在'}
                                </p>
                              </div>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                    <Divider />

                    <div>
                      <p className='text-small text-default-500'>插件文件</p>
                      <p>
                        {selectedPlugin.hasDll
                          ? `DLL 大小: ${formatSize(selectedPlugin.dllSize || 0)}`
                          : '无 DLL 文件'}
                      </p>
                    </div>
                  </div>
                </div>
              </div>
            ) : (
              <div className='text-center text-default-400 py-8'>无法加载插件详情</div>
            )}
          </ModalBody>
          <ModalFooter>
            <Button variant='light' onPress={() => setDetailModalOpen(false)}>
              关闭
            </Button>
            {selectedPlugin && (
              <Button
                color='primary'
                startContent={<IoMdDownload />}
                onPress={() => {
                  setDetailModalOpen(false);
                  handleInstall(selectedPlugin);
                }}
                isLoading={installing === selectedPlugin.id}
              >
                安装
              </Button>
            )}
          </ModalFooter>
        </ModalContent>
      </Modal>
    </div>
  );
}
