import { Input } from '@heroui/input';
import { Button } from '@heroui/button';
import { Divider } from '@heroui/divider';
import { Avatar } from '@heroui/avatar';
import { useRequest } from 'ahooks';
import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import toast from 'react-hot-toast';

import SaveButtons from '@/components/button/save_buttons';
import GUIDManager from '@/components/guid_manager';
import PageLoading from '@/components/page_loading';

import QQManager from '@/controllers/qq_manager';
import ProcessManager from '@/controllers/process_manager';
import { waitForBackendReady } from '@/utils/process_utils';
import useI18n from '@/hooks/use-i18n';

const LoginConfigCard = () => {
  const { t } = useI18n();
  const [isRestarting, setIsRestarting] = useState(false);
  const [loginList, setLoginList] = useState<LoginListItem[]>([]);
  const [loginListLoading, setLoginListLoading] = useState(false);
  const {
    data: quickLoginData,
    loading: quickLoginLoading,
    error: quickLoginError,
    refreshAsync: refreshQuickLogin,
  } = useRequest(QQManager.getQuickLoginQQ);
  const {
    control,
    handleSubmit: handleOnebotSubmit,
    formState: { isSubmitting },
    setValue: setOnebotValue,
    watch,
  } = useForm<{
    quickLoginQQ: string;
  }>({
    defaultValues: {
      quickLoginQQ: '',
    },
  });

  const currentQQ = watch('quickLoginQQ');

  const reset = () => {
    setOnebotValue('quickLoginQQ', quickLoginData ?? '');
  };

  const fetchLoginList = async () => {
    try {
      setLoginListLoading(true);
      const list = await QQManager.getQQQuickLoginListNew();
      setLoginList(list ?? []);
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.login.fetch_accounts_failed', msg));
    } finally {
      setLoginListLoading(false);
    }
  };

  const onSubmit = handleOnebotSubmit(async (data) => {
    try {
      await QQManager.setQuickLoginQQ(data.quickLoginQQ);
      toast.success(t('webui.login.save_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.login.save_failed', msg));
    }
  });

  const onRefresh = async () => {
    try {
      await refreshQuickLogin();
      toast.success(t('webui.login.refresh_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.login.refresh_failed', msg));
    }
  };

  const onRestartProcess = async () => {
    setIsRestarting(true);
    try {
      const result = await ProcessManager.restartProcess();
      toast.success(result.message || t('webui.login.restart_request_sent'));

      // 轮询探测后端是否恢复
      const isReady = await waitForBackendReady(
        30000, // 30秒超时
        () => {
          setIsRestarting(false);
          toast.success(t('webui.login.restart_success'));
        },
        () => {
          setIsRestarting(false);
          toast.error(t('webui.login.backend_timeout'));
        }
      );

      if (!isReady) {
        setIsRestarting(false);
      }
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.login.restart_failed', msg));
      setIsRestarting(false);
    }
  };

  useEffect(() => {
    reset();
  }, [quickLoginData]);

  if (quickLoginLoading) return <PageLoading loading />;

  return (
    <>
      <title>{t('webui.config.login.title')}</title>
      <div className='flex-shrink-0 w-full font-bold text-default-600 dark:text-default-400 px-1'>{t('webui.login.quick_login_qq')}</div>
      <Controller
        control={control}
        name='quickLoginQQ'
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.login.quick_login_label')}
            placeholder={t('webui.login.qq_placeholder')}
            isDisabled={!!quickLoginError}
            errorMessage={quickLoginError ? t('webui.login.fetch_failed') : undefined}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />

      <div className='flex flex-col gap-2'>
        <div className='flex items-center gap-2'>
          <Button
            size='sm'
            color='primary'
            variant='flat'
            onPress={fetchLoginList}
            isLoading={loginListLoading}
          >
            {t('webui.login.fetch_list')}
          </Button>
          {loginList.length > 0 && (
            <span className='text-xs text-default-400'>
              {t('webui.login.account_count', loginList.length)}
            </span>
          )}
        </div>
        {loginList.length > 0 && (
          <div className='grid grid-cols-1 sm:grid-cols-2 gap-2'>
            {loginList.filter(item => item.isQuickLogin).map((item) => (
              <button
                key={item.uin}
                type='button'
                onClick={() => setOnebotValue('quickLoginQQ', item.uin)}
                className={`flex items-center gap-3 p-3 rounded-xl border transition-all cursor-pointer text-left
                  ${currentQQ === item.uin
                    ? 'border-primary bg-primary/10 dark:bg-primary/20 shadow-sm'
                    : 'border-default-200 dark:border-default-100/10 bg-default-100/50 dark:bg-white/5 hover:bg-default-200/50 dark:hover:bg-white/10'
                  }`}
              >
                <Avatar
                  src={item.faceUrl}
                  name={item.nickName?.charAt(0) || item.uin.charAt(0)}
                  size='sm'
                  className='flex-shrink-0'
                />
                <div className='flex flex-col min-w-0'>
                  <span className='text-sm font-medium text-default-700 truncate'>
                    {item.nickName || t('webui.login.unknown_nickname')}
                  </span>
                  <span className='text-xs text-default-400 truncate'>
                    {item.uin}
                  </span>
                </div>
                {currentQQ === item.uin && (
                  <span className='ml-auto text-xs text-primary font-medium flex-shrink-0'>{t('webui.login.selected')}</span>
                )}
              </button>
            ))}
            {loginList.filter(item => !item.isQuickLogin).length > 0 && (
              <div className='col-span-full text-xs text-default-400 mt-1'>
                {t('webui.login.not_support_quick')}
                {loginList.filter(item => !item.isQuickLogin).map(item => item.uin).join('、')}
              </div>
            )}
          </div>
        )}
      </div>

      <SaveButtons
        onSubmit={onSubmit}
        reset={reset}
        isSubmitting={isSubmitting || quickLoginLoading}
        refresh={onRefresh}
      />
      <div className='flex-shrink-0 w-full mt-6 pt-6 border-t border-divider'>
        <div className='mb-3 text-sm text-default-600'>{t('webui.login.process_management')}</div>
        <Button
          color='warning'
          variant='flat'
          onPress={onRestartProcess}
          isLoading={isRestarting}
          isDisabled={isRestarting}
          fullWidth
        >
          {isRestarting ? t('webui.login.restarting') : t('webui.login.restart_process')}
        </Button>
        <div className='mt-2 text-xs text-default-500'>
          {t('webui.login.restart_desc')}
        </div>
      </div>
      <Divider className='mt-6' />
      <div className='flex-shrink-0 w-full mt-4'>
        <div className='mb-3 text-sm text-default-600'>{t('webui.login.guid_management')}</div>
        <div className='text-xs text-default-400 mb-3'>
          {t('webui.login.guid_desc')}
        </div>
        <GUIDManager showRestart={false} />
      </div>
    </>
  );
};

export default LoginConfigCard;
