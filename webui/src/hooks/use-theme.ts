import { useEffect, useMemo } from 'react';

const ThemeProps = {
  key: 'theme',
  light: 'light',
  dark: 'dark',
} as const;

type Theme = typeof ThemeProps.light | typeof ThemeProps.dark;

export const useTheme = () => {
  const theme: Theme = ThemeProps.dark;

  const isDark = useMemo(() => {
    return true;
  }, []);

  const isLight = useMemo(() => {
    return false;
  }, []);

  const _setTheme = (theme: Theme) => {
    document.documentElement.classList.remove(ThemeProps.light, ThemeProps.dark);
    document.documentElement.classList.add(theme);
  };

  const setDarkTheme = () => _setTheme(ThemeProps.dark);

  useEffect(() => {
    _setTheme(ThemeProps.dark);
  });

  return { theme, isDark, isLight, setDarkTheme };
};
