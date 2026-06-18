import { Card, CardBody } from '@heroui/card';
import { Chip } from '@heroui/chip';
import { Divider } from '@heroui/divider';
import { Image } from '@heroui/image';
import { Spinner } from '@heroui/spinner';
import { useEffect, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import remarkBreaks from 'remark-breaks';

import logo from '@/assets/images/logo.png';
import WebUIManager from '@/controllers/webui_manager';
import useI18n from '@/hooks/use-i18n';

export default function AboutPage () {
  const { t } = useI18n();
  const [content, setContent] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [version, setVersion] = useState<string>('');

  useEffect(() => {
    loadAboutContent();
    loadVersion();
  }, []);

  const loadAboutContent = async () => {
    try {
      const response = await fetch('/api/base/about');
      const result = await response.json();
      if (result.code === 0 && result.data?.content) {
        setContent(result.data.content);
      } else {
        setError(result.message || t('webui.about.load_failed'));
      }
    } catch (err) {
      setError(t('webui.about.load_content_failed'));
    } finally {
      setLoading(false);
    }
  };

  const loadVersion = async () => {
    try {
      const data = await WebUIManager.GetOneBotVersion();
      setVersion(data?.version || '');
    } catch {
      // ignore
    }
  };

  const cardStyle = 'bg-default/40 backdrop-blur-lg border-none shadow-none';

  return (
    <div className='flex flex-col h-full w-full gap-6 p-2 md:p-6'>
      <title>{t('webui.about.title')}</title>

      <div className='flex flex-col gap-2'>
        <h1 className='text-2xl font-bold flex items-center gap-3 text-default-900'>
          <Image src={logo} alt='MorningCat Logo' width={32} height={32} />
          {t('webui.about.heading')}
        </h1>
        <div className='flex items-center gap-4 text-small text-default-500'>
          <p>{t('webui.about.description')}</p>
          <Divider orientation='vertical' className='h-4' />
          <div className='flex items-center gap-2'>
            {version && <Chip size='sm' color='primary' variant='flat'>v{version}</Chip>}
          </div>
        </div>
      </div>

      <Divider className='opacity-50' />

      <div className='grid grid-cols-1 lg:grid-cols-3 gap-6 flex-grow'>
        <div className='lg:col-span-2'>
          <Card shadow='sm' className={cardStyle}>
            <CardBody className='py-6 px-6'>
              {loading ? (
                <div className='flex items-center justify-center h-[200px]'>
                  <Spinner size='lg' />
                </div>
              ) : error ? (
                <div className='text-center text-danger py-8'>{error}</div>
              ) : (
                <div className='prose prose-sm dark:prose-invert max-w-none'>
                  <ReactMarkdown
                    remarkPlugins={[remarkGfm, remarkBreaks]}
                    components={{
                      p: ({ children }) => <p className='mb-3 last:mb-0'>{children}</p>,
                      li: ({ children }) => <li className='mb-1'>{children}</li>,
                      ul: ({ children }) => <ul className='mb-3 list-disc pl-4'>{children}</ul>,
                      h1: ({ children }) => <h1 className='text-xl font-bold mb-4'>{children}</h1>,
                      h2: ({ children }) => <h2 className='text-lg font-bold mt-4 mb-2'>{children}</h2>,
                      hr: () => <hr className='my-4 border-default-200' />,
                    }}
                  >
                    {content}
                  </ReactMarkdown>
                </div>
              )}
            </CardBody>
          </Card>
        </div>

        <div>
          <Card shadow='sm' className={cardStyle}>
            <CardBody className='py-4'>
              <h2 className='text-lg font-bold mb-3'>{t('webui.about.tech_stack')}</h2>
              <div className='flex flex-wrap gap-2'>
                {['.NET 10', 'ASP.NET Core', 'React 19', 'TypeScript', 'HeroUI', 'Vite'].map((tech) => (
                  <Chip key={tech} size='sm' variant='flat' className='bg-default-100/50 text-default-600'>
                    {tech}
                  </Chip>
                ))}
              </div>
            </CardBody>
          </Card>
        </div>
      </div>

      <div className='w-full text-center text-tiny text-default-400 py-4 mt-auto flex flex-col items-center gap-1'>
        <p className='flex items-center justify-center gap-1'>
          Made with <span className='text-danger'>❤️</span> by XSY_xiaoqi
        </p>
      </div>
    </div>
  );
}
