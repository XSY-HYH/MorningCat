import { useLocalStorage } from '@uidotdev/usehooks';
import clsx from 'clsx';
import { useEffect, useRef } from 'react';
import toast from 'react-hot-toast';

import key from '@/const/key';

import WebUIManager from '@/controllers/webui_manager';

import type { XTermRef } from '@/components/xterm';
import XTerm from '@/components/xterm';

export default function LogsPage () {
  const Xterm = useRef<XTermRef>(null);
  const [backgroundImage] = useLocalStorage<string>(key.backgroundImage, '');
  const hasBackground = !!backgroundImage;

  useEffect(() => {
    const subscribeLogs = () => {
      try {
        const source = WebUIManager.getRealTimeLogs((data) => {
          data.forEach((log) => {
            Xterm.current?.write(log.raw);
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
      <title>猫猫日志 - MorningCat WebUI</title>
      <div className='h-[calc(100vh_-_8rem)] flex flex-col gap-4 items-center pt-4 px-2'>
        <div className={clsx(
          'w-full flex-1 h-full overflow-hidden rounded-2xl border backdrop-blur-sm transition-all shadow-sm',
          hasBackground ? 'bg-white/20 dark:bg-black/10 border-white/40 dark:border-white/10' : 'bg-white/60 dark:bg-black/40 border-white/40 dark:border-white/10'
        )}
        >
          <XTerm ref={Xterm} />
        </div>
      </div>
    </>
  );
}
