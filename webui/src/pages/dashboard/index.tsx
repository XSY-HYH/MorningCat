import { Card, CardBody } from '@heroui/card';
import { useLocalStorage } from '@uidotdev/usehooks';
import { useRequest } from 'ahooks';
import clsx from 'clsx';
import { useCallback, useEffect, useRef, useState } from 'react';
import key from '@/const/key';

import QQInfoCard from '@/components/qq_info_card';

import QQManager from '@/controllers/qq_manager';
import WebUIManager from '@/controllers/webui_manager';
import { Image } from '@heroui/image';
import { BiSolidMemoryCard } from 'react-icons/bi';
import { GiCpu } from 'react-icons/gi';
import { Button } from '@heroui/button';
import { IoRefresh, IoCheckmarkCircle, IoAlertCircle } from 'react-icons/io5';

import bkg from '@/assets/images/bg/1AD934174C0107F14BAD8776D29C5F90.png';

const QQInfo: React.FC = () => {
  const { data, loading, error } = useRequest(QQManager.getQQLoginInfo);
  return <QQInfoCard data={data} error={error} loading={loading} />;
};

export interface SystemStatusCardProps {
  setArchInfo: (arch: string | undefined) => void;
}

interface SystemStatusItemProps {
  title: string;
  value?: string | number;
  size?: 'md' | 'lg';
  unit?: string;
  hasBackground?: boolean;
}

const SystemStatusItem: React.FC<SystemStatusItemProps> = ({
  title,
  value = '-',
  size = 'md',
  unit,
  hasBackground = false,
}) => {
  return (
    <div
      className={clsx(
        'py-1.5 text-sm transition-colors',
        size === 'lg' ? 'col-span-2' : 'col-span-1 flex justify-between items-center'
      )}
    >
      <div className={clsx(
        'w-24 font-medium',
        hasBackground ? 'text-white/90' : 'text-default-600 dark:text-gray-300'
      )}
      >{title}
      </div>
      <div className={clsx(
        'font-mono text-xs',
        hasBackground ? 'text-white/80' : 'text-default-500'
      )}
      >
        {value}
        {unit && <span className='ml-0.5 opacity-70'>{unit}</span>}
      </div>
    </div>
  );
};

interface ProgressBarProps {
  value: number;
  label: string;
  percentText: string;
  hasBackground?: boolean;
}

const ProgressBar: React.FC<ProgressBarProps> = ({
  value,
  label,
  percentText,
  hasBackground = false,
}) => {
  return (
    <div>
      <div className='flex justify-between text-xs mb-1'>
        <span className={hasBackground ? 'text-white/80' : 'text-default-500'}>{label}</span>
        <span className={clsx('font-mono', hasBackground ? 'text-white/80' : 'text-default-500')}>
          {percentText}
        </span>
      </div>
      <div
        className={clsx(
          'h-2 rounded-full overflow-hidden',
          hasBackground ? 'bg-white/20' : 'bg-default-200 dark:bg-default-700'
        )}
      >
        <div
          className='h-full rounded-full transition-all duration-300 bg-primary-500'
          style={{ width: `${Math.min(100, Math.max(0, value))}%` }}
        />
      </div>
    </div>
  );
};

