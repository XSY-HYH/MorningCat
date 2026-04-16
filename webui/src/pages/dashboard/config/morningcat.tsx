import { Button } from '@heroui/button';
import { Card, CardBody, CardHeader } from '@heroui/card';
import { Chip } from '@heroui/chip';
import { Divider } from '@heroui/divider';
import { Input } from '@heroui/input';
import { Switch } from '@heroui/switch';
import { Tab, Tabs } from '@heroui/tabs';
import { useLocalStorage } from '@uidotdev/usehooks';
import { Controller, useForm } from 'react-hook-form';
import toast from 'react-hot-toast';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useEffect, useState } from 'react';

import key from '@/const/key';
import SaveButtons from '@/components/button/save_buttons';
import ImageInput from '@/components/input/image_input';

interface MorningCatConfig {
  napCatServerUrl: string;
  napCatToken: string;
  reconnectDelay: number;
  modulesDirectory: string;
  autoLoadModules: boolean;
  ownerQQ: number;
  adminQQs: number[];
  webui: {
    enabled: boolean;
    port: number;
    username: string;
    password: string;
  };
}

const defaultConfig: MorningCatConfig = {
  napCatServerUrl: 'ws://127.0.0.1:7892',
  napCatToken: '',
  reconnectDelay: 5,
  modulesDirectory: 'Modules',
  autoLoadModules: true,
  ownerQQ: 0,
  adminQQs: [],
  webui: {
    enabled: true,
    port: 8080,
    username: 'admin',
    password: 'admin123',
  },
};

function ConfigSection ({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className='flex flex-col gap-4'>
      <div className='flex items-center gap-2'>
        <h3 className='text-lg font-semibold'>{title}</h3>
      </div>
      <div className='flex flex-col gap-4'>
        {children}
      </div>
    </div>
  );
}

const NapCatConfigCard = ({ config, onSubmit }: { config: MorningCatConfig; onSubmit: (data: Partial<MorningCatConfig>) => Promise<void> }) => {
  const { control, handleSubmit, formState: { isSubmitting }, reset } = useForm({
    defaultValues: {
      napCatServerUrl: config.napCatServerUrl,
      napCatToken: config.napCatToken,
      reconnectDelay: config.reconnectDelay,
    },
  });

  return (
    <form onSubmit={handleSubmit(async (data) => {
      await onSubmit(data);
    })}>
      <ConfigSection title='NapCat 连接配置'>
        <Controller
          control={control}
          name='napCatServerUrl'
          rules={{ required: '服务器地址不能为空' }}
          render={({ field, fieldState }) => (
            <Input
              {...field}
              label='NapCat 服务器地址'
              placeholder='ws://127.0.0.1:7892'
              isInvalid={!!fieldState.error}
              errorMessage={fieldState.error?.message}
            />
          )}
        />
        <Controller
          control={control}
          name='napCatToken'
          render={({ field }) => (
            <Input
              {...field}
              label='NapCat Token'
              placeholder='留空则不验证'
              type='password'
            />
          )}
        />
        <Controller
          control={control}
          name='reconnectDelay'
          rules={{ 
            required: '重连延迟不能为空', 
            min: { value: 1, message: '重连延迟至少为1秒' },
            max: { value: 300, message: '重连延迟不能超过300秒' }
          }}
          render={({ field, fieldState }) => (
            <Input
              {...field}
              label='重连延迟 (秒)'
              type='number'
              description='WebSocket 断开后每隔多少秒尝试重连一次'
              isInvalid={!!fieldState.error}
              errorMessage={fieldState.error?.message}
            />
          )}
        />
      </ConfigSection>
      <Divider className='my-4' />
      <SaveButtons onSubmit={handleSubmit(async (data) => onSubmit(data))} reset={reset} isSubmitting={isSubmitting} />
    </form>
  );
};

