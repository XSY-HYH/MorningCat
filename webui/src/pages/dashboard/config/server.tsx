import { Input } from '@heroui/input';
import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import toast from 'react-hot-toast';

import SaveButtons from '@/components/button/save_buttons';
import PageLoading from '@/components/page_loading';
import SwitchCard from '@/components/switch_card';

import QQManager from '@/controllers/qq_manager';
import useI18n from '@/hooks/use-i18n';

interface ServerFormData {
  httpPort: number;
  httpHosts: string;
  httpEnable: boolean;
  httpSecret: string;
  wsPort: number;
  wsHosts: string;
  wsEnable: boolean;
  reverseWsUrl: string;
  reverseWsReconnectInterval: number;
  reverseWsEnable: boolean;
  debug: boolean;
  heartInterval: number;
  messagePostFormat: string;
  enableLocalFile2Url: boolean;
  musicSignUrl: string;
  reportSelfMessage: boolean;
  token: string;
}

const ServerConfigCard = () => {
  const { t } = useI18n();
  const [loading, setLoading] = useState(true);
  const {
    control,
    handleSubmit,
    formState: { isSubmitting },
    setValue,
  } = useForm<ServerFormData>();

  const loadConfig = async (showTip = false) => {
    try {
      setLoading(true);
      const config = await QQManager.getOB11Config();
      setValue('httpPort', config.httpPort ?? 3000);
      setValue('httpHosts', config.httpHosts ?? '');
      setValue('httpEnable', config.httpEnable ?? false);
      setValue('httpSecret', config.httpSecret ?? '');
      setValue('wsPort', config.wsPort ?? 3001);
      setValue('wsHosts', config.wsHosts ?? '');
      setValue('wsEnable', config.wsEnable ?? false);
      setValue('reverseWsUrl', config.reverseWsUrl ?? '');
      setValue('reverseWsReconnectInterval', config.reverseWsReconnectInterval ?? 3000);
      setValue('reverseWsEnable', config.reverseWsEnable ?? false);
      setValue('debug', config.debug ?? false);
      setValue('heartInterval', config.heartInterval ?? 30000);
      setValue('messagePostFormat', config.messagePostFormat ?? 'array');
      setValue('enableLocalFile2Url', config.enableLocalFile2Url ?? false);
      setValue('musicSignUrl', config.musicSignUrl ?? '');
      setValue('reportSelfMessage', config.reportSelfMessage ?? false);
      setValue('token', config.token ?? '');
      if (showTip) toast.success(t('webui.server.refresh_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.server.fetch_failed', msg));
    } finally {
      setLoading(false);
    }
  };

  const onSubmit = handleSubmit(async (data) => {
    try {
      await QQManager.setOB11Config({
        ...data,
        httpHosts: data.httpHosts ? data.httpHosts.split(',').map((s: string) => s.trim()) : [],
        wsHosts: data.wsHosts ? data.wsHosts.split(',').map((s: string) => s.trim()) : [],
      });
      toast.success(t('webui.server.save_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.server.save_failed', msg));
    }
  });

  const onReset = () => {
    loadConfig();
  };

  const onRefresh = async () => {
    await loadConfig(true);
  };

  useEffect(() => {
    loadConfig();
  }, []);

  if (loading) return <PageLoading loading />;

  return (
    <>
      <title>{t('webui.config.server.title')}</title>
      <div className='flex flex-col gap-1 mb-2'>
        <h3 className='text-lg font-semibold text-default-700'>{t('webui.server.heading')}</h3>
        <p className='text-sm text-default-500'>
          {t('webui.server.description')}
        </p>
      </div>

      <div className='flex-shrink-0 w-full font-bold text-default-600 dark:text-default-400 px-1 mt-4'>{t('webui.server.http_group')}</div>
      <Controller
        control={control}
        name='httpEnable'
        render={({ field }) => (
          <SwitchCard
            {...field}
            label={t('webui.server.http_enable')}
            description={t('webui.server.http_enable_desc')}
          />
        )}
      />
      <Controller
        control={control}
        name='httpPort'
        render={({ field }) => (
          <Input
            {...field}
            type='number'
            label={t('webui.server.http_port')}
            value={field.value?.toString() ?? ''}
            onChange={(e) => field.onChange(parseInt(e.target.value) || 0)}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />
      <Controller
        control={control}
        name='httpHosts'
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.server.http_hosts')}
            placeholder={t('webui.server.http_hosts_placeholder')}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />
      <Controller
        control={control}
        name='httpSecret'
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.server.http_secret')}
            placeholder={t('webui.server.http_secret_placeholder')}
            type='password'
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />

      <div className='flex-shrink-0 w-full font-bold text-default-600 dark:text-default-400 px-1 mt-4'>{t('webui.server.ws_group')}</div>
      <Controller
        control={control}
        name='wsEnable'
        render={({ field }) => (
          <SwitchCard
            {...field}
            label={t('webui.server.ws_enable')}
            description={t('webui.server.ws_enable_desc')}
          />
        )}
      />
      <Controller
        control={control}
        name='wsPort'
        render={({ field }) => (
          <Input
            {...field}
            type='number'
            label={t('webui.server.ws_port')}
            value={field.value?.toString() ?? ''}
            onChange={(e) => field.onChange(parseInt(e.target.value) || 0)}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />
      <Controller
        control={control}
        name='wsHosts'
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.server.ws_hosts')}
            placeholder={t('webui.server.ws_hosts_placeholder')}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />

      <div className='flex-shrink-0 w-full font-bold text-default-600 dark:text-default-400 px-1 mt-4'>{t('webui.server.reverse_ws_group')}</div>
      <Controller
        control={control}
        name='reverseWsEnable'
        render={({ field }) => (
          <SwitchCard
            {...field}
            label={t('webui.server.reverse_ws_enable')}
            description={t('webui.server.reverse_ws_enable_desc')}
          />
        )}
      />
      <Controller
        control={control}
        name='reverseWsUrl'
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.server.reverse_ws_url')}
            placeholder={t('webui.server.reverse_ws_url_placeholder')}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />
      <Controller
        control={control}
        name='reverseWsReconnectInterval'
        render={({ field }) => (
          <Input
            {...field}
            type='number'
            label={t('webui.server.reverse_ws_reconnect')}
            value={field.value?.toString() ?? ''}
            onChange={(e) => field.onChange(parseInt(e.target.value) || 0)}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />

      <div className='flex-shrink-0 w-full font-bold text-default-600 dark:text-default-400 px-1 mt-4'>{t('webui.server.general_group')}</div>
      <Controller
        control={control}
        name='debug'
        render={({ field }) => (
          <SwitchCard
            {...field}
            label={t('webui.server.debug')}
            description={t('webui.server.debug_desc')}
          />
        )}
      />
      <Controller
        control={control}
        name='heartInterval'
        render={({ field }) => (
          <Input
            {...field}
            type='number'
            label={t('webui.server.heart_interval')}
            value={field.value?.toString() ?? ''}
            onChange={(e) => field.onChange(parseInt(e.target.value) || 0)}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />
      <Controller
        control={control}
        name='messagePostFormat'
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.server.message_post_format')}
            placeholder='array'
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />
      <Controller
        control={control}
        name='reportSelfMessage'
        render={({ field }) => (
          <SwitchCard
            {...field}
            label={t('webui.server.report_self_message')}
            description={t('webui.server.report_self_message_desc')}
          />
        )}
      />
      <Controller
        control={control}
        name='token'
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.server.access_token')}
            placeholder={t('webui.server.access_token_placeholder')}
            type='password'
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400',
            }}
          />
        )}
      />

      <SaveButtons
        onSubmit={onSubmit}
        reset={onReset}
        isSubmitting={isSubmitting}
        refresh={onRefresh}
      />
    </>
  );
};

export default ServerConfigCard;
