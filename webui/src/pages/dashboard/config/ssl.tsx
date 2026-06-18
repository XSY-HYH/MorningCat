import { useRequest } from 'ahooks';
import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { Button } from '@heroui/button';
import { Textarea } from '@heroui/input';

import PageLoading from '@/components/page_loading';

import WebUIManager from '@/controllers/webui_manager';
import useI18n from '@/hooks/use-i18n';

const SSLConfigCard = () => {
  const { t } = useI18n();
  const {
    data: sslData,
    loading: sslLoading,
    refreshAsync: refreshSSL,
  } = useRequest(WebUIManager.getSSLStatus);

  const [sslCert, setSslCert] = useState('');
  const [sslKey, setSslKey] = useState('');
  const [sslSaving, setSslSaving] = useState(false);

  useEffect(() => {
    if (sslData) {
      setSslCert(sslData.certContent || '');
      setSslKey(sslData.keyContent || '');
    }
  }, [sslData]);

  const handleSaveSSL = async () => {
    if (!sslCert.trim() || !sslKey.trim()) {
      toast.error(t('webui.ssl.cert_key_required'));
      return;
    }
    setSslSaving(true);
    try {
      const result = await WebUIManager.saveSSLCert(sslCert, sslKey);
      toast.success(result.message || t('webui.ssl.save_success'));
      await refreshSSL();
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.ssl.save_failed', msg));
    } finally {
      setSslSaving(false);
    }
  };

  const handleDeleteSSL = async () => {
    setSslSaving(true);
    try {
      const result = await WebUIManager.deleteSSLCert();
      toast.success(result.message || t('webui.ssl.delete_success'));
      setSslCert('');
      setSslKey('');
      await refreshSSL();
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.ssl.delete_failed', msg));
    } finally {
      setSslSaving(false);
    }
  };

  const handleRefresh = async () => {
    try {
      await refreshSSL();
      toast.success(t('webui.ssl.refresh_success'));
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.ssl.refresh_failed', msg));
    }
  };

  if (sslLoading) return <PageLoading loading />;

  return (
    <>
      <title>{t('webui.config.ssl.title')}</title>
      <div className='flex flex-col gap-4'>
        <div className='flex flex-col gap-3'>
          <div className='flex items-center gap-2'>
            <div className='flex-shrink-0 w-full font-bold text-default-600 dark:text-default-400 px-1'>{t('webui.ssl.title')}</div>
            {sslData?.enabled && (
              <span className='px-2 py-0.5 text-xs bg-success-100 text-success-700 dark:bg-success-900/30 dark:text-success-400 rounded-full whitespace-nowrap'>
                {t('webui.ssl.enabled')}
              </span>
            )}
          </div>
          <p className='text-sm text-default-500 px-1'>
            {t('webui.ssl.desc')}
          </p>
          <div className='p-3 bg-warning-50 dark:bg-warning-900/20 rounded-lg border border-warning-200 dark:border-warning-800'>
            <p className='text-sm text-warning-700 dark:text-warning-400'>
              <strong>{t('webui.ssl.note')}</strong>
            </p>
          </div>
        </div>

        <div className='flex flex-col gap-4'>
          <Textarea
            label={t('webui.ssl.cert_label')}
            placeholder={'-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----'}
            value={sslCert}
            onValueChange={setSslCert}
            minRows={6}
            maxRows={12}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400 font-mono text-sm',
            }}
          />
          <Textarea
            label={t('webui.ssl.key_label')}
            placeholder={'-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----'}
            value={sslKey}
            onValueChange={setSslKey}
            minRows={6}
            maxRows={12}
            classNames={{
              inputWrapper:
                'bg-default-100/50 dark:bg-white/5 backdrop-blur-md border border-transparent hover:bg-default-200/50 dark:hover:bg-white/10 transition-all shadow-sm data-[hover=true]:border-default-300',
              input: 'bg-transparent text-default-700 placeholder:text-default-400 font-mono text-sm',
            }}
          />
        </div>

        <div className='flex gap-2 justify-end'>
          <Button
            variant='flat'
            isLoading={sslSaving || sslLoading}
            onPress={handleRefresh}
            size='sm'
          >
            {t('webui.ssl.refresh')}
          </Button>
          {sslData?.enabled && (
            <Button
              color='danger'
              variant='flat'
              isLoading={sslSaving || sslLoading}
              onPress={handleDeleteSSL}
              size='sm'
            >
              {t('webui.ssl.delete')}
            </Button>
          )}
          <Button
            color='primary'
            isLoading={sslSaving || sslLoading}
            onPress={handleSaveSSL}
            size='sm'
          >
            {t('webui.ssl.save')}
          </Button>
        </div>
      </div>
    </>
  );
};

export default SSLConfigCard;