const PermissionConfigCard = ({ config, onSubmit }: { config: MorningCatConfig; onSubmit: (data: Partial<MorningCatConfig>) => Promise<void> }) => {
  const { control, handleSubmit, formState: { isSubmitting }, reset, setValue } = useForm({
    defaultValues: {
      ownerQQ: config.ownerQQ || '',
      adminQQs: config.adminQQs.join(', '),
    },
  });

  return (
    <form onSubmit={handleSubmit(async (data) => {
      const adminQQs = (data.adminQQs as string)
        .split(/[,\s]+/)
        .map(qq => parseInt(qq.trim()))
        .filter(qq => !isNaN(qq) && qq > 0);
      await onSubmit({ ownerQQ: Number(data.ownerQQ) || 0, adminQQs });
    })}>
      <ConfigSection title='权限配置'>
        <Controller
          control={control}
          name='ownerQQ'
          render={({ field }) => (
            <Input
              {...field}
              label='持有者 QQ'
              placeholder='设置为 0 表示未设置'
              type='number'
              description='持有者拥有最高权限'
            />
          )}
        />
        <Controller
          control={control}
          name='adminQQs'
          render={({ field }) => (
            <Input
              {...field}
              label='管理员 QQ 列表'
              placeholder='多个管理员用逗号或空格分隔'
              description='管理员拥有部分特权命令权限'
            />
          )}
        />
      </ConfigSection>
      <Divider className='my-4' />
      <SaveButtons onSubmit={handleSubmit(async (data) => {
        const adminQQs = (data.adminQQs as string)
          .split(/[,\s]+/)
          .map(qq => parseInt(qq.trim()))
          .filter(qq => !isNaN(qq) && qq > 0);
        await onSubmit({ ownerQQ: Number(data.ownerQQ) || 0, adminQQs });
      })} reset={reset} isSubmitting={isSubmitting} />
    </form>
  );
};

const WebUIConfigCard = ({ config, onSubmit }: { config: MorningCatConfig; onSubmit: (data: Partial<MorningCatConfig>) => Promise<void> }) => {
  const { control, handleSubmit, formState: { isSubmitting }, reset } = useForm({
    defaultValues: {
      enabled: config.webui.enabled,
      port: config.webui.port,
      username: config.webui.username,
      password: config.webui.password,
    },
  });

  return (
    <form onSubmit={handleSubmit(async (data) => {
      await onSubmit({ webui: data });
    })}>
      <ConfigSection title='WebUI 配置'>
        <Controller
          control={control}
          name='enabled'
          render={({ field }) => (
            <div className='flex items-center justify-between'>
              <div>
                <p className='font-medium'>启用 WebUI</p>
                <p className='text-sm text-default-500'>关闭后 WebUI 将不会启动</p>
              </div>
              <Switch {...field} isSelected={field.value} onValueChange={field.onChange} />
            </div>
          )}
        />
        <Controller
          control={control}
          name='port'
          rules={{ required: '端口不能为空', min: { value: 1, message: '端口必须大于0' }, max: { value: 65535, message: '端口必须小于65536' } }}
          render={({ field, fieldState }) => (
            <Input
              {...field}
              label='WebUI 端口'
              type='number'
              isInvalid={!!fieldState.error}
              errorMessage={fieldState.error?.message}
            />
          )}
        />
        <Controller
          control={control}
          name='username'
          rules={{ required: '用户名不能为空' }}
          render={({ field, fieldState }) => (
            <Input
              {...field}
              label='登录用户名'
              isInvalid={!!fieldState.error}
              errorMessage={fieldState.error?.message}
            />
          )}
        />
        <Controller
          control={control}
          name='password'
          rules={{ required: '密码不能为空' }}
          render={({ field, fieldState }) => (
            <Input
              {...field}
              label='登录密码'
              type='password'
              isInvalid={!!fieldState.error}
              errorMessage={fieldState.error?.message}
            />
          )}
        />
      </ConfigSection>
      <Divider className='my-4' />
      <SaveButtons onSubmit={handleSubmit(async (data) => onSubmit({ webui: data }))} reset={reset} isSubmitting={isSubmitting} />
    </form>
  );
};

