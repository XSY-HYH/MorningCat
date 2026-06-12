import { Button } from '@heroui/button';
import { CardBody, CardHeader } from '@heroui/card';
import { Image } from '@heroui/image';
import { Input } from '@heroui/input';
import { useLocalStorage } from '@uidotdev/usehooks';
import { useEffect, useState } from 'react';
import { toast } from 'react-hot-toast';
import { IoKeyOutline, IoPersonOutline } from 'react-icons/io5';
import { useNavigate } from 'react-router-dom';

import logo from '@/assets/images/logo.png';

import key from '@/const/key';

import HoverEffectCard from '@/components/effect_card';
import { title } from '@/components/primitives';

import WebUIManager from '@/controllers/webui_manager';
import PureLayout from '@/layouts/pure';
import { motion } from 'motion/react';

export default function WebLoginPage () {
  const urlSearchParams = new URLSearchParams(window.location.search);
  const token = urlSearchParams.get('token');
  const navigate = useNavigate();
  const [username, setUsername] = useState<string>('');
  const [password, setPassword] = useState<string>(token || '');
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [isPasskeyLoading, setIsPasskeyLoading] = useState<boolean>(true);
  const [, setLocalToken] = useLocalStorage<string>(key.token, '');

  function base64UrlToUint8Array (base64Url: string): Uint8Array {
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const binaryString = atob(base64);
    const bytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
      bytes[i] = binaryString.charCodeAt(i);
    }
    return bytes;
  }

  function uint8ArrayToBase64Url (uint8Array: Uint8Array): string {
    const base64 = btoa(String.fromCharCode(...uint8Array));
    return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
  }

  const tryPasskeyLogin = async () => {
    if (!navigator.credentials || !navigator.credentials.get) {
      return;
    }
    try {
      const options = await WebUIManager.generatePasskeyAuthenticationOptions();

      const credential = await navigator.credentials.get({
        publicKey: {
          challenge: base64UrlToUint8Array(options.challenge) as BufferSource,
          allowCredentials: options.allowCredentials?.map((cred: any) => ({
            id: base64UrlToUint8Array(cred.id) as BufferSource,
            type: cred.type,
            transports: cred.transports,
          })),
          userVerification: options.userVerification,
        },
      }) as PublicKeyCredential;

      if (!credential) {
        throw new Error('Passkey authentication cancelled');
      }

      const authResponse = credential.response as AuthenticatorAssertionResponse;
      const response = {
        id: credential.id,
        rawId: uint8ArrayToBase64Url(new Uint8Array(credential.rawId)),
        response: {
          authenticatorData: uint8ArrayToBase64Url(new Uint8Array(authResponse.authenticatorData)),
          clientDataJSON: uint8ArrayToBase64Url(new Uint8Array(authResponse.clientDataJSON)),
          signature: uint8ArrayToBase64Url(new Uint8Array(authResponse.signature)),
          userHandle: authResponse.userHandle ? uint8ArrayToBase64Url(new Uint8Array(authResponse.userHandle)) : null,
        },
        type: credential.type,
      };

      const data = await WebUIManager.verifyPasskeyAuthentication(response);

      if (data && data.Credential) {
        setLocalToken(data.Credential);
        navigate('/', { replace: true });
        return true;
      }
    } catch (error) {
      console.log('Passkey login failed or not available:', error);
    }
    return false;
  };

  const onSubmit = async () => {
    if (!username) {
      toast.error('请输入用户名');
      return;
    }
    if (!password) {
      toast.error('请输入密码');
      return;
    }
    setIsLoading(true);
    try {
      const data = await WebUIManager.loginWithCredentials(username, password);

      if (data) {
        setLocalToken(data);
        navigate('/', { replace: true });
      }
    } catch (error) {
      toast.error((error as Error).message);
    } finally {
      setIsLoading(false);
    }
  };

  const handleKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Enter' && !isLoading && !isPasskeyLoading) {
      onSubmit();
    }
  };

  useEffect(() => {
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [username, password, isLoading, isPasskeyLoading]);

  useEffect(() => {
    if (token) {
      onSubmit();
      return;
    }

    tryPasskeyLogin().finally(() => {
      setIsPasskeyLoading(false);
    });
  }, []);

  return (
    <>
      <title>WebUI登录 - MorningCat WebUI</title>
      <PureLayout>
        <motion.div
          initial={{ opacity: 0, y: 20, scale: 0.95 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          transition={{ duration: 0.5, type: 'spring', stiffness: 120, damping: 20 }}
          className='w-[608px] max-w-full py-8 px-2 md:px-8 overflow-hidden'
        >
          <HoverEffectCard
            className='items-center gap-4 pt-0 pb-6 bg-default-50'
            maxXRotation={3}
            maxYRotation={3}
          >
            <CardHeader className='inline-block max-w-lg text-center justify-center'>
              <div className='flex items-center justify-center w-full gap-2 pt-10'>
                <Image alt='logo' height='7em' src={logo} />
                <div>
                  <span className={title()}>Web&nbsp;</span>
                  <span className={title({ color: 'violet' })}>
                    Login&nbsp;
                  </span>
                </div>
              </div>
            </CardHeader>

            <CardBody className='flex gap-5 py-5 px-5 md:px-10'>
              {isPasskeyLoading && (
                <div className='text-center text-small text-default-600 dark:text-default-400 px-2'>
                  正在检查Passkey...
                </div>
              )}
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  onSubmit();
                }}
                className='flex flex-col gap-4'
              >
                <Input
                  isClearable
                  type='text'
                  name='username'
                  autoComplete='username'
                  classNames={{
                    label: 'text-black/50 dark:text-white/90',
                    input: [
                      'bg-transparent',
                      'text-black/90 dark:text-white/90',
                      'placeholder:text-default-700/50 dark:placeholder:text-white/60',
                    ],
                    innerWrapper: 'bg-transparent',
                    inputWrapper: [
                      'shadow-xl',
                      'bg-default-100/70',
                      'dark:bg-default/60',
                      'backdrop-blur-xl',
                      'backdrop-saturate-200',
                      'hover:bg-default-0/70',
                      'dark:hover:bg-default/70',
                      'group-data-[focus=true]:bg-default-100/50',
                      'dark:group-data-[focus=true]:bg-default/60',
                      '!cursor-text',
                    ],
                  }}
                  isDisabled={isLoading || isPasskeyLoading}
                  label='用户名'
                  placeholder='请输入用户名'
                  radius='lg'
                  size='lg'
                  startContent={
                    <IoPersonOutline className='text-black/50 mb-0.5 dark:text-white/90 text-slate-400 pointer-events-none flex-shrink-0' />
                  }
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  onClear={() => setUsername('')}
                />
                <Input
                  isClearable
                  type='password'
                  name='password'
                  autoComplete='current-password'
                  classNames={{
                    label: 'text-black/50 dark:text-white/90',
                    input: [
                      'bg-transparent',
                      'text-black/90 dark:text-white/90',
                      'placeholder:text-default-700/50 dark:placeholder:text-white/60',
                    ],
                    innerWrapper: 'bg-transparent',
                    inputWrapper: [
                      'shadow-xl',
                      'bg-default-100/70',
                      'dark:bg-default/60',
                      'backdrop-blur-xl',
                      'backdrop-saturate-200',
                      'hover:bg-default-0/70',
                      'dark:hover:bg-default/70',
                      'group-data-[focus=true]:bg-default-100/50',
                      'dark:group-data-[focus=true]:bg-default/60',
                      '!cursor-text',
                    ],
                  }}
                  isDisabled={isLoading || isPasskeyLoading}
                  label='密码'
                  placeholder='请输入密码'
                  radius='lg'
                  size='lg'
                  startContent={
                    <IoKeyOutline className='text-black/50 mb-0.5 dark:text-white/90 text-slate-400 pointer-events-none flex-shrink-0' />
                  }
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  onClear={() => setPassword('')}
                />
              </form>
              <Button
                className='mx-10 mt-10 text-lg py-7'
                color='primary'
                isLoading={isLoading}
                radius='full'
                size='lg'
                variant='shadow'
                onPress={onSubmit}
              >
                {!isLoading && (
                  <Image
                    alt='logo'
                    classNames={{
                      wrapper: '-ml-8',
                    }}
                    height='2em'
                    src={logo}
                  />
                )}
                登录
              </Button>
            </CardBody>
          </HoverEffectCard>
        </motion.div>
      </PureLayout>
    </>
  );
}
