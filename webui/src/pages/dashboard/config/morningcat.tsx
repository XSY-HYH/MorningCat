import { Card, CardBody } from '@heroui/card';
import { Divider } from '@heroui/divider';
import { Input } from '@heroui/input';
import { Switch } from '@heroui/switch';
import { Select, SelectItem } from '@heroui/select';
import { Tab, Tabs } from '@heroui/tabs';
import { useLocalStorage } from '@uidotdev/usehooks';
import toast from 'react-hot-toast';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useEffect, useState, useCallback } from 'react';

import key from '@/const/key';
import SaveButtons from '@/components/button/save_buttons';
import ImageInput from '@/components/input/image_input';
import useI18n from '@/hooks/use-i18n';

interface ConfigGroup {
  key: string;
  label: string;
  icon: string;
}

interface ConfigItem {
  key: string;
  label: string;
  type: string;
  group: string;
  description: string;
  placeholder?: string;
  required: boolean;
  min?: number;
  max?: number;
  value: any;
  options?: string[];
}

interface ConfigResponse {
  groups: ConfigGroup[];
  items: ConfigItem[];
}

const ChangePasswordCard = () => {
  const { t } = useI18n();
  const [oldPassword, setOldPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const navigate = useNavigate();
  const [, setToken] = useLocalStorage(key.token, '');

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSubmitting(true);
    try {
      const response = await fetch('/api/auth/update_password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ oldPassword, newPassword }),
      });
      const result = await response.json();
      if (result.code === 0) {
        toast.success(t('config.password_changed'));
        setToken('');
        localStorage.removeItem(key.token);
        navigate('/web_login');
      } else {
        toast.error(result.message || t('config.password_change_failed'));
      }
    } catch {
      toast.error(t('config.password_change_failed'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <form onSubmit={onSubmit}>
      <div className='flex flex-col gap-4'>
        <div className='flex items-center gap-2'>
          <h3 className='text-lg font-semibold'>{t('config.label.change_password')}</h3>
        </div>
        <Input label={t('config.label.old_password')} type='password' value={oldPassword} onValueChange={setOldPassword} isRequired />
        <Input label={t('config.label.new_password')} type='password' value={newPassword} onValueChange={setNewPassword} isRequired />
      </div>
      <Divider className='my-4' />
      <SaveButtons onSubmit={onSubmit} reset={() => { setOldPassword(''); setNewPassword(''); }} isSubmitting={isSubmitting} />
    </form>
  );
};

const ThemeConfigCard = () => {
  const { t } = useI18n();
  const [background, setBackground] = useLocalStorage(key.backgroundImage, '');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const onSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    toast.success(t('config.saved'));
  };

  return (
    <form onSubmit={onSubmit}>
      <div className='flex flex-col gap-4'>
        <div className='flex items-center gap-2'>
          <h3 className='text-lg font-semibold'>{t('config.label.theme')}</h3>
        </div>
        <ImageInput label={t('config.label.background_image')} value={background} onChange={setBackground} />
      </div>
      <Divider className='my-4' />
      <SaveButtons onSubmit={onSubmit} reset={() => setBackground('')} isSubmitting={isSubmitting} />
    </form>
  );
};

function DynamicConfigCard ({ items, onSubmit, title }: { items: ConfigItem[]; onSubmit: (data: Record<string, any>) => Promise<void>; title: string }) {
  const { t } = useI18n();
  const [formValues, setFormValues] = useState<Record<string, any>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    const values: Record<string, any> = {};
    items.forEach(item => {
      values[item.key] = item.value;
    });
    setFormValues(values);
  }, [items]);

  const handleChange = (itemKey: string, value: any) => {
    setFormValues(prev => ({ ...prev, [itemKey]: value }));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSubmitting(true);
    try {
      const data: Record<string, any> = {};
      items.forEach(item => {
        if (item.type === 'number_array') {
          const val = String(formValues[item.key] || '');
          data[item.key] = val.split(/[,\s]+/).map((n: string) => parseInt(n.trim())).filter((n: number) => !isNaN(n) && n > 0);
        } else {
          data[item.key] = formValues[item.key];
        }
      });
      await onSubmit(data);
    } finally {
      setIsSubmitting(false);
    }
  };

  const reset = () => {
    const values: Record<string, any> = {};
    items.forEach(item => {
      values[item.key] = item.value;
    });
    setFormValues(values);
  };

  const renderField = (item: ConfigItem) => {
    const value = formValues[item.key];

    switch (item.type) {
      case 'boolean':
        return (
          <div className='flex items-center justify-between' key={item.key}>
            <div>
              <p className='font-medium'>{t(item.label)}</p>
              {item.description && <p className='text-sm text-default-500'>{t(item.description)}</p>}
            </div>
            <Switch isSelected={!!value} onValueChange={(v) => handleChange(item.key, v)} />
          </div>
        );

      case 'select':
        return (
          <Select
            key={item.key}
            label={t(item.label)}
            description={item.description ? t(item.description) : undefined}
            selectedKeys={value ? [String(value)] : []}
            onSelectionChange={(keys) => {
              const selected = Array.from(keys)[0];
              handleChange(item.key, selected);
            }}
          >
            {(item.options || []).map((opt) => (
              <SelectItem key={opt}>{opt}</SelectItem>
            ))}
          </Select>
        );

      case 'password':
        return (
          <Input
            key={item.key}
            label={t(item.label)}
            type='password'
            placeholder={item.placeholder || ''}
            description={item.description ? t(item.description) : undefined}
            value={value || ''}
            onValueChange={(v) => handleChange(item.key, v)}
            isRequired={item.required}
          />
        );

      case 'number':
        return (
          <Input
            key={item.key}
            label={t(item.label)}
            type='number'
            placeholder={item.placeholder || ''}
            description={item.description ? t(item.description) : undefined}
            value={value !== undefined && value !== null ? String(value) : ''}
            onValueChange={(v) => handleChange(item.key, Number(v))}
            isRequired={item.required}
            min={item.min}
            max={item.max}
          />
        );

      case 'number_array':
        return (
          <Input
            key={item.key}
            label={t(item.label)}
            placeholder={item.placeholder || ''}
            description={item.description ? t(item.description) : undefined}
            value={Array.isArray(value) ? value.join(', ') : (value || '')}
            onValueChange={(v) => handleChange(item.key, v)}
          />
        );

      case 'string':
      default:
        return (
          <Input
            key={item.key}
            label={t(item.label)}
            placeholder={item.placeholder || ''}
            description={item.description ? t(item.description) : undefined}
            value={value || ''}
            onValueChange={(v) => handleChange(item.key, v)}
            isRequired={item.required}
          />
        );
    }
  };

  return (
    <form onSubmit={handleSubmit}>
      <div className='flex flex-col gap-4'>
        <div className='flex items-center gap-2'>
          <h3 className='text-lg font-semibold'>{title}</h3>
        </div>
        <div className='flex flex-col gap-4'>
          {items.map(item => renderField(item))}
        </div>
      </div>
      <Divider className='my-4' />
      <SaveButtons onSubmit={handleSubmit} reset={reset} isSubmitting={isSubmitting} />
    </form>
  );
}