const ChangePasswordCard = () => {
  const { control, handleSubmit, formState: { isSubmitting }, reset, watch } = useForm<{
    oldPassword: string;
    newPassword: string;
  }>({
    defaultValues: {
      oldPassword: '',
      newPassword: '',
    },
  });

  const navigate = useNavigate();
  const [, setToken] = useLocalStorage(key.token, '');
  const oldPasswordValue = watch('oldPassword');

  const onSubmit = handleSubmit(async (data) => {
    try {
      const response = await fetch('/api/auth/update_password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ oldPassword: data.oldPassword, newPassword: data.newPassword }),
      });
      const result = await response.json();
      if (result.code === 0) {
        toast.success('修改成功，请重新登录');
        setToken('');
        localStorage.removeItem(key.token);
        navigate('/web_login');
      } else {
        toast.error(result.message || '修改失败');
      }
    } catch (error) {
      toast.error('修改失败');
    }
  });

  return (
    <form onSubmit={onSubmit}>
      <ConfigSection title='修改密码'>
        <Controller
          control={control}
          name='oldPassword'
          rules={{ required: '旧密码不能为空' }}
          render={({ field, fieldState }) => (
            <Input
              {...field}
              label='旧密码'
              type='password'
              isInvalid={!!fieldState.error}
              errorMessage={fieldState.error?.message}
            />
          )}
        />
        <Controller
          control={control}
          name='newPassword'
          rules={{
            required: '新密码不能为空',
            minLength: { value: 6, message: '新密码至少需要6个字符' },
            validate: (value) => {
              if (value === oldPasswordValue) return '新密码不能与旧密码相同';
              if (!/[a-zA-Z]/.test(value)) return '新密码必须包含字母';
              if (!/[0-9]/.test(value)) return '新密码必须包含数字';
              return true;
            },
          }}
          render={({ field, fieldState }) => (
            <Input
              {...field}
              label='新密码'
              type='password'
              placeholder='至少6位，包含字母和数字'
              isInvalid={!!fieldState.error}
              errorMessage={fieldState.error?.message}
            />
          )}
        />
      </ConfigSection>
      <Divider className='my-4' />
      <SaveButtons onSubmit={onSubmit} reset={reset} isSubmitting={isSubmitting} />
    </form>
  );
};

const ThemeConfigCard = () => {
  const { control, handleSubmit, formState: { isSubmitting }, reset, setValue } = useForm({
    defaultValues: {
      background: '',
    },
  });

  const [b64img, setB64img] = useLocalStorage(key.backgroundImage, '');

  useEffect(() => {
    setValue('background', b64img || '');
  }, [b64img, setValue]);

  const onSubmit = handleSubmit((data) => {
    setB64img(data.background);
    toast.success('保存成功');
  });

  return (
    <form onSubmit={onSubmit}>
      <ConfigSection title='主题配置'>
        <Controller
          control={control}
          name='background'
          render={({ field }) => (
            <ImageInput
              label='背景图片'
              value={field.value}
              onChange={field.onChange}
            />
          )}
        />
      </ConfigSection>
      <Divider className='my-4' />
      <SaveButtons onSubmit={onSubmit} reset={reset} isSubmitting={isSubmitting} />
    </form>
  );
};

export default function MorningCatConfigPage () {
  const navigate = useNavigate();
  const search = useSearchParams({ tab: 'napcat' })[0];
  const tab = search.get('tab') ?? 'napcat';
  const [config, setConfig] = useState<MorningCatConfig>(defaultConfig);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadConfig();
  }, []);

  const loadConfig = async () => {
    try {
      const response = await fetch('/api/config');
      const result = await response.json();
      if (result.code === 0 && result.data) {
        setConfig({ ...defaultConfig, ...result.data });
      }
    } catch (error) {
      console.error('加载配置失败:', error);
    } finally {
      setLoading(false);
    }
  };

  const updateConfig = async (data: Partial<MorningCatConfig>) => {
    try {
      const response = await fetch('/api/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      const result = await response.json();
      if (result.code === 0) {
        toast.success('保存成功');
        await loadConfig();
      } else {
        toast.error(result.message || '保存失败');
      }
    } catch (error) {
      toast.error('保存失败');
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

  return (
    <section className='w-full max-w-[1200px] mx-auto py-4 md:py-8 px-2 md:px-6 relative'>
      <title>系统配置 - MorningCat WebUI</title>
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
        <Tab title='NapCat 连接' key='napcat'>
          <ConfigPageItem>
            <NapCatConfigCard config={config} onSubmit={updateConfig} />
          </ConfigPageItem>
        </Tab>
        <Tab title='权限配置' key='permission'>
          <ConfigPageItem>
            <PermissionConfigCard config={config} onSubmit={updateConfig} />
          </ConfigPageItem>
        </Tab>
        <Tab title='WebUI 配置' key='webui'>
          <ConfigPageItem>
            <WebUIConfigCard config={config} onSubmit={updateConfig} />
          </ConfigPageItem>
        </Tab>
        <Tab title='修改密码' key='password'>
          <ConfigPageItem size='sm'>
            <ChangePasswordCard />
          </ConfigPageItem>
        </Tab>
        <Tab title='主题配置' key='theme'>
          <ConfigPageItem size='lg'>
            <ThemeConfigCard />
          </ConfigPageItem>
        </Tab>
      </Tabs>
    </section>
  );
}
