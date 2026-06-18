import { Button } from '@heroui/button';
import { Input } from '@heroui/input';
import { Select, SelectItem, SelectSection } from '@heroui/select';
import { useLocalStorage } from '@uidotdev/usehooks';
import clsx from 'clsx';
import { useEffect, useRef, useState } from 'react';
import toast from 'react-hot-toast';

import key from '@/const/key';
import useI18n from '@/hooks/use-i18n';

import WebUIManager, { type MessageEntry } from '@/controllers/webui_manager';

interface GroupInfo {
  groupId: number;
  groupName: string;
}

interface FriendInfo {
  userId: number;
  nickname: string;
  remark: string;
}

export default function MessagesPage () {
  const { t } = useI18n();
  const [messages, setMessages] = useState<MessageEntry[]>([]);
  const [backgroundImage] = useLocalStorage<string>(key.backgroundImage, '');
  const hasBackground = !!backgroundImage;
  const listRef = useRef<HTMLDivElement>(null);
  const [autoScroll, setAutoScroll] = useState(true);
  const [connected, setConnected] = useState(false);

  const [sendType, setSendType] = useState<'private' | 'group'>('group');
  const [selectedTarget, setSelectedTarget] = useState<string>('');
  const [sendMessage, setSendMessage] = useState('');
  const [sending, setSending] = useState(false);

  const [groupList, setGroupList] = useState<GroupInfo[]>([]);
  const [friendList, setFriendList] = useState<FriendInfo[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchContacts = async () => {
      try {
        const token = localStorage.getItem(key.token);
        const headers = token ? { Authorization: `Bearer ${JSON.parse(token)}` } : {};

        const [groupRes, friendRes] = await Promise.all([
          fetch('/api/message/group_list', { headers }),
          fetch('/api/message/friend_list', { headers }),
        ]);

        const groupResult = await groupRes.json();
        const friendResult = await friendRes.json();

        if (groupResult.code === 0) {
          setGroupList(groupResult.data || []);
        }
        if (friendResult.code === 0) {
          setFriendList(friendResult.data || []);
        }
      } catch {
        toast.error(t('webui.messages.load_contacts_failed'));
      } finally {
        setLoading(false);
      }
    };

    fetchContacts();
  }, [t]);

  useEffect(() => {
    let source: { close: () => void } | null = null;

    try {
      source = WebUIManager.getRealTimeMessages((msg) => {
        setConnected(true);
        setMessages((prev) => {
          const next = [...prev, msg];
          if (next.length > 200) {
            return next.slice(-200);
          }
          return next;
        });
      });
    } catch {
      // connection failed
    }

    return () => {
      source?.close();
    };
  }, []);

  useEffect(() => {
    if (autoScroll && listRef.current) {
      listRef.current.scrollTop = listRef.current.scrollHeight;
    }
  }, [messages, autoScroll]);

  const handleScroll = () => {
    if (!listRef.current) return;
    const { scrollTop, scrollHeight, clientHeight } = listRef.current;
    setAutoScroll(scrollHeight - scrollTop - clientHeight < 50);
  };

  const handleSend = async () => {
    if (!selectedTarget || !sendMessage.trim()) {
      toast.error(t('webui.messages.select_target'));
      return;
    }

    const targetId = parseInt(selectedTarget, 10);
    if (isNaN(targetId)) {
      toast.error(t('webui.messages.invalid_id'));
      return;
    }

    setSending(true);
    try {
      const token = localStorage.getItem(key.token);
      const response = await fetch('/api/message/send', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${JSON.parse(token)}` } : {}),
        },
        body: JSON.stringify({
          type: sendType,
          target: targetId,
          message: sendMessage,
        }),
      });
      const result = await response.json();
      if (result.code === 0 && result.data?.success) {
        toast.success(t('webui.messages.send_success'));
        setSendMessage('');
      } else {
        toast.error(result.message || t('webui.messages.send_failed'));
      }
    } catch {
      toast.error(t('webui.messages.send_failed'));
    } finally {
      setSending(false);
    }
  };

  const handleCopyContent = (content: string) => {
    if (!content) return;
    try {
      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(content);
        toast.success(t('webui.messages.copied'));
      } else {
        const textarea = document.createElement('textarea');
        textarea.value = content;
        textarea.style.position = 'fixed';
        textarea.style.left = '-9999px';
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        document.body.removeChild(textarea);
        toast.success(t('webui.messages.copied'));
      }
    } catch {
      toast.error(t('webui.messages.copy_failed'));
    }
  };

  const formatTime = (timeStr: string) => {
    try {
      const d = new Date(timeStr);
      return d.toLocaleTimeString('zh-CN', { hour12: false });
    } catch {
      return '';
    }
  };

  const getDisplayLabel = (msg: MessageEntry) => {
    if (msg.messageType === 'group') {
      return `[${msg.groupName}]-[${msg.senderName}]`;
    }
    return `${t('webui.messages.private_label')}-[${msg.senderName}]`;
  };

  return (
    <>
      <title>{t('webui.messages.title')}</title>
      <div className='h-[calc(100vh_-_8rem)] flex flex-col gap-4 items-center pt-4 px-2'>
        <div className={clsx(
          'w-full flex-1 h-full overflow-hidden rounded-2xl border backdrop-blur-sm transition-all shadow-sm flex flex-col',
          hasBackground ? 'bg-white/20 dark:bg-black/10 border-white/40 dark:border-white/10' : 'bg-white/60 dark:bg-black/40 border-white/40 dark:border-white/10'
        )}
        >
          <div className='flex items-center justify-between px-4 py-2 border-b border-white/10'>
            <div className='flex items-center gap-2'>
              <span className='text-sm font-medium'>{t('webui.messages.heading')}</span>
              <span className={clsx(
                'text-xs px-1.5 py-0.5 rounded',
                connected ? 'bg-success-100 text-success-700 dark:bg-success-900/30 dark:text-success-400' : 'bg-danger-100 text-danger-700 dark:bg-danger-900/30 dark:text-danger-400'
              )}>
                {connected ? t('webui.messages.connected') : t('webui.messages.disconnected')}
              </span>
              <span className='text-xs text-default-400'>{t('webui.messages.count', messages.length)}</span>
            </div>
            {!autoScroll && (
              <button
                className='text-xs px-2 py-1 rounded bg-primary-500 text-white hover:bg-primary-600 transition-colors'
                onClick={() => {
                  setAutoScroll(true);
                  if (listRef.current) {
                    listRef.current.scrollTop = listRef.current.scrollHeight;
                  }
                }}
              >
                {t('webui.messages.scroll_bottom')}
              </button>
            )}
          </div>

          <div className='flex items-center gap-2 px-4 py-2 border-b border-white/10 bg-default-50/30'>
            <Select
              className='w-32'
              size='sm'
              aria-label={t('webui.messages.type_label')}
              selectedKeys={new Set([sendType])}
              onSelectionChange={(keys) => {
                const selected = Array.from(keys)[0] as string;
                if (selected === 'private' || selected === 'group') {
                  setSendType(selected);
                  setSelectedTarget('');
                }
              }}
            >
              <SelectItem key='group'>{t('webui.messages.group')}</SelectItem>
              <SelectItem key='private'>{t('webui.messages.private')}</SelectItem>
            </Select>

            <Select
              className='flex-1'
              size='sm'
              key={sendType}
              aria-label={t('webui.messages.send_target')}
              placeholder={sendType === 'group' ? t('webui.messages.select_group') : t('webui.messages.select_friend')}
              selectedKeys={selectedTarget ? new Set([selectedTarget]) : new Set()}
              onSelectionChange={(keys) => {
                const selected = Array.from(keys)[0] as string;
                setSelectedTarget(selected || '');
              }}
              isLoading={loading}
            >
              {sendType === 'group' ? (
                <SelectSection key='groups' title={t('webui.messages.groups_section')}>
                  {groupList.map((g) => (
                    <SelectItem key={String(g.groupId)} textValue={`${g.groupName} (${g.groupId})`}>
                      {g.groupName} ({g.groupId})
                    </SelectItem>
                  ))}
                </SelectSection>
              ) : (
                <SelectSection key='friends' title={t('webui.messages.friends_section')}>
                  {friendList.map((f) => (
                    <SelectItem key={String(f.userId)} textValue={`${f.remark || f.nickname} (${f.userId})`}>
                      {f.remark || f.nickname} ({f.userId})
                    </SelectItem>
                  ))}
                </SelectSection>
              )}
            </Select>

            <Input
              className='flex-1'
              size='sm'
              placeholder={t('webui.messages.cq_placeholder')}
              value={sendMessage}
              onValueChange={setSendMessage}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault();
                  handleSend();
                }
              }}
            />

            <Button
              color='primary'
              size='sm'
              isLoading={sending}
              onPress={handleSend}
              isDisabled={!selectedTarget || !sendMessage.trim()}
            >
              {t('webui.messages.send')}
            </Button>
          </div>

          <div
            ref={listRef}
            onScroll={handleScroll}
            className='flex-1 overflow-y-auto px-4 py-2 space-y-1'
          >
            {messages.length === 0 && (
              <div className='flex items-center justify-center h-full text-default-400 text-sm'>
                {t('webui.messages.waiting')}
              </div>
            )}
            {messages.map((msg, idx) => (
              <div key={idx} className={clsx(
                'flex items-start gap-2 py-1 px-2 rounded text-sm',
                msg.messageType === 'group'
                  ? 'hover:bg-primary-50 dark:hover:bg-primary-900/20'
                  : 'hover:bg-secondary-50 dark:hover:bg-secondary-900/20'
              )}>
                <span className='text-default-400 text-xs whitespace-nowrap mt-0.5'>
                  {formatTime(msg.time)}
                </span>
                <span className={clsx(
                  'font-medium whitespace-nowrap',
                  msg.messageType === 'group' ? 'text-primary-600 dark:text-primary-400' : 'text-secondary-600 dark:text-secondary-400'
                )}>
                  {getDisplayLabel(msg)}
                </span>
                <span
                  className={clsx(
                    'cursor-pointer hover:underline flex-1',
                    msg.hasUnsupportedContent ? 'text-warning-500' : 'text-foreground'
                  )}
                  onClick={() => handleCopyContent(msg.content)}
                  title={t('webui.messages.copy_message')}
                >
                  {msg.content || (msg.hasUnsupportedContent ? t('webui.messages.unsupported') : '')}
                </span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </>
  );
}
