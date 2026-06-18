import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import toast from 'react-hot-toast';

import SaveButtons from '@/components/button/save_buttons';
import PageLoading from '@/components/page_loading';
import SwitchCard from '@/components/switch_card';

import QQManager from '@/controllers/qq_manager';
import useI18n from '@/hooks/use-i18n';

interface BypassFormData {
  hook: boolean;
  window: boolean;
  module: boolean;
  process: boolean;
  container: boolean;
  js: boolean;
  o3HookMode: boolean;
}

const BypassConfigCard = () => {
  const { t } = useI18n();
  const [loading, setLoading] = useState(true);
  const {
    control,
    handleSubmit,
    formState: { isSubmitting },
    setValue,
  } = useForm<BypassFormData>();

  const loadConfig = async (showTip = false) => {
    try {
      setLoading(true);
      const config = await QQManager.getNapCatConfig();
      const bypass = config.bypass ?? {} as Partial<BypassOptions>;
      setValue('hook', bypass.hook ?? false);
      setValue('window', bypass.window ?? false);
      setValue('module', bypass.module ?? false);
      setValue('process', bypass.process ?? false);
      setValue('container', bypass.container ?? false);
      setValue('js', bypass.js ?? false);
      setValue('o3HookMode', config.o3HookMode === 1);
      if (showTip) toast.success(t('webui.bypass.refresh_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.bypass.fetch_failed', msg));
    } finally {
      setLoading(false);
    }
  };

  const onSubmit = handleSubmit(async (data) => {
    try {
      const { o3HookMode, ...bypass } = data;
      await QQManager.setNapCatConfig({ bypass, o3HookMode: o3HookMode ? 1 : 0 });
      toast.success(t('webui.bypass.save_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.bypass.save_failed', msg));
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
      <title>{t('webui.config.bypass.title')}</title>
      <div className='flex flex-col gap-1 mb-2'>
        <h3 className='text-lg font-semibold text-default-700'>{t('webui.bypass.heading')}</h3>
        <p className='text-sm text-default-500'>
          {t('webui.bypass.description')}
        </p>
      </div>
      <div className='grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3'>
        <Controller
          control={control}
          name='hook'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.bypass.hook')}
              description={t('webui.bypass.hook_desc')}
            />
          )}
        />
        <Controller
          control={control}
          name='window'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.bypass.window')}
              description={t('webui.bypass.window_desc')}
            />
          )}
        />
        <Controller
          control={control}
          name='module'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.bypass.module')}
              description={t('webui.bypass.module_desc')}
            />
          )}
        />
        <Controller
          control={control}
          name='process'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.bypass.process')}
              description={t('webui.bypass.process_desc')}
            />
          )}
        />
        <Controller
          control={control}
          name='container'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.bypass.container')}
              description={t('webui.bypass.container_desc')}
            />
          )}
        />
        <Controller
          control={control}
          name='js'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.bypass.js')}
              description={t('webui.bypass.js_desc')}
            />
          )}
        />
        <Controller
          control={control}
          name='o3HookMode'
          render={({ field }) => (
            <SwitchCard
              {...field}
              label={t('webui.bypass.o3hook')}
              description={t('webui.bypass.o3hook_desc')}
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

export default BypassConfigCard;
