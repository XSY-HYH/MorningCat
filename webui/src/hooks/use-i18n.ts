import { useState, useEffect, useCallback } from 'react';
import WebUIManager from '@/controllers/webui_manager';

let cachedTranslations: Record<string, string> = {};
let cachedLang = '';

const useI18n = () => {
  const [translations, setTranslations] = useState<Record<string, string>>(cachedTranslations);
  const [lang, setLang] = useState(cachedLang);

  const loadTranslations = useCallback(async () => {
    try {
      const data = await WebUIManager.getTranslations();
      if (data) {
        cachedTranslations = data.translations;
        cachedLang = data.lang;
        setTranslations(data.translations);
        setLang(data.lang);
      }
    } catch (e) {
      console.error('Failed to load translations:', e);
    }
  }, []);

  useEffect(() => {
    if (Object.keys(cachedTranslations).length === 0) {
      loadTranslations();
    }
  }, [loadTranslations]);

  const t = useCallback((key: string, ...args: (string | number)[]): string => {
    let text = translations[key] || key;
    args.forEach((v, i) => {
      text = text.replace(new RegExp(`\\{${i}\\}`, 'g'), String(v));
    });
    return text;
  }, [translations]);

  return { t, lang, translations, loadTranslations };
};

export default useI18n;