const SystemStatusCard: React.FC<SystemStatusCardProps> = ({ setArchInfo }) => {
  const [systemStatus, setSystemStatus] = useLocalStorage<SystemStatus | undefined>('napcat_system_status_cache', undefined);
  const isSetted = useRef(false);
  const [backgroundImage] = useLocalStorage<string>(key.backgroundImage, '');
  const hasBackground = !!backgroundImage;

  const getStatus = useCallback(() => {
    try {
      const event = WebUIManager.getSystemStatus(setSystemStatus);
      return event;
    } catch (_error) {
      console.error('获取系统状态失败');
    }
  }, []);

  useEffect(() => {
    const close = getStatus();
    return () => {
      close?.close();
    };
  }, [getStatus]);

  useEffect(() => {
    if (systemStatus?.arch && !isSetted.current) {
      setArchInfo(systemStatus.arch);
      isSetted.current = true;
    }
  }, [systemStatus, setArchInfo]);

  const memoryUsagePercent = systemStatus
    ? (Number(systemStatus.memory.usage.system) / (Number(systemStatus.memory.total) || 1)) * 100
    : 0;

  return (
    <Card className={clsx(
      'backdrop-blur-sm border border-white/40 dark:border-white/10 shadow-sm relative overflow-hidden flex-1',
      hasBackground ? 'bg-white/10 dark:bg-black/10' : 'bg-white/60 dark:bg-black/40'
    )}
    >
      <div className='absolute h-full right-0 top-0'>
        <Image
          src={bkg}
          alt='background'
          className='select-none pointer-events-none !opacity-30 w-full h-full'
          classNames={{
            wrapper: 'w-full h-full',
            img: 'object-contain w-full h-full',
          }}
        />
      </div>
      <CardBody className='overflow-visible gap-4 items-stretch z-10 p-4'>
        <div className='flex-1 w-full'>
          <h2 className={clsx(
            'text-lg font-semibold flex items-center gap-2 mb-3',
            hasBackground ? 'text-white drop-shadow-sm' : 'text-default-700 dark:text-gray-200'
          )}
          >
            <GiCpu className='text-xl opacity-80' />
            <span>CPU</span>
          </h2>
          <div className='grid grid-cols-2 gap-2 mb-4'>
            <SystemStatusItem title='型号' value={systemStatus?.cpu.model} size='lg' hasBackground={hasBackground} />
            <SystemStatusItem title='内核数' value={systemStatus?.cpu.core} hasBackground={hasBackground} />
            <SystemStatusItem title='主频' value={systemStatus?.cpu.speed} unit='GHz' hasBackground={hasBackground} />
          </div>
          <ProgressBar
            value={Number(systemStatus?.cpu.usage.system) || 0}
            label='使用率'
            percentText={`${systemStatus?.cpu.usage.system || 0}%`}
            hasBackground={hasBackground}
          />
        </div>
        <div className='flex-1 w-full'>
          <h2 className={clsx(
            'text-lg font-semibold flex items-center gap-2 mb-3',
            hasBackground ? 'text-white drop-shadow-sm' : 'text-default-700 dark:text-gray-200'
          )}
          >
            <BiSolidMemoryCard className='text-xl opacity-80' />
            <span>内存</span>
          </h2>
          <div className='grid grid-cols-2 gap-2 mb-4'>
            <SystemStatusItem
              title='总量'
              value={systemStatus?.memory.total}
              size='lg'
              unit='MB'
              hasBackground={hasBackground}
            />
            <SystemStatusItem
              title='使用量'
              value={systemStatus?.memory.usage.system}
              unit='MB'
              hasBackground={hasBackground}
            />
          </div>
          <ProgressBar
            value={memoryUsagePercent}
            label='使用率'
            percentText={`${memoryUsagePercent.toFixed(1)}%`}
            hasBackground={hasBackground}
          />
        </div>
      </CardBody>
    </Card>
  );
};

interface VersionInfo {
  name: string;
  version: string;
  description: string;
}

interface VersionDirectory {
  name: string;
  url: string;
  lastModified: string;
  sha256: string | null;
  canView: boolean;
  canDownload: boolean;
}

interface VersionListResponse {
  directories: VersionDirectory[];
  files: unknown[];
  currentPath: string;
  directoryHash: string;
}

