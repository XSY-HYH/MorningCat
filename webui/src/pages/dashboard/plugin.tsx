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

function getStatusColor (status: string): 'success' | 'danger' | 'warning' | 'default' {
  switch (status) {
    case 'Running':
      return 'success';
    case 'Error':
      return 'danger';
    case 'Disabled':
      return 'warning';
    default:
      return 'default';
  }
}

function getStatusText (status: string): string {
  switch (status) {
    case 'Running':
      return '运行中';
    case 'Error':
      return '加载失败';
    case 'Disabled':
      return '已禁用';
    case 'Unloaded':
      return '已卸载';
    case 'Initializing':
      return '初始化中';
    case 'Scanned':
      return '待初始化';
    default:
      return status;
  }
}

function getPluginIcon (plugin: PluginItem | PluginDetail): string {
  if (plugin.iconBase64) {
    return `data:image/png;base64,${plugin.iconBase64}`;
  }
  return `https://avatar.vercel.sh/${encodeURIComponent(plugin.moduleName)}`;
}

export default function PluginPage () {
  const [plugins, setPlugins] = useState<PluginItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [pluginManagerNotFound, setPluginManagerNotFound] = useState(false);
  const [selectedPlugin, setSelectedPlugin] = useState<PluginDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const dialog = useDialog();

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
        toast.error('获取插件详情失败');
      }
    } catch (e: any) {
      toast.error(e.message);
    } finally {
      setDetailLoading(false);
    }
  };

  const handleToggle = async (plugin: PluginItem) => {
    if (plugin.isBuiltin) {
      toast.error('无法禁用内置模块');
      return;
    }

    const isDisabled = plugin.status === 'Disabled';
    const actionText = isDisabled ? '启用' : '禁用';
    
    dialog.confirm({
      title: `${actionText}插件`,
      content: (
        <p className='text-base text-default-800'>
          确定要{actionText}插件「<span className='font-semibold text-primary'>{plugin.displayName || plugin.moduleName}</span>」吗？
          {isDisabled ? '' : '（重启后生效）'}
        </p>
      ),
      confirmText: '确定',
      cancelText: '取消',
      onConfirm: async () => {
        const loadingToast = toast.loading(`${actionText}中...`);
        try {
          if (isDisabled) {
            await PluginManager.enablePlugin(plugin.moduleName);
          } else {
            await PluginManager.disablePlugin(plugin.moduleName);
          }
          toast.success(`${actionText}成功`, { id: loadingToast });
          loadPlugins();
        } catch (e: any) {
          toast.error(e.message, { id: loadingToast });
        }
      },
    });
  };

  const handleUnload = async (plugin: PluginItem) => {
    if (plugin.isBuiltin) {
      toast.error('无法卸载内置模块');
      return;
    }

    dialog.confirm({
      title: '卸载插件',
      content: (
        <p className='text-base text-default-800'>
          确定要卸载插件「<span className='font-semibold text-danger'>{plugin.displayName || plugin.moduleName}</span>」吗？此操作不可恢复。
        </p>
      ),
      confirmText: '确定卸载',
      cancelText: '取消',
      onConfirm: async () => {
        const loadingToast = toast.loading('卸载中...');
        try {
          const success = await PluginManager.unloadPlugin(plugin.moduleName);
          if (success) {
            toast.success('卸载成功', { id: loadingToast });
            loadPlugins();
          } else {
            toast.error('卸载失败，可能有其他插件依赖此模块', { id: loadingToast });
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
      <title>插件管理 - MorningCat WebUI</title>
      <div className='p-2 md:p-4 relative'>
        <PageLoading loading={loading} />

        <div className='flex mb-6 items-center gap-4'>
          <h1 className='text-2xl font-bold'>插件管理</h1>
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
                无插件加载
              </h2>
              <p className='text-default-500 dark:text-white/60 max-w-md'>
                插件管理器未加载，请检查 Modules 目录是否存在
              </p>
            </div>
          )
          : plugins.length === 0
            ? (
              <div className='text-default-400'>暂时没有安装插件</div>
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
                          <Chip size='sm' color='primary' variant='flat'>内置</Chip>
                        )}
                      </div>
                      <Chip size='sm' color={getStatusColor(plugin.status)} variant='flat'>
                        {getStatusText(plugin.status)}
                      </Chip>
                    </CardHeader>
                    <Divider />
                    <CardBody className='px-4 py-3'>
                      <p className='text-sm text-default-500 line-clamp-2 min-h-[40px]'>
                        {plugin.description || '暂无描述'}
                      </p>
                      {plugin.author && (
                        <p className='text-xs text-default-400 mt-2'>
                          作者: {plugin.author}
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
                          详情
                        </Button>
                        {!plugin.isBuiltin && (
                          <>
                            <Button
                              size='sm'
                              variant='flat'
                              color={plugin.status === 'Disabled' ? 'success' : 'warning'}
                              onPress={() => handleToggle(plugin)}
                            >
                              {plugin.status === 'Disabled' ? '启用' : '禁用'}
                            </Button>
                            <Button
                              size='sm'
                              variant='flat'
                              color='danger'
                              onPress={() => handleUnload(plugin)}
                            >
                              卸载
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
                    {getStatusText(selectedPlugin.status)}
                  </Chip>
                  {selectedPlugin.isBuiltin && (
                    <Chip size='sm' color='primary' variant='flat'>内置</Chip>
                  )}
                </div>
              </CardHeader>
              <Divider />
              <CardBody className='px-4 py-3 flex flex-col gap-3'>
                <div>
                  <p className='text-xs text-default-400'>模块名称</p>
                  <p className='text-sm'>{selectedPlugin.moduleName}</p>
                </div>
                {selectedPlugin.author && (
                  <div>
                    <p className='text-xs text-default-400'>作者</p>
                    <p className='text-sm'>{selectedPlugin.author}</p>
                  </div>
                )}
                {selectedPlugin.website && (
                  <div>
                    <p className='text-xs text-default-400'>网站</p>
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
                    <p className='text-xs text-default-400'>描述</p>
                    <p className='text-sm'>{selectedPlugin.description}</p>
                  </div>
                )}
                {selectedPlugin.moduleType && (
                  <div>
                    <p className='text-xs text-default-400'>类型</p>
                    <p className='text-sm font-mono text-xs break-all'>{selectedPlugin.moduleType}</p>
                  </div>
                )}
                {selectedPlugin.assemblyPath && (
                  <div>
                    <p className='text-xs text-default-400'>程序集路径</p>
                    <p className='text-sm font-mono text-xs break-all'>{selectedPlugin.assemblyPath}</p>
                  </div>
                )}
                {selectedPlugin.dependencies && selectedPlugin.dependencies.length > 0 && (
                  <div>
                    <p className='text-xs text-default-400'>依赖</p>
                    <div className='flex flex-wrap gap-1 mt-1'>
                      {selectedPlugin.dependencies.map(dep => (
                        <Chip key={dep} size='sm' variant='flat'>{dep}</Chip>
                      ))}
                    </div>
                  </div>
                )}
                {selectedPlugin.dependents && selectedPlugin.dependents.length > 0 && (
                  <div>
                    <p className='text-xs text-default-400'>被依赖</p>
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
