import {
  LuFileText,
  LuInfo,
  LuLayoutDashboard,
  LuSettings,
  LuPackage,
  LuFileCode,
} from 'react-icons/lu';

export type SiteConfig = typeof siteConfig;
export interface MenuItem {
  label: string;
  icon?: React.ReactNode;
  autoOpen?: boolean;
  href?: string;
  items?: MenuItem[];
  customIcon?: string;
}

export const siteConfig = {
  name: 'MorningCat',
  description: 'MorningCat WebUI.',
  navItems: [
    {
      label: '基础信息',
      icon: <LuLayoutDashboard className='w-5 h-5' />,
      href: '/',
    },
    {
      label: '猫猫日志',
      icon: <LuFileText className='w-5 h-5' />,
      href: '/logs',
    },
    {
      label: '插件管理',
      icon: <LuPackage className='w-5 h-5' />,
      href: '/plugins',
    },
    {
      label: '插件配置',
      icon: <LuFileCode className='w-5 h-5' />,
      href: '/plugin-config',
    },
    {
      label: '系统配置',
      icon: <LuSettings className='w-5 h-5' />,
      href: '/config',
    },
    {
      label: '关于',
      icon: <LuInfo className='w-5 h-5' />,
      href: '/about',
    },
  ] as MenuItem[],
  links: {
    github: 'https://github.com/NapNeko/NapCatQQ',
    docs: 'https://napcat.napneko.icu/',
  },
};