const VersionCard: React.FC = () => {
  const [backgroundImage] = useLocalStorage<string>(key.backgroundImage, '');
  const hasBackground = !!backgroundImage;
  const [checking, setChecking] = useState(false);
  const [latestVersion, setLatestVersion] = useState<VersionDirectory | null>(null);
  const [hasUpdate, setHasUpdate] = useState<boolean | null>(null);

  const { data: versionInfo, loading: versionLoading } = useRequest(WebUIManager.GetNapCatVersion);

  const getPlatformArch = () => {
    const platform = navigator.platform.toLowerCase();
    const userAgent = navigator.userAgent.toLowerCase();
    
    if (platform.includes('win') || userAgent.includes('windows')) {
      if (userAgent.includes('wow64') || userAgent.includes('win64') || userAgent.includes('x64')) {
        return 'win64';
      }
      return 'win86';
    }
    if (platform.includes('mac') || platform.includes('darwin') || userAgent.includes('mac')) {
      return 'mac';
    }
    if (platform.includes('linux') || userAgent.includes('linux')) {
      return 'linux';
    }
    return 'win64';
  };

  const parseVersion = (versionStr: string): string[] => {
    const match = versionStr.match(/v(\d+)\.(\d+)(?:\.(\d+))?/);
    if (match) {
      return [match[1], match[2], match[3] || '0'];
    }
    return ['0', '0', '0'];
  };

  const compareVersions = (v1: string, v2: string): number => {
    const parts1 = parseVersion(v1);
    const parts2 = parseVersion(v2);
    
    for (let i = 0; i < 3; i++) {
      const num1 = parseInt(parts1[i], 10);
      const num2 = parseInt(parts2[i], 10);
      if (num1 > num2) return 1;
      if (num1 < num2) return -1;
    }
    return 0;
  };

  const checkUpdate = async () => {
    setChecking(true);
    try {
      const result = await WebUIManager.CheckUpdate();
      if (result.success && result.data) {
        const versionList: VersionListResponse = JSON.parse(result.data);
        const arch = getPlatformArch();
        
        const releaseVersions = versionList.directories.filter(d => 
          d.name.includes('release') && 
          !d.name.includes('debug') &&
          d.name.includes(arch)
        );
        
        if (releaseVersions.length > 0) {
          const latest = releaseVersions.reduce((prev, current) => {
            const prevVersion = parseVersion(prev.name);
            const currentVersion = parseVersion(current.name);
            
            for (let i = 0; i < 3; i++) {
              const prevNum = parseInt(prevVersion[i], 10);
              const currentNum = parseInt(currentVersion[i], 10);
              if (currentNum > prevNum) return current;
              if (currentNum < prevNum) return prev;
            }
            return prev;
          });
          
          setLatestVersion(latest);
          
          if (versionInfo?.version) {
            const currentVersion = `v${versionInfo.version}`;
            const comparison = compareVersions(latest.name, currentVersion);
            setHasUpdate(comparison > 0);
          }
        } else {
          setLatestVersion(null);
          setHasUpdate(null);
        }
      }
    } catch (error) {
      console.error('检查更新失败:', error);
    } finally {
      setChecking(false);
    }
  };

  return (
    <Card className={clsx(
      'backdrop-blur-sm border border-white/40 dark:border-white/10 shadow-sm',
      hasBackground ? 'bg-white/10 dark:bg-black/10' : 'bg-white/60 dark:bg-black/40'
    )}
    >
      <CardBody className='p-4'>
        <div className='flex items-center justify-between mb-3'>
          <h2 className={clsx(
            'text-lg font-semibold flex items-center gap-2',
            hasBackground ? 'text-white drop-shadow-sm' : 'text-default-700 dark:text-gray-200'
          )}
          >
            <span>MorningCat 版本</span>
          </h2>
          <Button
            size='sm'
            variant='flat'
            color='primary'
            onPress={checkUpdate}
            isLoading={checking}
            startContent={!checking && <IoRefresh className='text-base' />}
          >
            检查更新
          </Button>
        </div>
        
        <div className='space-y-2'>
          <div className='flex justify-between items-center'>
            <span className={clsx('text-sm', hasBackground ? 'text-white/80' : 'text-default-500')}>
              当前版本
            </span>
            <span className={clsx('font-mono text-sm', hasBackground ? 'text-white/90' : 'text-default-700')}>
              {versionLoading ? '加载中...' : `v${versionInfo?.version || '1.0.0'}`}
            </span>
          </div>
          
          {latestVersion && (
            <>
              <div className='flex justify-between items-center'>
                <span className={clsx('text-sm', hasBackground ? 'text-white/80' : 'text-default-500')}>
                  最新版本
                </span>
                <span className={clsx('font-mono text-sm flex items-center gap-1', hasBackground ? 'text-white/90' : 'text-default-700')}>
                  {(() => {
                    const versionMatch = latestVersion.name.match(/v(\d+\.\d+(?:\.\d+)?)/);
                    const versionNum = versionMatch ? versionMatch[1] : '0.0.0';
                    const arch = getPlatformArch();
                    return `v${versionNum}-release-${arch}`;
                  })()}
                  {hasUpdate === true && (
                    <IoAlertCircle className='text-warning-500' title='有新版本可用' />
                  )}
                  {hasUpdate === false && (
                    <IoCheckmarkCircle className='text-success-500' title='已是最新版本' />
                  )}
                </span>
              </div>
              <div className='flex justify-between items-center'>
                <span className={clsx('text-sm', hasBackground ? 'text-white/80' : 'text-default-500')}>
                  发布时间
                </span>
                <span className={clsx('font-mono text-xs', hasBackground ? 'text-white/80' : 'text-default-500')}>
                  {latestVersion.lastModified}
                </span>
              </div>
            </>
          )}
        </div>
      </CardBody>
    </Card>
  );
};

const DashboardIndexPage: React.FC = () => {
  const [archInfo, setArchInfo] = useLocalStorage<string | undefined>('napcat_arch_info_cache', undefined);

  return (
    <>
      <title>基础信息 - MorningCat WebUI</title>
      <section className='w-full p-2 md:p-4 md:max-w-[1000px] mx-auto overflow-hidden'>
        <div className='grid grid-cols-1 lg:grid-cols-2 gap-4 items-stretch'>
          <QQInfo />
          <SystemStatusCard setArchInfo={setArchInfo} />
        </div>
        <div className='mt-4'>
          <VersionCard />
        </div>
        <div className='w-full text-right mt-4'>
          <p className='text-sm text-default-400 italic'>
            WebUI借鉴NapCat的设计，有能力请支持原作者
          </p>
        </div>
      </section>
    </>
  );
};

export default DashboardIndexPage;
