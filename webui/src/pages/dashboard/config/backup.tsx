import { Button } from '@heroui/button';
import toast from 'react-hot-toast';
import { LuDownload, LuUpload } from 'react-icons/lu';
import useI18n from '@/hooks/use-i18n';

const handleExportConfig = async (t: (key: string, ...args: any[]) => string) => {
  try {
    const response = await fetch('/api/backup/export', {
      method: 'GET',
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(t('webui.backup.export_failed'));
    }

    const blob = await response.blob();
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    const fileName = response.headers.get('Content-Disposition')?.split('=')[1]?.replace(/"/g, '') || 'mct_backup.zip';
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);

    toast.success(t('webui.backup.export_success'));
  } catch (error) {
    const msg = (error as Error).message;
    toast.error(t('webui.backup.export_failed', msg));
  }
};

const handleImportConfig = async (event: React.ChangeEvent<HTMLInputElement>, t: (key: string, ...args: any[]) => string) => {
  const file = event.target.files?.[0];
  if (!file) return;

  if (!file.name.endsWith('.zip')) {
    toast.error(t('webui.backup.zip_required'));
    return;
  }

  try {
    const formData = new FormData();
    formData.append('configFile', file);

    const response = await fetch('/api/backup/import', {
      method: 'POST',
      credentials: 'include',
      body: formData,
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message || t('webui.backup.import_failed'));
    }

    toast.success(t('webui.backup.import_success'));
  } catch (error) {
    const msg = (error as Error).message;
    toast.error(t('webui.backup.import_failed', msg));
  } finally {
    event.target.value = '';
  }
};

const BackupConfigCard: React.FC = () => {
  const { t } = useI18n();

  return (
    <div className='space-y-6'>
      <div>
        <h3 className='text-lg font-medium mb-4'>{t('webui.backup.heading')}</h3>
        <p className='text-sm text-default-500 mb-4'>
          {t('webui.backup.description')}
        </p>

        <div className='flex flex-wrap gap-3'>
          <Button
            isIconOnly
            className='bg-primary hover:bg-primary/90 text-white'
            radius='full'
            onPress={() => handleExportConfig(t)}
            title={t('webui.backup.export')}
          >
            <LuDownload size={20} />
          </Button>
          <label className='cursor-pointer'>
            <input
              type='file'
              accept='.zip'
              onChange={(e) => handleImportConfig(e, t)}
              className='hidden'
            />
            <Button
              isIconOnly
              className='bg-primary hover:bg-primary/90 text-white'
              radius='full'
              as='span'
              title={t('webui.backup.import')}
            >
              <LuUpload size={20} />
            </Button>
          </label>
        </div>

        <div className='mt-4 p-3 bg-warning/10 border border-warning/20 rounded-lg'>
          <div className='flex items-start gap-2'>
            <p className='text-sm text-warning'>
              {t('webui.backup.warning')}
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};

export default BackupConfigCard;
