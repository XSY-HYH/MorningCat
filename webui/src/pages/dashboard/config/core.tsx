import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import toast from 'react-hot-toast';

import SaveButtons from '@/components/button/save_buttons';
import PageLoading from '@/components/page_loading';
import SwitchCard from '@/components/switch_card';

import QQManager from '@/controllers/qq_manager';
import useI18n from '@/hooks/use-i18n';

interface CoreFormData {
  fileLog: boolean;
  consoleLog: boolean;
  autoTimeSync: boolean;
}

const CoreConfigCard = () => {
  const { t } = useI18n();
  const [loading, setLoading] = useState(true);
  const {
    control,
    handleSubmit,
    formState: { isSubmitting },
    setValue,
  } = useForm<CoreFormData>();

  const loadConfig = async (showTip = false) => {
    try {
      setLoading(true);
      const config = await QQManager.getNapCatUinConfig();
      setValue('fileLog', config.fileLog ?? false);
      setValue('consoleLog', config.consoleLog ?? true);
      setValue('autoTimeSync', config.autoTimeSync ?? true);
      if (showTip) toast.success(t('webui.core.refresh_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.core.fetch_failed', msg));
    } finally {
      setLoading(false);
    }
  };

  const onSubmit = handleSubmit(async (data) => {
    try {
      await QQManager.setNapCatUinConfig(data);
      toast.success(t('webui.core.save_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.core.save_failed', msg));
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
      <title>{t('webui.config.core.title')}</title>
      <div className='flex flex-col gap-1 mb-2'>
        <h3 className='text-lg font-semibold text-default-700'>{t('webui.core.heading')}</h3>
        <p className='text-sm text-default-500'>
          {t('webui.core.description')}
        </p>
      </div>
      <div className='grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3'>
        <Controller
          control={control}
          name='autoTimeSync'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.core.auto_time_sync')}
              description={t('webui.core.auto_time_sync_desc')}
            />
          )}
        />
        <Controller
          control={control}
          name='fileLog'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.core.file_log')}
              description={t('webui.core.file_log_desc')}
            />
          )}
        />
        <Controller
          control={control}
          name='consoleLog'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.core.console_log')}
              description={t('webui.core.console_log_desc')}
            />
          )}
        />
      </div>
      <SaveButtons
        onSubmit={onSubmit}
        reset={onReset}
        isSubmitting={isSubmitting}
        refresh={onRefresh}
      />
    </>
  );
};

export default CoreConfigCard;
