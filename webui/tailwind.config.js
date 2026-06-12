import { heroui } from '@heroui/theme';

/** @type {import('tailwindcss').Config} */
export default {
  content: [
    './index.html',
    './src/layouts/**/*.{js,ts,jsx,tsx,mdx}',
    './src/pages/**/*.{js,ts,jsx,tsx,mdx}',
    './src/components/**/*.{js,ts,jsx,tsx,mdx}',
    './node_modules/@heroui/theme/dist/**/*.{js,ts,jsx,tsx}',
  ],
  safelist: [
    {
      pattern:
        /bg-(primary|secondary|success|danger|warning|default)-(50|100|200|300|400|500|600|700|800|900)/,
    },
  ],
  theme: {
    extend: {
      fontFamily: {
        mono: [
          'ui-monospace',
          'SFMono-Regular',
          'SF Mono',
          'Menlo',
          'Consolas',
          'Liberation Mono',
          'JetBrains Mono',
          'monospace',
        ],
      },
    },
  },
  darkMode: 'class',
  plugins: [
    heroui({
      themes: {
        light: {
          colors: {
            primary: {
              DEFAULT: '#3B82F6',
              foreground: '#fff',
              50: '#EFF6FF',
              100: '#DBEAFE',
              200: '#BFDBFE',
              300: '#93C5FD',
              400: '#60A5FA',
              500: '#3B82F6',
              600: '#2563EB',
              700: '#1D4ED8',
              800: '#1E40AF',
              900: '#1E3A8A',
            },
            secondary: {
              DEFAULT: '#88C0D0',
              foreground: '#fff',
              50: '#F0F9FC',
              100: '#D7F0F8',
              200: '#AEE1F2',
              300: '#88C0D0',
              400: '#5E9FBF',
              500: '#4C8DAE',
              600: '#3A708C',
              700: '#2A546A',
              800: '#1A3748',
              900: '#0B1B26',
            },
            danger: {
              DEFAULT: '#2563EB',
              foreground: '#fff',
              50: '#EFF6FF',
              100: '#DBEAFE',
              200: '#BFDBFE',
              300: '#93C5FD',
              400: '#60A5FA',
              500: '#3B82F6',
              600: '#2563EB',
              700: '#1D4ED8',
              800: '#1E40AF',
              900: '#1E3A8A',
            },
          },
        },
        dark: {
          colors: {
            primary: {
              DEFAULT: '#3B82F6',
              foreground: '#fff',
              50: '#1E3A8A',
              100: '#1E40AF',
              200: '#1D4ED8',
              300: '#2563EB',
              400: '#3B82F6',
              500: '#60A5FA',
              600: '#93C5FD',
              700: '#BFDBFE',
              800: '#DBEAFE',
              900: '#EFF6FF',
            },
            secondary: {
              DEFAULT: '#60A5FA',
              foreground: '#fff',
              50: '#1E3A8A',
              100: '#1E40AF',
              200: '#1D4ED8',
              300: '#2563EB',
              400: '#3B82F6',
              500: '#60A5FA',
              600: '#93C5FD',
              700: '#BFDBFE',
              800: '#DBEAFE',
              900: '#EFF6FF',
            },
            danger: {
              DEFAULT: '#2563EB',
              foreground: '#fff',
              50: '#1E3A8A',
              100: '#1E40AF',
              200: '#1D4ED8',
              300: '#2563EB',
              400: '#3B82F6',
              500: '#60A5FA',
              600: '#93C5FD',
              700: '#BFDBFE',
              800: '#DBEAFE',
              900: '#EFF6FF',
            },
          },
        },
      },
    }),
  ],
};
