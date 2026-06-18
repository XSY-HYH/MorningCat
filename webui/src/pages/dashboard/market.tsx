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
import useI18n from '@/hooks/use-i18n';

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
  const { t } = useI18n();
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
      console.error(t('webui.market.load_installed_failed'), e);
    }
  }, []);

  const loadPlugins = useCallback(async () => {
    setLoading(true);
    try {
      const token = localStorage.getItem(key.token);
      if (!token) {
        toast.error(t('webui.market.not_logged_in'));
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
        toast.error(result.message || t('webui.market.load_list_failed'));
      }
    } catch (e: any) {
      toast.error(e.message || t('webui.market.load_list_failed'));
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
        toast.error(t('webui.market.not_logged_in'));
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
            toast.success(t('webui.market.update_success_with_warnings', [data.pluginName || '', data.newVersion || '', data.warnings.join(', ')]), {
              duration: 5000,
            });
          } else {
            toast.success(t('webui.market.update_success', [data.pluginName || '', data.newVersion || '']));
          }
          loadInstalledPlugins();
        } else {
          toast.error(data.message || t('webui.market.update_failed'));
        }
      } else {
        toast.error(result.message || t('webui.market.update_failed'));
      }
    } catch (e: any) {
      toast.error(e.message || t('webui.market.update_failed'));
    } finally {
      setUpdating(null);
    }
  };

  const handleInstall = async (plugin: MarketPluginItem) => {
    setInstalling(plugin.id);
    try {
      const token = localStorage.getItem(key.token);
      if (!token) {
        toast.error(t('webui.market.not_logged_in'));
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
            toast.success(t('webui.market.install_success_with_warnings', [data.pluginName || '', data.warnings.join(', ')]), {
              duration: 5000,
            });
          } else {
            toast.success(t('webui.market.install_success', data.pluginName || ''));
          }
          loadInstalledPlugins();
        } else if (data.alreadyInstalled) {
          toast(t('webui.market.already_installed', [data.pluginName || '', data.installedVersion || '']), {
            icon: 'ℹ️',
            duration: 3000,
          });
        } else {
          toast.error(data.message || t('webui.market.install_failed'));
        }
      } else {
        toast.error(result.message || t('webui.market.install_failed'));
      }
    } catch (e: any) {
      toast.error(e.message || t('webui.market.install_failed'));
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
        toast.error(t('webui.market.not_logged_in'));
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
        toast.error(result.message || t('webui.market.load_detail_failed'));
      }
    } catch (e: any) {
      setSelectedPlugin({ ...plugin } as MarketPluginDetail);
      toast.error(e.message || t('webui.market.load_detail_failed'));
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
      <title>{t('webui.market.title')}</title>

      <div className='flex flex-col md:flex-row items-start md:items-center justify-between gap-4'>
        <div className='flex items-center gap-3'>
          <h1 className='text-2xl font-bold'>{t('webui.market.heading')}</h1>
          <Tooltip content={t('webui.market.refresh')}>
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
          placeholder={t('webui.market.search_placeholder')}
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
          {searchQuery ? t('webui.market.no_match') : t('webui.market.no_plugins')}
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
                <p className='text-xs text-default-500'>{t('webui.market.author', plugin.author)}</p>
              </CardHeader>
              <CardBody className='py-3'>
                <p className='text-sm text-default-600 line-clamp-2 mb-3'>
                  {plugin.description || t('webui.market.no_description')}
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
                        {t('webui.market.update')}
                      </Button>
                    ) : (
                      <Button
                        size='sm'
                        color='default'
                        variant='flat'
                        isDisabled
                        className='flex-1'
                      >
                        {t('webui.market.installed')}
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
                      {t('webui.market.install')}
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
          <ModalHeader>{t('webui.market.detail')}</ModalHeader>
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
                      <p className='text-small text-default-500'>{t('webui.market.version')}</p>
                      <p>{selectedPlugin.version}</p>
                    </div>
                    <div>
                      <p className='text-small text-default-500'>{t('webui.market.author_label')}</p>
                      <p>{selectedPlugin.author}</p>
                    </div>
                    <div>
                      <p className='text-small text-default-500'>{t('webui.market.description')}</p>
                      <p>{selectedPlugin.description || t('webui.market.no_description')}</p>
                    </div>
                    {selectedPlugin.documentation && (
                      <div>
                        <Divider className='my-2' />
                        <p className='text-small text-default-500 mb-2'>{t('webui.market.documentation')}</p>
                        <div className='rounded-lg border border-default-200 p-4 bg-default-50 max-h-80 overflow-y-auto'>
                          <TailwindMarkdown content={selectedPlugin.documentation} />
                        </div>
                      </div>
                    )}
                  </div>

                  <div className='space-y-4'>
                    {selectedPlugin.website && (
                      <div>
                        <p className='text-small text-default-500'>{t('webui.market.website')}</p>
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
                        <p className='text-small text-default-500 mb-2'>{t('webui.market.tags')}</p>
                        <div className='flex flex-wrap gap-2'>
                          {selectedPlugin.tags.map((tag) => (
                            <Chip key={tag} size='sm' variant='flat'>{tag}</Chip>
                          ))}
                        </div>
                      </div>
                    )}

                    {selectedPlugin.dependencies && selectedPlugin.dependencies.length > 0 && (
                      <div>
                        <p className='text-small text-default-500 mb-2 text-warning'>{t('webui.market.plugin_deps')}</p>
                        <p className='text-xs text-warning-500 mb-2'>{t('webui.market.plugin_deps_hint')}</p>
                        <div className='flex flex-wrap gap-2'>
                          {selectedPlugin.dependencies.map((dep) => (
                            <Chip key={dep} size='sm' variant='flat' color='warning'>{dep}</Chip>
                          ))}
                        </div>
                      </div>
                    )}

                    {selectedPlugin.nugetDependencies && selectedPlugin.nugetDependencies.length > 0 && (
                      <div>
                        <p className='text-small text-default-500 mb-2'>{t('webui.market.nuget_deps')}</p>
                        <p className='text-xs text-default-500 mb-2'>{t('webui.market.nuget_deps_hint')}</p>
                        <div className='flex flex-wrap gap-2'>
                          {selectedPlugin.nugetDependencies.map((dep) => (
                            <Chip key={dep} size='sm' variant='flat' color='secondary'>{dep}</Chip>
                          ))}
                        </div>
                      </div>
                    )}

                    {selectedPlugin.libraryDependencies && selectedPlugin.libraryDependencies.length > 0 && (
                      <div>
                        <p className='text-small text-default-500 mb-2'>{t('webui.market.lib_deps')}</p>
                        <div className='space-y-2'>
                          {selectedPlugin.libraryDependencies.map((dep) => (
                            <div key={dep.fileName} className='flex items-center justify-between bg-content3 p-2 rounded-lg'>
                              <div className='flex-1 min-w-0'>
                                <p className='text-sm font-medium truncate'>{dep.fileName}</p>
                                {dep.description && (
                                  <p className='text-xs text-default-500 truncate'>{dep.description}</p>
                                )}
                                <p className='text-xs text-default-400'>
                                  {dep.exists ? formatSize(dep.size) : t('webui.market.file_not_found')}
                                </p>
                              </div>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                    <Divider />

                    <div>
                      <p className='text-small text-default-500'>{t('webui.market.plugin_files')}</p>
                      <p>
                        {selectedPlugin.hasDll
                          ? t('webui.market.dll_size', formatSize(selectedPlugin.dllSize || 0))
                          : t('webui.market.no_dll')}
                      </p>
                    </div>
                  </div>
                </div>
              </div>
            ) : (
              <div className='text-center text-default-400 py-8'>{t('webui.market.load_detail_failed')}</div>
            )}
          </ModalBody>
          <ModalFooter>
            <Button variant='light' onPress={() => setDetailModalOpen(false)}>
              {t('webui.common.close')}
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
                {t('webui.market.install')}
              </Button>
            )}
          </ModalFooter>
        </ModalContent>
      </Modal>
    </div>
  );
}
