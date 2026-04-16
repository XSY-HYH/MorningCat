import { Button } from '@heroui/button';
import { useLocalStorage } from '@uidotdev/usehooks';
import clsx from 'clsx';
import { useEffect, useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { IoDownloadOutline } from 'react-icons/io5';

import key from '@/const/key';

import WebUIManager, { RawLog } from '@/controllers/webui_manager';

import type { XTermRef } from '../xterm';
import XTerm from '../xterm';

const RealTimeLogs = () => {
  const Xterm = useRef<XTermRef>(null);
  const [dataArr, setDataArr] = useState<RawLog[]>([]);
  const [backgroundImage] = useLocalStorage<string>(key.backgroundImage, '');
  const hasBackground = !!backgroundImage;

  const onDownloadLog = () => {
    const logContent = dataArr
      .map((log) => log.raw.replace(/\u001b\[[0-9;]*m/g, ''))
      .join('\r\n');
    const blob = new Blob([logContent], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'morningcat.log';
    a.click();
    URL.revokeObjectURL(url);
  };

  const writeStream = () => {
    try {
      Xterm.current?.clear();
      const _data = dataArr
        .map((log) => log.raw.replace(/\n/g, '\r\n'))
        .join('');
      Xterm.current?.write(_data);
    } catch (error) {
      console.error(error);
      toast.error('获取实时日志失败');
    }
  };

  useEffect(() => {
    writeStream();
  }, [dataArr]);

  useEffect(() => {
    const subscribeLogs = () => {
      try {
        const source = WebUIManager.getRealTimeLogs((data) => {
          setDataArr((prev) => {
            const newData = [...prev, ...data];
            if (newData.length > 1000) {
              newData.splice(0, newData.length - 1000);
            }
            return newData;
          });
        });
        return () => {
          source.close();
        };
      } catch (_error) {
        toast.error('获取实时日志失败');
      }
    };

    const close = subscribeLogs();
    return () => {
      console.log('close');
      close?.();
    };
  }, []);

  return (
    <>
      <title>实时日志 - MorningCat WebUI</title>
      <div className={clsx(
        'flex items-center gap-2 p-2 rounded-2xl border backdrop-blur-sm transition-all shadow-sm mb-4',
        hasBackground ? 'bg-white/20 dark:bg-black/10 border-white/40 dark:border-white/10' : 'bg-white/60 dark:bg-black/40 border-white/40 dark:border-white/10'
      )}
      >
        <Button
          className='flex-shrink-0'
          onPress={onDownloadLog}
          startContent={<IoDownloadOutline className='text-lg' />}
          color='primary'
          variant='flat'
        >
          下载日志
        </Button>
      </div>
      <div className='flex-1 h-full overflow-hidden'>
        <XTerm ref={Xterm} />
      </div>
    </>
  );
};

export default RealTimeLogs;
