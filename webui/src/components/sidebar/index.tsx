import { Button } from '@heroui/button';
import { useLocalStorage } from '@uidotdev/usehooks';
import clsx from 'clsx';
import { AnimatePresence, motion } from 'motion/react';
import React from 'react';
import { IoMdLogOut, IoMdRefresh, IoMdPower } from 'react-icons/io';

import key from '@/const/key';
import useAuth from '@/hooks/auth';
import useDialog from '@/hooks/use-dialog';
import type { MenuItem } from '@/config/site';
import ProcessManager from '@/controllers/process_manager';

import Menus from './menus';

interface SideBarProps {
  open: boolean;
  items: MenuItem[];
  onClose?: () => void;
}

const SideBar: React.FC<SideBarProps> = (props) => {
  const { open, items, onClose } = props;
  const { revokeAuth } = useAuth();
  const dialog = useDialog();
  const [backgroundImage] = useLocalStorage<string>(key.backgroundImage, '');
  const hasBackground = !!backgroundImage;

  const onRevokeAuth = () => {
    dialog.confirm({
      title: '退出登录',
      content: '确定要退出登录吗？',
      onConfirm: revokeAuth,
    });
  };

  const onRestart = () => {
    dialog.confirm({
      title: '重启应用',
      content: '确定要重启 MorningCat 吗？重启后 WebUI 将暂时无法访问。',
      onConfirm: async () => {
        try {
          await ProcessManager.restartProcess();
        } catch (e) {
          console.error('Restart failed:', e);
        }
      },
    });
  };

  const onShutdown = () => {
    dialog.confirm({
      title: '停止运行',
      content: '确定要停止 MorningCat 吗？停止后需要手动重新启动。',
      onConfirm: async () => {
        try {
          await ProcessManager.shutdownProcess();
        } catch (e) {
          console.error('Shutdown failed:', e);
        }
      },
    });
  };

  return (
    <>
      <AnimatePresence initial={false}>
        {open && (
          <motion.div
            className='fixed inset-y-0 left-64 right-0 bg-black/20 backdrop-blur-[1px] z-40 md:hidden'
            aria-hidden='true'
            onClick={onClose}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0, transition: { duration: 0.15 } }}
            transition={{ duration: 0.2, delay: 0.15 }}
          />
        )}
      </AnimatePresence>
      <motion.div
        className={clsx(
          'overflow-hidden fixed top-0 left-0 h-full z-50 md:static md:shadow-none rounded-r-2xl md:rounded-none',
          hasBackground
            ? 'bg-transparent backdrop-blur-md'
            : 'bg-content1/70 backdrop-blur-xl backdrop-saturate-150 shadow-xl',
          'md:bg-transparent md:backdrop-blur-none md:backdrop-saturate-100 md:shadow-none'
        )}
        initial={{ width: 0 }}
        animate={{ width: open ? '16rem' : 0 }}
        transition={{
          type: open ? 'spring' : 'tween',
          stiffness: 150,
          damping: open ? 15 : 10,
        }}
        style={{ overflow: 'hidden' }}
      >
        <motion.div className='w-64 flex flex-col items-stretch h-full transition-transform duration-300 ease-in-out z-30 relative float-right p-4'>
          <div className='flex items-center justify-start gap-3 px-2 my-8 ml-2'>
            <div className='h-5 w-1 bg-primary rounded-full shadow-sm' />
            <div className={clsx(
              'text-xl font-bold tracking-wide select-none',
              hasBackground ? 'text-white' : 'text-default-900 dark:text-white'
            )}
            >
              MorningCat
            </div>
          </div>
          <div className='overflow-y-auto flex flex-col flex-1 px-2'>
            <Menus items={items} />
            <div className='mt-auto mb-10 md:mb-0 space-y-2 px-2'>
              <Button
                className='w-full bg-warning-50/50 hover:bg-warning-100/80 text-warning-600 font-medium shadow-sm hover:shadow-md transition-all duration-300 backdrop-blur-sm'
                radius='full'
                variant='flat'
                onPress={onRestart}
                startContent={<IoMdRefresh size={18} />}
              >
                重启应用
              </Button>
              <Button
                className='w-full bg-danger-50/50 hover:bg-danger-100/80 text-danger-500 font-medium shadow-sm hover:shadow-md transition-all duration-300 backdrop-blur-sm'
                radius='full'
                variant='flat'
                onPress={onShutdown}
                startContent={<IoMdPower size={18} />}
              >
                停止运行
              </Button>
              <Button
                className='w-full bg-default-50/50 hover:bg-default-100/80 text-default-500 font-medium shadow-sm hover:shadow-md transition-all duration-300 backdrop-blur-sm'
                radius='full'
                variant='flat'
                onPress={onRevokeAuth}
                startContent={<IoMdLogOut size={18} />}
              >
                退出登录
              </Button>
            </div>
          </div>
        </motion.div>
      </motion.div>
    </>
  );
};

export default SideBar;
