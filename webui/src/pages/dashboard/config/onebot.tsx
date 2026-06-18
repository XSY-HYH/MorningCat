import { Input } from '@heroui/input';
import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import toast from 'react-hot-toast';

import SaveButtons from '@/components/button/save_buttons';
import PageLoading from '@/components/page_loading';
import SwitchCard from '@/components/switch_card';

import useConfig from '@/hooks/use-config';
import useI18n from '@/hooks/use-i18n';

const OneBotConfigCard = () => {
  const { t } = useI18n();
  const { config, saveConfigWithoutNetwork, refreshConfig } = useConfig();
  const [loading, setLoading] = useState(false);
  const {
    control,
    handleSubmit: handleOnebotSubmit,
    formState: { isSubmitting },
    setValue: setOnebotValue,
  } = useForm<IConfig['onebot']>({
    defaultValues: {
      musicSignUrl: '',
      enableLocalFile2Url: false,
      parseMultMsg: false,
      imageDownloadProxy: '',
      timeout: {
        baseTimeout: 10000,
        uploadSpeedKBps: 256,
        downloadSpeedKBps: 256,
        maxTimeout: 1800000,
      },
    },
  });
  const reset = () => {
    setOnebotValue('musicSignUrl', config.musicSignUrl);
    setOnebotValue('enableLocalFile2Url', config.enableLocalFile2Url);
    setOnebotValue('parseMultMsg', config.parseMultMsg);
    setOnebotValue('imageDownloadProxy', config.imageDownloadProxy);
    setOnebotValue('timeout', config.timeout ?? {
      baseTimeout: 10000,
      uploadSpeedKBps: 256,
      downloadSpeedKBps: 1000,
      maxTimeout: 1800000,
    });
  };

  const onSubmit = handleOnebotSubmit(async (data) => {
    try {
      await saveConfigWithoutNetwork(data);
      toast.success(t('webui.onebot.save_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.onebot.save_failed', msg));
    }
  });

  const onRefresh = async (shotTip = true) => {
    try {
      setLoading(true);
      await refreshConfig();
      if (shotTip) toast.success(t('webui.onebot.refresh_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.onebot.refresh_failed', msg));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    reset();
  }, [config]);

  useEffect(() => {
    onRefresh(false);
  }, []);

  if (loading) return <PageLoading loading />;

  return (
    <>
      <title>{t('webui.config.onebot.title')}</title>
      <Controller
        control={control}
        name='musicSignUrl'
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.onebot.music_sign_url')}
            placeholder={t('webui.onebot.music_sign_placeholder')}
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
        name='enableLocalFile2Url'
        render={({ field }) => (
          <SwitchCard
            {...field}
            description={t('webui.onebot.enable_local_file2url')}
            label={t('webui.onebot.enable_local_file2url')}
          />
        )}
      />
      <Controller
        control={control}
        name='parseMultMsg'
        render={({ field }) => (
          <SwitchCard
            {...field}
            description={t('webui.onebot.parse_mult_msg')}
            label={t('webui.onebot.parse_mult_msg')}
          />
        )}
      />
      <Controller
        control={control}
        name='imageDownloadProxy'
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.onebot.image_proxy')}
            placeholder={t('webui.onebot.image_proxy_placeholder')}
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
        name='timeout.baseTimeout'
        render={({ field }) => (
          <Input
            {...field}
            type='number'
            label={t('webui.onebot.base_timeout')}
            placeholder='10000'
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
        name='timeout.uploadSpeedKBps'
        render={({ field }) => (
          <Input
            {...field}
            type='number'
            label={t('webui.onebot.upload_speed')}
            placeholder='256'
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
        name='timeout.downloadSpeedKBps'
        render={({ field }) => (
          <Input
            {...field}
            type='number'
            label={t('webui.onebot.download_speed')}
            placeholder='1000'
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
        name='timeout.maxTimeout'
        render={({ field }) => (
          <Input
            {...field}
            type='number'
            label={t('webui.onebot.max_timeout')}
            placeholder='1800000'
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
      <SaveButtons
        onSubmit={onSubmit}
        reset={reset}
        isSubmitting={isSubmitting}
        refresh={onRefresh}
      />
    </>
  );
};

export default OneBotConfigCard;
