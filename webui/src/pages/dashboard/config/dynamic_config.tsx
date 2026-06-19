import { Card, CardBody } from '@heroui/card';
import { Divider } from '@heroui/divider';
import { Input } from '@heroui/input';
import { Switch } from '@heroui/switch';
import { Tab, Tabs } from '@heroui/tabs';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useForm, Controller } from 'react-hook-form';
import toast from 'react-hot-toast';
import { useEffect, useState } from 'react';

import useConfigSchema, { ConfigItem, ConfigGroup } from '@/hooks/use-config-schema';
import useI18n from '@/hooks/use-i18n';
import SaveButtons from '@/components/button/save_buttons';
import BackupConfigCard from './backup';

export default function DynamicConfigPage () {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const tab = searchParams.get('tab') ?? '';
  const { t } = useI18n();

  const { items, groups, loading, updateConfig, reload } = useConfigSchema();
  const [saving, setSaving] = useState(false);

  const defaultValues: Record<string, any> = {};
  items.forEach(item => {
    if (item.type === 'number_array' && Array.isArray(item.value)) {
      defaultValues[item.key] = item.value.join(', ');
    } else {
      defaultValues[item.key] = item.value ?? '';
    }
  });

  const { control, handleSubmit, formState: { isSubmitting }, reset } = useForm({ defaultValues });

  useEffect(() => {
    const values: Record<string, any> = {};
    items.forEach(item => {
      if (item.type === 'number_array' && Array.isArray(item.value)) {
        values[item.key] = item.value.join(', ');
      } else {
        values[item.key] = item.value ?? '';
      }
    });
    reset(values);
  }, [items, reset]);

  const onSubmit = handleSubmit(async (data) => {
    setSaving(true);
    try {
      const processed: Record<string, any> = {};
      items.forEach(item => {
        let val = data[item.key];
        if (item.type === 'number') {
          val = Number(val) || 0;
        } else if (item.type === 'boolean') {
          val = !!val;
        } else if (item.type === 'number_array') {
          val = String(val)
            .split(/[,\s]+/)
            .map((n: string) => parseInt(n.trim()))
            .filter((n: number) => !isNaN(n) && n > 0);
        } else if (item.type === 'password' && val === '') {
          return;
        }
        processed[item.key] = val;
      });

      const success = await updateConfig(processed);
      if (success) {
        toast.success(t('webui.config.saved'));
      } else {
        toast.error(t('webui.config.save_failed'));
      }
    } catch {
      toast.error(t('webui.config.save_failed'));
    } finally {
      setSaving(false);
    }
  });

  const onReset = () => {
    const values: Record<string, any> = {};
    items.forEach(item => {
      if (item.type === 'number_array' && Array.isArray(item.value)) {
        values[item.key] = item.value.join(', ');
      } else {
        values[item.key] = item.value ?? '';
      }
    });
    reset(values);
  };

  if (loading) {
    return (
      <div className='flex items-center justify-center h-[400px]'>
        <div className='text-default-500'>{t('webui.config.loading')}</div>
      </div>
    );
  }

  const itemsByGroup: Record<string, ConfigItem[]> = {};
  items.forEach(item => {
    if (!itemsByGroup[item.group]) itemsByGroup[item.group] = [];
    itemsByGroup[item.group].push(item);
  });

  const firstGroupKey = groups[0]?.key ?? '';

  return (
    <section className='w-full max-w-[1200px] mx-auto py-4 md:py-8 px-2 md:px-6 relative'>
      <title>{t('webui.config.page_title')} - MorningCat WebUI</title>
      <Tabs
        aria-label='config tab'
        fullWidth={false}
        className='w-full'
        selectedKey={tab || firstGroupKey}
        onSelectionChange={(key) => navigate(`/config?tab=${key}`)}
        classNames={{
          base: 'w-full flex-col items-center',
          tabList: 'bg-white/40 dark:bg-black/20 backdrop-blur-md rounded-2xl p-1.5 shadow-sm border border-white/20 dark:border-white/5 mb-4 md:mb-8 w-full md:w-fit mx-auto overflow-x-auto hide-scrollbar',
          cursor: 'bg-white/80 dark:bg-white/10 backdrop-blur-md shadow-sm rounded-xl',
          tab: 'h-9 px-4 md:px-6',
          tabContent: 'text-default-600 dark:text-default-300 font-medium group-data-[selected=true]:text-primary',
          panel: 'w-full relative p-0',
        }}
      >
        {groups.map(group => (
          <Tab title={t(group.label)} key={group.key}>
            {group.key === 'backup' ? (
              <Card className='w-full max-w-3xl mx-auto backdrop-blur-sm border border-white/40 dark:border-white/10 shadow-sm rounded-2xl bg-white/60 dark:bg-black/40'>
                <CardBody className='py-6 px-4 md:py-8 md:px-12'>
                  <BackupConfigCard />
                </CardBody>
              </Card>
            ) : (
              <Card className='w-full max-w-3xl mx-auto backdrop-blur-sm border border-white/40 dark:border-white/10 shadow-sm rounded-2xl bg-white/60 dark:bg-black/40'>
                <CardBody className='py-6 px-4 md:py-8 md:px-12'>
                  <form onSubmit={onSubmit} className='flex flex-col gap-5'>
                    {(itemsByGroup[group.key] || []).map(item => (
                      <SchemaFieldRenderer key={item.key} item={item} control={control} />
                    ))}
                    <Divider className='my-4' />
                    <SaveButtons
                      onSubmit={onSubmit}
                      reset={onReset}
                      isSubmitting={isSubmitting || saving}
                      refresh={reload}
                    />
                  </form>
                </CardBody>
              </Card>
            )}
          </Tab>
        ))}
      </Tabs>
    </section>
  );
}