export default function MorningCatConfigPage () {
  const { t } = useI18n();
  const navigate = useNavigate();
  const search = useSearchParams({ tab: 'onebot' })[0];
  const tab = search.get('tab') ?? 'onebot';
  const [configData, setConfigData] = useState<ConfigResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadConfig();
  }, []);

  const loadConfig = async () => {
    try {
      const response = await fetch('/api/config');
      const result = await response.json();
      if (result.code === 0 && result.data) {
        setConfigData(result.data);
      }
    } catch (error) {
      console.error('Failed to load config:', error);
    } finally {
      setLoading(false);
    }
  };

  const updateConfig = async (data: Record<string, any>) => {
    try {
      const response = await fetch('/api/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      const result = await response.json();
      if (result.code === 0) {
        toast.success(t('config.saved'));
        await loadConfig();
      } else {
        toast.error(result.message || t('config.save_failed'));
      }
    } catch {
      toast.error(t('config.save_failed'));
    }
  };

  const ConfigPageItem = ({ children, size = 'md' }: { children: React.ReactNode; size?: 'sm' | 'md' | 'lg' }) => {
    const [backgroundImage] = useLocalStorage<string>(key.backgroundImage, '');
    const hasBackground = !!backgroundImage;

    return (
      <Card className={`w-full mx-auto backdrop-blur-sm border border-white/40 dark:border-white/10 shadow-sm rounded-2xl transition-all ${hasBackground ? 'bg-white/20 dark:bg-black/10' : 'bg-white/60 dark:bg-black/40'} ${size === 'sm' ? 'max-w-xl' : size === 'md' ? 'max-w-3xl' : 'max-w-6xl'}`}>
        <CardBody className='py-6 px-4 md:py-8 md:px-12'>
          <div className='w-full flex flex-col gap-5'>
            {children}
          </div>
        </CardBody>
      </Card>
    );
  };

  const getItemsForGroup = (groupKey: string): ConfigItem[] => {
    if (!configData) return [];
    return configData.items.filter(item => item.group === groupKey);
  };

  const renderGroupTab = (group: ConfigGroup) => {
    const items = getItemsForGroup(group.key);
    if (items.length === 0) return null;

    return (
      <Tab title={t(group.label)} key={group.key}>
        <ConfigPageItem>
          <DynamicConfigCard items={items} onSubmit={updateConfig} title={t(group.label)} />
        </ConfigPageItem>
      </Tab>
    );
  };

  return (
    <section className='w-full max-w-[1200px] mx-auto py-4 md:py-8 px-2 md:px-6 relative'>
      <title>{t('config.page_title')} - MorningCat WebUI</title>
      <Tabs
        aria-label='config tab'
        fullWidth={false}
        className='w-full'
        selectedKey={tab}
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
        {configData?.groups.map(group => renderGroupTab(group))}
        <Tab title={t('config.label.change_password')} key='password'>
          <ConfigPageItem size='sm'>
            <ChangePasswordCard />
          </ConfigPageItem>
        </Tab>
        <Tab title={t('config.label.theme')} key='theme'>
          <ConfigPageItem size='lg'>
            <ThemeConfigCard />
          </ConfigPageItem>
        </Tab>
      </Tabs>
    </section>
  );
}
