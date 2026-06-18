import { Button } from '@heroui/button';
import { Card, CardBody, CardHeader } from '@heroui/card';
import { Chip } from '@heroui/chip';
import { Divider } from '@heroui/divider';
import { Avatar } from '@heroui/avatar';
import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { IoMdRefresh } from 'react-icons/io';

import PageLoading from '@/components/page_loading';
import PluginManager, { PluginItem, PluginDetail } from '@/controllers/plugin_manager';
import useDialog from '@/hooks/use-dialog';
import useI18n from '@/hooks/use-i18n';

function getStatusColor (status: string): 'success' | 'danger' | 'warning' | 'default' {
  switch (status) {
    case 'Running':
      return 'success';
    case 'Error':
      return 'danger';
    case 'Disabled':
      return 'warning';
    case 'SignatureFailed':
      return 'danger';
    default:
      return 'default';
  }
}

function getPluginIcon (plugin: PluginItem | PluginDetail): string {
  if (plugin.iconBase64) {
    return `data:image/png;base64,${plugin.iconBase64}`;
  }
  return `https://avatar.vercel.sh/${encodeURIComponent(plugin.moduleName)}`;
}

export default function PluginPage () {
  const { t } = useI18n();
  const [plugins, setPlugins] = useState<PluginItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [pluginManagerNotFound, setPluginManagerNotFound] = useState(false);
  const [selectedPlugin, setSelectedPlugin] = useState<PluginDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const dialog = useDialog();

  const getStatusText = (status: string): string => {
    switch (status) {
      case 'Running':
        return t('webui.plugin.status.running');
      case 'Error':
        return t('webui.plugin.status.load_failed');
      case 'Disabled':
        return t('webui.plugin.status.disabled');
      case 'Unloaded':
        return t('webui.plugin.status.unloaded');
      case 'Initializing':
        return t('webui.plugin.status.initializing');
      case 'Scanned':
        return t('webui.plugin.status.pending');
      case 'SignatureFailed':
        return t('webui.plugin.status.signature_failed');
      default:
        return status;
    }
  };

  const loadPlugins = async () => {
    setLoading(true);
    setPluginManagerNotFound(false);
    try {
      const listResult = await PluginManager.getPluginList();

      if (listResult.pluginManagerNotFound) {
        setPluginManagerNotFound(true);
        setPlugins([]);
      } else {
        setPlugins(listResult.plugins);
      }
    } catch (e: any) {
      toast.error(e.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadPlugins();
  }, []);

  const handleViewDetail = async (plugin: PluginItem) => {
    setDetailLoading(true);
    try {
      const detail = await PluginManager.getPluginDetail(plugin.moduleName);
      if (detail) {
        setSelectedPlugin(detail);
      } else {
        toast.error(t('webui.plugin.load_detail_failed'));
      }
    } catch (e: any) {
      toast.error(e.message);
    } finally {
      setDetailLoading(false);
    }
  };

  const handleToggle = async (plugin: PluginItem) => {
    if (plugin.isBuiltin) {
      toast.error(t('webui.plugin.cannot_disable_builtin'));
      return;
    }

    const isDisabled = plugin.status === 'Disabled';
    const actionText = isDisabled ? t('webui.plugin.enable') : t('webui.plugin.disable');
    const titleKey = isDisabled ? 'webui.plugin.confirm_enable' : 'webui.plugin.confirm_disable';
    const msgKey = isDisabled ? 'webui.plugin.confirm_enable_msg' : 'webui.plugin.confirm_disable_msg';

    dialog.confirm({
      title: t(titleKey),
      content: (
        <p className='text-base text-default-800'>
          {t(msgKey, plugin.displayName || plugin.moduleName)}
        </p>
      ),
      confirmText: t('webui.plugin.confirm_btn'),
      cancelText: t('webui.plugin.cancel_btn'),
      onConfirm: async () => {
        const loadingToast = toast.loading(t('webui.plugin.disabling'));
        try {
          if (isDisabled) {
            await PluginManager.enablePlugin(plugin.moduleName);
          } else {
            await PluginManager.disablePlugin(plugin.moduleName);
          }
          toast.success(t('webui.plugin.success', actionText), { id: loadingToast });
          loadPlugins();
        } catch (e: any) {
          toast.error(e.message, { id: loadingToast });
        }
      },
    });
  };

  const handleUnload = async (plugin: PluginItem) => {
    if (plugin.isBuiltin) {
      toast.error(t('webui.plugin.cannot_uninstall_builtin'));
      return;
    }

    dialog.confirm({
      title: t('webui.plugin.confirm_uninstall'),
      content: (
        <p className='text-base text-default-800'>
          {t('webui.plugin.confirm_uninstall_msg', plugin.displayName || plugin.moduleName)}
        </p>
      ),
      confirmText: t('webui.plugin.confirm_uninstall_btn'),
      cancelText: t('webui.plugin.cancel_btn'),
      onConfirm: async () => {
        const loadingToast = toast.loading(t('webui.plugin.uninstalling'));
        try {
          const success = await PluginManager.unloadPlugin(plugin.moduleName);
          if (success) {
            toast.success(t('webui.plugin.uninstall_success'), { id: loadingToast });
            loadPlugins();
          } else {
            toast.error(t('webui.plugin.uninstall_failed'), { id: loadingToast });
          }
        } catch (e: any) {
          toast.error(e.message, { id: loadingToast });
        }
      },
    });
  };

  const handleDelete = async (plugin: PluginItem) => {
    if (plugin.isBuiltin) {
      toast.error(t('webui.plugin.cannot_delete_builtin'));
      return;
    }

    dialog.confirm({
      title: t('webui.plugin.confirm_delete'),
      content: (
        <div className='text-base text-default-800'>
          <p>
            {t('webui.plugin.confirm_delete_msg', plugin.displayName || plugin.moduleName)}
          </p>
        </div>
      ),
      confirmText: t('webui.plugin.confirm_delete_btn'),
      cancelText: t('webui.plugin.cancel_btn'),
      onConfirm: async () => {
        const loadingToast = toast.loading(t('webui.plugin.deleting'));
        try {
          const success = await PluginManager.deletePlugin(plugin.moduleName);
          if (success) {
            toast.success(t('webui.plugin.delete_success'), { id: loadingToast });
            loadPlugins();
          } else {
            toast.error(t('webui.plugin.delete_failed'), { id: loadingToast });
          }
        } catch (e: any) {
          toast.error(e.message, { id: loadingToast });
        }
      },
    });
  };

  const closeDetail = () => {
    setSelectedPlugin(null);
  };

  return (
    <>
      <title>{t('webui.plugin.title')}</title>
      <div className='p-2 md:p-4 relative'>
        <PageLoading loading={loading} />

        <div className='flex mb-6 items-center gap-4'>
          <h1 className='text-2xl font-bold'>{t('webui.plugin.heading')}</h1>
          <Button
            isIconOnly
            className='bg-default-100/50 hover:bg-default-200/50 text-default-700 backdrop-blur-md'
            radius='full'
            onPress={loadPlugins}
          >
            <IoMdRefresh size={24} />
          </Button>
        </div>

        {pluginManagerNotFound
          ? (
            <div className='flex flex-col items-center justify-center min-h-[400px] text-center'>
              <div className='text-6xl mb-4'>📦</div>
              <h2 className='text-xl font-semibold text-default-700 dark:text-white/90 mb-2'>
                {t('webui.plugin.no_plugin_loaded')}
              </h2>
              <p className='text-default-500 dark:text-white/60 max-w-md'>
                {t('webui.plugin.no_loader')}
              </p>
            </div>
          )
          : plugins.length === 0
            ? (
              <div className='text-default-400'>{t('webui.plugin.no_plugins')}</div>
            )
            : (
              <div className='grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 justify-start items-stretch gap-4'>
                {plugins.map(plugin => (
                  <Card key={plugin.moduleName} className='bg-white/60 dark:bg-black/40 backdrop-blur-sm'>
                    <CardHeader className='flex flex-col items-start gap-2 px-4 pt-4 pb-2'>
                      <div className='flex items-center justify-between w-full'>
                        <div className='flex items-center gap-2 min-w-0 flex-1'>
                          <Avatar
                            src={getPluginIcon(plugin)}
                            name={plugin.displayName || plugin.moduleName}
                            size='sm'
                            className='flex-shrink-0'
                          />
                          <h3 className='text-lg font-semibold truncate'>
                            {plugin.displayName || plugin.moduleName}
                          </h3>
                        </div>
                        {plugin.isBuiltin && (
                          <Chip size='sm' color='primary' variant='flat'>{t('webui.plugin.builtin')}</Chip>
                        )}
                      </div>
                      <Chip size='sm' color={getStatusColor(plugin.status)} variant='flat'>
                        {plugin.signatureStatus === 'Failed' ? t('webui.plugin.status.signature_failed') : getStatusText(plugin.status)}
                      </Chip>
                    </CardHeader>
                    <Divider />
                    <CardBody className='px-4 py-3'>
                      <p className='text-sm text-default-500 line-clamp-2 min-h-[40px]'>
                        {plugin.description || t('webui.plugin.no_description')}
                      </p>
                      {plugin.author && (
                        <p className='text-xs text-default-400 mt-2'>
                          {t('webui.plugin.author', plugin.author)}
                        </p>
                      )}
                      <div className='flex flex-wrap gap-2 mt-3'>
                        <Button
                          size='sm'
                          variant='flat'
                          color='primary'
                          onPress={() => handleViewDetail(plugin)}
                          isLoading={detailLoading}
                        >
                          {t('webui.plugin.detail')}
                        </Button>
                        {!plugin.isBuiltin && (
                          <>
                            <Button
                              size='sm'
                              variant='flat'
                              color={plugin.status === 'Disabled' ? 'success' : 'warning'}
                              onPress={() => handleToggle(plugin)}
                            >
                              {plugin.status === 'Disabled' ? t('webui.plugin.enable') : t('webui.plugin.disable')}
                            </Button>
                            <Button
                              size='sm'
                              variant='flat'
                              color='danger'
                              onPress={() => handleUnload(plugin)}
                            >
                              {t('webui.plugin.uninstall')}
                            </Button>
                            <Button
                              size='sm'
                              variant='flat'
                              color='danger'
                              onPress={() => handleDelete(plugin)}
                            >
                              {t('webui.plugin.delete')}
                            </Button>
                          </>
                        )}
                      </div>
                    </CardBody>
                  </Card>
                ))}
              </div>
            )}

        {selectedPlugin && (
          <div
            className='fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4'
            onClick={closeDetail}
          >
            <Card
              className='bg-white/90 dark:bg-black/80 backdrop-blur-md max-w-lg w-full max-h-[80vh] overflow-auto'
              onClick={e => e.stopPropagation()}
            >
              <CardHeader className='flex flex-col items-start gap-2 px-4 pt-4 pb-2'>
                <div className='flex items-center justify-between w-full'>
                  <div className='flex items-center gap-2'>
                    <Avatar
                      src={getPluginIcon(selectedPlugin)}
                      name={selectedPlugin.displayName || selectedPlugin.moduleName}
                      size='md'
                    />
                    <h3 className='text-xl font-semibold'>
                      {selectedPlugin.displayName || selectedPlugin.moduleName}
                    </h3>
                  </div>
                  <Button
                    isIconOnly
                    size='sm'
                    variant='light'
                    onPress={closeDetail}
                  >
                    ✕
                  </Button>
                </div>
                <div className='flex gap-2'>
                  <Chip size='sm' color={getStatusColor(selectedPlugin.status)} variant='flat'>
                    {selectedPlugin.signatureStatus === 'Failed' ? t('webui.plugin.status.signature_failed') : getStatusText(selectedPlugin.status)}
                  </Chip>
                  {selectedPlugin.isBuiltin && (
                    <Chip size='sm' color='primary' variant='flat'>{t('webui.plugin.builtin')}</Chip>
                  )}
                </div>
              </CardHeader>
              <Divider />
              <CardBody className='px-4 py-3 flex flex-col gap-3'>
                <div>
                  <p className='text-xs text-default-400'>{t('webui.plugin.module_name')}</p>
                  <p className='text-sm'>{selectedPlugin.moduleName}</p>
                </div>
                {selectedPlugin.author && (
                  <div>
                    <p className='text-xs text-default-400'>{t('webui.plugin.author_label')}</p>
                    <p className='text-sm'>{selectedPlugin.author}</p>
                  </div>
                )}
                {selectedPlugin.website && (
                  <div>
                    <p className='text-xs text-default-400'>{t('webui.plugin.website')}</p>
                    <a
                      href={selectedPlugin.website}
                      target='_blank'
                      rel='noopener noreferrer'
                      className='text-sm text-primary hover:underline'
                    >
                      {selectedPlugin.website}
                    </a>
                  </div>
                )}
                {selectedPlugin.description && (
                  <div>
                    <p className='text-xs text-default-400'>{t('webui.plugin.description')}</p>
                    <p className='text-sm'>{selectedPlugin.description}</p>
                  </div>
                )}
                {selectedPlugin.moduleType && (
                  <div>
                    <p className='text-xs text-default-400'>{t('webui.plugin.type')}</p>
                    <p className='text-sm font-mono text-xs break-all'>{selectedPlugin.moduleType}</p>
                  </div>
                )}
                {selectedPlugin.assemblyPath && (
                  <div>
                    <p className='text-xs text-default-400'>{t('webui.plugin.assembly_path')}</p>
                    <p className='text-sm font-mono text-xs break-all'>{selectedPlugin.assemblyPath}</p>
                  </div>
                )}
                {selectedPlugin.dependencies && selectedPlugin.dependencies.length > 0 && (
                  <div>
                    <p className='text-xs text-default-400'>{t('webui.plugin.dependencies')}</p>
                    <div className='flex flex-wrap gap-1 mt-1'>
                      {selectedPlugin.dependencies.map(dep => (
                        <Chip key={dep} size='sm' variant='flat'>{dep}</Chip>
                      ))}
                    </div>
                  </div>
                )}
                {selectedPlugin.dependents && selectedPlugin.dependents.length > 0 && (
                  <div>
                    <p className='text-xs text-default-400'>{t('webui.plugin.dependents')}</p>
                    <div className='flex flex-wrap gap-1 mt-1'>
                      {selectedPlugin.dependents.map(dep => (
                        <Chip key={dep} size='sm' variant='flat' color='warning'>{dep}</Chip>
                      ))}
                    </div>
                  </div>
                )}
              </CardBody>
            </Card>
          </div>
        )}
      </div>
    </>
  );
}