function SchemaFieldRenderer ({ item, control }: { item: ConfigItem; control: any }) {
  const { t } = useI18n();
  const label = t(item.label);
  const description = item.description ? t(item.description) : undefined;

  if (item.type === 'boolean') {
    return (
      <Controller
        control={control}
        name={item.key}
        render={({ field }) => (
          <div className='flex items-center justify-between'>
            <div>
              <p className='font-medium'>{label}</p>
              {description && <p className='text-sm text-default-500'>{description}</p>}
            </div>
            <Switch {...field} isSelected={!!field.value} onValueChange={field.onChange} />
          </div>
        )}
      />
    );
  }

  if (item.type === 'number') {
    return (
      <Controller
        control={control}
        name={item.key}
        rules={{
          required: item.required ? t('webui.config.field_required', label) : false,
          min: item.min != null ? { value: item.min, message: t('webui.config.min_value', item.min) } : undefined,
          max: item.max != null ? { value: item.max, message: t('webui.config.max_value', item.max) } : undefined,
        }}
        render={({ field, fieldState }) => (
          <Input
            {...field}
            label={label}
            type='number'
            placeholder={item.placeholder}
            description={description}
            value={field.value?.toString() ?? ''}
            onChange={(e) => field.onChange(parseInt(e.target.value) || 0)}
            isInvalid={!!fieldState.error}
            errorMessage={fieldState.error?.message}
          />
        )}
      />
    );
  }

  if (item.type === 'password') {
    return (
      <Controller
        control={control}
        name={item.key}
        render={({ field }) => (
          <Input
            {...field}
            label={label}
            type='password'
            placeholder={item.placeholder}
            description={description}
          />
        )}
      />
    );
  }

  if (item.type === 'number_array') {
    return (
      <Controller
        control={control}
        name={item.key}
        render={({ field }) => (
          <Input
            {...field}
            label={label}
            placeholder={item.placeholder || t('webui.config.multi_placeholder')}
            description={description}
          />
        )}
      />
    );
  }

  // string
  return (
    <Controller
      control={control}
      name={item.key}
      rules={{ required: item.required ? t('webui.config.field_required', label) : false }}
      render={({ field, fieldState }) => (
        <Input
          {...field}
          label={label}
          placeholder={item.placeholder}
          description={description}
          isInvalid={!!fieldState.error}
          errorMessage={fieldState.error?.message}
        />
      )}
    />
  );
}
