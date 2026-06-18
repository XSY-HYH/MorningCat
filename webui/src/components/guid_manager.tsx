import { Button } from '@heroui/button';
import { Input } from '@heroui/input';
import { Divider } from '@heroui/divider';
import { Chip } from '@heroui/chip';
import { Listbox, ListboxItem } from '@heroui/listbox';
import { Spinner } from '@heroui/spinner';
import { useEffect, useState, useCallback, useMemo } from 'react';
import toast from 'react-hot-toast';
import { MdContentCopy, MdDelete, MdRefresh, MdSave, MdRestorePage, MdBackup } from 'react-icons/md';
import MD5 from 'crypto-js/md5';

import QQManager from '@/controllers/qq_manager';
import useDialog from '@/hooks/use-dialog';
import useI18n from '@/hooks/use-i18n';

interface GUIDManagerProps {
  showRestart?: boolean;
  compact?: boolean;
}

const GUIDManager: React.FC<GUIDManagerProps> = ({ showRestart = true, compact = false }) => {
  const dialog = useDialog();
  const { t } = useI18n();

  const [platform, setPlatform] = useState<string>('');
  const isWindows = platform === 'win32';
  const isMac = platform === 'darwin';
  const isLinux = platform !== '' && platform !== 'win32' && platform !== 'darwin';
  const platformDetected = platform !== '';

  const [currentGUID, setCurrentGUID] = useState<string>('');
  const [inputGUID, setInputGUID] = useState<string>('');
  const [backups, setBackups] = useState<string[]>([]);

  const [currentMAC, setCurrentMAC] = useState<string>('');
  const [inputMAC, setInputMAC] = useState<string>('');
  const [machineId, setMachineId] = useState<string>('');
  const [linuxBackups, setLinuxBackups] = useState<string[]>([]);

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [restarting, setRestarting] = useState(false);

  const isValidGUID = (guid: string) => /^[0-9a-fA-F]{32}$/.test(guid);
  const isValidMAC = (mac: string) => /^[0-9a-fA-F]{2}(-[0-9a-fA-F]{2}){5}$/.test(mac.trim().toLowerCase());

  const computedLinuxGUID = useMemo(() => {
    if (!isLinux) return '';
    const mac = inputMAC.trim().toLowerCase();
    if (!machineId && !mac) return '';
    return MD5(machineId + mac).toString();
  }, [isLinux, machineId, inputMAC]);

  const currentLinuxGUID = useMemo(() => {
    if (!isLinux || !currentMAC) return '';
    return MD5(machineId + currentMAC).toString();
  }, [isLinux, machineId, currentMAC]);

  const fetchPlatform = useCallback(async () => {
    try {
      const data = await QQManager.getPlatformInfo();
      setPlatform(data.platform);
    } catch {
      setPlatform('win32');
    }
  }, []);

  const fetchGUID = useCallback(async () => {
    setLoading(true);
    try {
      const data = await QQManager.getDeviceGUID();
      setCurrentGUID(data.guid);
      setInputGUID(data.guid);
    } catch (error) {
      const msg = (error as Error).message;
      setCurrentGUID('');
      setInputGUID('');
      if (!msg.includes('not found')) {
        toast.error(t('webui.guid.fetch_guid_failed', msg));
      }
    } finally {
      setLoading(false);
    }
  }, [t]);

  const fetchBackups = useCallback(async () => {
    try {
      const data = await QQManager.getGUIDBackups();
      setBackups(data);
    } catch {
      // ignore
    }
  }, []);

  const fetchLinuxInfo = useCallback(async () => {
    setLoading(true);
    try {
      const [macData, midData] = await Promise.all([
        QQManager.getLinuxMAC().catch(() => ({ mac: '' })),
        QQManager.getLinuxMachineId().catch(() => ({ machineId: '' })),
      ]);
      setCurrentMAC(macData.mac);
      setInputMAC(macData.mac);
      setMachineId(midData.machineId);
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.guid.fetch_device_failed', msg));
    } finally {
      setLoading(false);
    }
  }, [t]);

  const fetchLinuxBackups = useCallback(async () => {
    try {
      const data = await QQManager.getLinuxMachineInfoBackups();
      setLinuxBackups(data);
    } catch {
      // ignore
    }
  }, []);

  useEffect(() => {
    fetchPlatform();
  }, [fetchPlatform]);

  useEffect(() => {
    if (!platformDetected) return;
    if (isWindows) {
      fetchGUID();
      fetchBackups();
    } else {
      fetchLinuxInfo();
      fetchLinuxBackups();
    }
  }, [platformDetected, isWindows, fetchGUID, fetchBackups, fetchLinuxInfo, fetchLinuxBackups]);

  const handleCopy = () => {
    const guid = isLinux ? currentLinuxGUID : currentGUID;
    if (guid) {
      navigator.clipboard.writeText(guid);
      toast.success(t('webui.guid.copied'));
    }
  };

  const handleSave = async () => {
    if (!isValidGUID(inputGUID)) {
      toast.error(t('webui.guid.invalid_guid'));
      return;
    }
    setSaving(true);
    try {
      await QQManager.setDeviceGUID(inputGUID);
      setCurrentGUID(inputGUID);
      toast.success(t('webui.guid.set_success'));
      await fetchBackups();
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.guid.set_failed', msg));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = () => {
    dialog.confirm({
      title: t('webui.guid.confirm_delete'),
      content: t('webui.guid.delete_registry_desc'),
      confirmText: t('webui.guid.delete_btn'),
      cancelText: t('webui.guid.cancel'),
      onConfirm: async () => {
        try {
          await QQManager.resetDeviceID();
          setCurrentGUID('');
          setInputGUID('');
          toast.success(t('webui.guid.deleted'));
          await fetchBackups();
        } catch (error) {
          const msg = (error as Error).message;
          toast.error(t('webui.guid.delete_failed', msg));
        }
      },
    });
  };

  const handleBackup = async () => {
    try {
      await QQManager.createGUIDBackup();
      toast.success(t('webui.guid.backup_created'));
      await fetchBackups();
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.guid.backup_failed', msg));
    }
  };

  const handleRestore = (backupName: string) => {
    dialog.confirm({
      title: t('webui.guid.confirm_restore'),
      content: t('webui.guid.restore_desc', backupName),
      confirmText: t('webui.guid.restore_btn'),
      cancelText: t('webui.guid.cancel'),
      onConfirm: async () => {
        try {
          await QQManager.restoreGUIDBackup(backupName);
          toast.success(t('webui.guid.restored'));
          await fetchGUID();
          await fetchBackups();
        } catch (error) {
          const msg = (error as Error).message;
          toast.error(t('webui.guid.restore_failed', msg));
        }
      },
    });
  };

  const handleLinuxSaveMAC = async () => {
    const mac = inputMAC.trim().toLowerCase();
    if (!isValidMAC(mac)) {
      toast.error(t('webui.guid.invalid_mac'));
      return;
    }
    setSaving(true);
    try {
      await QQManager.setLinuxMAC(mac);
      setCurrentMAC(mac);
      toast.success(t('webui.guid.mac_set_success'));
      await fetchLinuxBackups();
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.guid.mac_set_failed', msg));
    } finally {
      setSaving(false);
    }
  };

  const handleLinuxCopyMAC = () => {
    if (currentMAC) {
      navigator.clipboard.writeText(currentMAC);
      toast.success(t('webui.guid.copied'));
    }
  };

  const handleLinuxDelete = () => {
    dialog.confirm({
      title: t('webui.guid.confirm_delete'),
      content: t('webui.guid.delete_machine_info_desc'),
      confirmText: t('webui.guid.delete_btn'),
      cancelText: t('webui.guid.cancel'),
      onConfirm: async () => {
        try {
          await QQManager.resetLinuxDeviceID();
          setCurrentMAC('');
          setInputMAC('');
          toast.success(t('webui.guid.deleted'));
          await fetchLinuxBackups();
        } catch (error) {
          const msg = (error as Error).message;
          toast.error(t('webui.guid.delete_failed', msg));
        }
      },
    });
  };

  const handleLinuxBackup = async () => {
    try {
      await QQManager.createLinuxMachineInfoBackup();
      toast.success(t('webui.guid.backup_created'));
      await fetchLinuxBackups();
    } catch (error) {
      const msg = (error as Error).message;
      toast.error(t('webui.guid.backup_failed', msg));
    }
  };

  const handleLinuxRestore = (backupName: string) => {
    dialog.confirm({
      title: t('webui.guid.confirm_restore'),
      content: t('webui.guid.restore_linux_desc', backupName),
      confirmText: t('webui.guid.restore_btn'),
      cancelText: t('webui.guid.cancel'),
      onConfirm: async () => {
        try {
          await QQManager.restoreLinuxMachineInfoBackup(backupName);
          toast.success(t('webui.guid.restored'));
          await fetchLinuxInfo();
          await fetchLinuxBackups();
        } catch (error) {
          const msg = (error as Error).message;
          toast.error(t('webui.guid.restore_failed', msg));
        }
      },
    });
  };

  const handleRestart = () => {
    dialog.confirm({
      title: t('webui.guid.confirm_restart'),
      content: t('webui.guid.restart_desc'),
      confirmText: t('webui.guid.restart_btn'),
      cancelText: t('webui.guid.cancel'),
      onConfirm: async () => {
        setRestarting(true);
        try {
          await QQManager.restartNapCat();
          toast.success(t('webui.guid.restart_sent'));
        } catch (error) {
          const msg = (error as Error).message;
          toast.error(t('webui.guid.restart_failed', msg));
        } finally {
          setRestarting(false);
        }
      },
    });
  };

  if (loading || !platformDetected) {
    return (
      <div className='flex items-center justify-center py-8'>
        <Spinner label={t('webui.guid.loading')} />
      </div>
    );
  }

  if (isMac) {
    return (
      <div className={`flex flex-col gap-${compact ? '3' : '4'}`}>
        <div className='flex flex-col items-center justify-center py-8 gap-2'>
          <Chip variant='flat' color='warning' className='text-xs'>
            {t('webui.guid.macos_unsupported')}
          </Chip>
          <div className='text-xs text-default-400'>
            {t('webui.guid.macos_unsupported_desc')}
          </div>
        </div>
      </div>
    );
  }

  if (isLinux) {
    return (
      <div className={`flex flex-col gap-${compact ? '3' : '4'}`}>
        <div className='flex flex-col gap-2'>
          <div className='text-sm font-medium text-default-700'>{t('webui.guid.current_guid')}</div>
          <div className='flex items-center gap-2'>
            {currentLinuxGUID
              ? (
                <Chip variant='flat' color='primary' className='font-mono text-xs max-w-full'>
                  {currentLinuxGUID}
                </Chip>
              )
              : (
                <Chip variant='flat' color='warning' className='text-xs'>
                  {t('webui.guid.not_set')}
                </Chip>
              )}
            {currentLinuxGUID && (
              <Button isIconOnly size='sm' variant='light' onPress={handleCopy} aria-label={t('webui.guid.copy_guid')}>
                <MdContentCopy size={16} />
              </Button>
            )}
            <Button isIconOnly size='sm' variant='light' onPress={fetchLinuxInfo} aria-label={t('webui.guid.refresh')}>
              <MdRefresh size={16} />
            </Button>
          </div>
          <div className='text-xs text-default-400'>
            {t('webui.guid.linux_guid_formula')}
          </div>
        </div>

        <Divider />

        <div className='flex flex-col gap-1'>
          <div className='text-sm font-medium text-default-700'>Machine ID</div>
          <Chip variant='flat' color='default' className='font-mono text-xs max-w-full'>
            {machineId || t('webui.guid.unknown')}
          </Chip>
          <div className='text-xs text-default-400'>
            {t('webui.guid.machine_id_desc')}
          </div>
        </div>

        <Divider />

        <div className='flex flex-col gap-2'>
          <div className='text-sm font-medium text-default-700'>{t('webui.guid.current_mac')}</div>
          <div className='flex items-center gap-2'>
            {currentMAC
              ? (
                <Chip variant='flat' color='secondary' className='font-mono text-xs max-w-full'>
                  {currentMAC}
                </Chip>
              )
              : (
                <Chip variant='flat' color='warning' className='text-xs'>
                  {t('webui.guid.not_set')}
                </Chip>
              )}
            {currentMAC && (
              <Button isIconOnly size='sm' variant='light' onPress={handleLinuxCopyMAC} aria-label={t('webui.guid.copy_mac')}>
                <MdContentCopy size={16} />
              </Button>
            )}
          </div>
        </div>

        <Divider />

        <div className='flex flex-col gap-2'>
          <div className='text-sm font-medium text-default-700'>{t('webui.guid.set_mac')}</div>
          <div className='flex items-center gap-2'>
            <Input
              size='sm'
              variant='bordered'
              placeholder='xx-xx-xx-xx-xx-xx'
              value={inputMAC}
              onValueChange={setInputMAC}
              isInvalid={inputMAC.length > 0 && !isValidMAC(inputMAC)}
              errorMessage={inputMAC.length > 0 && !isValidMAC(inputMAC) ? t('webui.guid.mac_format') : undefined}
              classNames={{ input: 'font-mono text-sm' }}
              maxLength={17}
            />
          </div>

          {inputMAC && isValidMAC(inputMAC) && (
            <div className='flex flex-col gap-1 p-2 rounded-lg bg-default-100'>
              <div className='text-xs font-medium text-default-500'>{t('webui.guid.preview_guid')}</div>
              <div className='font-mono text-xs text-primary break-all'>
                {computedLinuxGUID}
              </div>
              {computedLinuxGUID !== currentLinuxGUID && (
                <div className='text-xs text-warning-500'>
                  {t('webui.guid.guid_diff_note')}
                </div>
              )}
            </div>
          )}

          <div className='flex items-center gap-2'>
            <Button size='sm' color='primary' variant='flat' isLoading={saving} isDisabled={!isValidMAC(inputMAC) || inputMAC.trim().toLowerCase() === currentMAC} onPress={handleLinuxSaveMAC} startContent={<MdSave size={16} />}>
              {t('webui.guid.save_mac')}
            </Button>
            <Button size='sm' color='danger' variant='flat' isDisabled={!currentMAC} onPress={handleLinuxDelete} startContent={<MdDelete size={16} />}>
              {t('webui.guid.delete_btn')}
            </Button>
            <Button size='sm' color='secondary' variant='flat' isDisabled={!currentMAC} onPress={handleLinuxBackup} startContent={<MdBackup size={16} />}>
              {t('webui.guid.manual_backup')}
            </Button>
          </div>
          <div className='text-xs text-default-400'>
            {t('webui.guid.mac_change_note')}
          </div>
        </div>

        {linuxBackups.length > 0 && (
          <>
            <Divider />
            <div className='flex flex-col gap-2'>
              <div className='text-sm font-medium text-default-700'>
                {t('webui.guid.backup_list')}
                <span className='text-xs text-default-400 ml-2'>{t('webui.guid.click_to_restore')}</span>
              </div>
              <div className='max-h-[160px] overflow-y-auto rounded-lg border border-default-200'>
                <Listbox
                  aria-label={t('webui.guid.backup_list')}
                  selectionMode='none'
                  onAction={(key) => handleLinuxRestore(key as string)}
                >
                  {linuxBackups.map((name) => (
                    <ListboxItem key={name} startContent={<MdRestorePage size={16} className='text-default-400' />} className='font-mono text-xs'>
                      {name}
                    </ListboxItem>
                  ))}
                </Listbox>
              </div>
            </div>
          </>
        )}

        {showRestart && (
          <>
            <Divider />
            <Button size='sm' color='warning' variant='flat' isLoading={restarting} onPress={handleRestart} startContent={<MdRefresh size={16} />}>
              {t('webui.guid.restart_btn')}
            </Button>
          </>
        )}
      </div>
    );
  }

  return (
    <div className={`flex flex-col gap-${compact ? '3' : '4'}`}>
      <div className='flex flex-col gap-2'>
        <div className='text-sm font-medium text-default-700'>{t('webui.guid.current_guid')}</div>
        <div className='flex items-center gap-2'>
          {currentGUID
            ? (
              <Chip variant='flat' color='primary' className='font-mono text-xs max-w-full'>
                {currentGUID}
              </Chip>
            )
            : (
              <Chip variant='flat' color='warning' className='text-xs'>
                {t('webui.guid.not_set')}
              </Chip>
            )}
          {currentGUID && (
            <Button isIconOnly size='sm' variant='light' onPress={handleCopy} aria-label={t('webui.guid.copy_guid')}>
              <MdContentCopy size={16} />
            </Button>
          )}
          <Button isIconOnly size='sm' variant='light' onPress={fetchGUID} aria-label={t('webui.guid.refresh')}>
            <MdRefresh size={16} />
          </Button>
        </div>
      </div>

      <Divider />

      <div className='flex flex-col gap-2'>
        <div className='text-sm font-medium text-default-700'>{t('webui.guid.set_guid')}</div>
        <div className='flex items-center gap-2'>
          <Input
            size='sm'
            variant='bordered'
            placeholder={t('webui.guid.guid_placeholder')}
            value={inputGUID}
            onValueChange={setInputGUID}
            isInvalid={inputGUID.length > 0 && !isValidGUID(inputGUID)}
            errorMessage={inputGUID.length > 0 && !isValidGUID(inputGUID) ? t('webui.guid.invalid_guid') : undefined}
            classNames={{ input: 'font-mono text-sm' }}
            maxLength={32}
          />
        </div>
        <div className='flex items-center gap-2'>
          <Button size='sm' color='primary' variant='flat' isLoading={saving} isDisabled={!isValidGUID(inputGUID) || inputGUID === currentGUID} onPress={handleSave} startContent={<MdSave size={16} />}>
            {t('webui.guid.save_guid')}
          </Button>
          <Button size='sm' color='danger' variant='flat' isDisabled={!currentGUID} onPress={handleDelete} startContent={<MdDelete size={16} />}>
            {t('webui.guid.delete_guid')}
          </Button>
          <Button size='sm' color='secondary' variant='flat' isDisabled={!currentGUID} onPress={handleBackup} startContent={<MdBackup size={16} />}>
            {t('webui.guid.manual_backup')}
          </Button>
        </div>
        <div className='text-xs text-default-400'>
          {t('webui.guid.guid_change_note')}
        </div>
      </div>

      {backups.length > 0 && (
        <>
          <Divider />
          <div className='flex flex-col gap-2'>
            <div className='text-sm font-medium text-default-700'>
              {t('webui.guid.backup_list')}
              <span className='text-xs text-default-400 ml-2'>{t('webui.guid.click_to_restore')}</span>
            </div>
            <div className='max-h-[160px] overflow-y-auto rounded-lg border border-default-200'>
              <Listbox
                aria-label={t('webui.guid.backup_list')}
                selectionMode='none'
                onAction={(key) => handleRestore(key as string)}
              >
                {backups.map((name) => (
                  <ListboxItem key={name} startContent={<MdRestorePage size={16} className='text-default-400' />} className='font-mono text-xs'>
                    {name}
                  </ListboxItem>
                ))}
              </Listbox>
            </div>
          </div>
        </>
      )}

      {showRestart && (
        <>
          <Divider />
          <Button size='sm' color='warning' variant='flat' isLoading={restarting} onPress={handleRestart} startContent={<MdRefresh size={16} />}>
            {t('webui.guid.restart_btn')}
          </Button>
        </>
      )}
    </div>
  );
};

export default GUIDManager;
