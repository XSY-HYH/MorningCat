import { Input } from '@heroui/input';
import { useLocalStorage } from '@uidotdev/usehooks';
import { Controller, useForm } from 'react-hook-form';
import toast from 'react-hot-toast';
import { useNavigate } from 'react-router-dom';

import key from '@/const/key';

import SaveButtons from '@/components/button/save_buttons';

import WebUIManager from '@/controllers/webui_manager';
import useI18n from '@/hooks/use-i18n';

const ChangePasswordCard = () => {
  const { t } = useI18n();
  const {
    control,
    handleSubmit: handleWebuiSubmit,
    formState: { isSubmitting, errors },
    reset,
    watch,
  } = useForm<{
    oldToken: string;
    newToken: string;
  }>({
    defaultValues: {
      oldToken: '',
      newToken: '',
    },
  });

  const navigate = useNavigate();
  const [, setToken] = useLocalStorage(key.token, '');

  // 监听旧密码的值
  const oldTokenValue = watch('oldToken');

  const onSubmit = handleWebuiSubmit(async (data) => {
    try {
      // 使用正常密码更新流程
      await WebUIManager.changePassword(data.oldToken, data.newToken);

      toast.success(t('webui.changepwd.success'));
      setToken('');
      localStorage.removeItem(key.token);
      navigate('/web_login');
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.changepwd.failed', msg));
    }
  });

  return (
    <>
      <title>{t('webui.config.change_password.title')}</title>

      <Controller
        control={control}
        name='oldToken'
        rules={{
          required: t('webui.changepwd.old_required'),
          validate: (value) => {
            if (!value || value.trim().length === 0) {
              return t('webui.changepwd.old_required');
            }
            return true;
          },
        }}
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.changepwd.old_password')}
            placeholder={t('webui.changepwd.old_placeholder')}
            type='password'
            isRequired
            isInvalid={!!errors.oldToken}
            errorMessage={errors.oldToken?.message}
          />
        )}
      />

      <Controller
        control={control}
        name='newToken'
        rules={{
          required: t('webui.changepwd.new_required'),
          minLength: {
            value: 6,
            message: t('webui.changepwd.min_length'),
          },
          validate: (value) => {
            if (!value || value.trim().length === 0) {
              return t('webui.changepwd.new_required');
            }
            if (value.trim().length !== value.length) {
              return t('webui.changepwd.no_spaces');
            }
            if (value === oldTokenValue) {
              return t('webui.changepwd.same_as_old');
            }
            // 检查是否包含字母
            if (!/[a-zA-Z]/.test(value)) {
              return t('webui.changepwd.must_have_letter');
            }
            // 检查是否包含数字
            if (!/[0-9]/.test(value)) {
              return t('webui.changepwd.must_have_number');
            }
            return true;
          },
        }}
        render={({ field }) => (
          <Input
            {...field}
            label={t('webui.changepwd.new_password')}
            placeholder={t('webui.changepwd.new_placeholder')}
            type='password'
            isRequired
            isInvalid={!!errors.newToken}
            errorMessage={errors.newToken?.message}
          />
        )}
      />

      <SaveButtons
        onSubmit={onSubmit}
        reset={reset}
        isSubmitting={isSubmitting}
      />
    </>
  );
};

export default ChangePasswordCard;
