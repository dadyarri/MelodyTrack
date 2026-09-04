import { ConfigProvider, theme as antdTheme, type ThemeConfig } from "antd";
import ruRU from "antd/locale/ru_RU";
import { type ReactNode, useCallback, useEffect, useMemo, useState } from "react";

import { ThemeContext, type ThemeContextValue, type ThemeMode } from "./ThemeContext";

const storageKey = "melodytrack.theme";

type ThemePalette = {
  accent: string;
  info: string;
  success: string;
  warning: string;
  error: string;
  text: string;
  textSecondary: string;
  bgBase: string;
  bgContainer: string;
  bgElevated: string;
  fillAlter: string;
  border: string;
  borderSecondary: string;
  borderStrong: string;
  cardShadow: string;
  menuHoverBg: string;
  menuHoverColor: string;
  menuSelectedBg: string;
  menuSelectedColor: string;
  tableRowHoverBg: string;
  inputBg: string;
  infoAlertBg: string;
  infoAlertBorder: string;
};

const lightPalette: ThemePalette = {
  accent: "#8b6226",
  info: "#6f6942",
  success: "#5f7b4b",
  warning: "#a87322",
  error: "#9b4a3c",
  text: "#33271a",
  textSecondary: "#746550",
  bgBase: "#f4e8c8",
  bgContainer: "#f8edcf",
  bgElevated: "#fbf3dc",
  fillAlter: "#efe1be",
  border: "#d4c19a",
  borderSecondary: "#e8dcc2",
  borderStrong: "#d2bd8f",
  cardShadow: "0 8px 24px rgba(72, 52, 27, 0.10)",
  menuHoverBg: "#e6d5ad",
  menuHoverColor: "#33271a",
  menuSelectedBg: "#d8bd7a",
  menuSelectedColor: "#2f2418",
  tableRowHoverBg: "#fbf2dd",
  inputBg: "#fbf3dc",
  infoAlertBg: "color-mix(in srgb, #8b6226 10%, #f8edcf)",
  infoAlertBorder: "color-mix(in srgb, #8b6226 24%, #d4c19a)",
};

const darkPalette: ThemePalette = {
  accent: "#d0a35b",
  info: "#b78d4a",
  success: "#7ca46d",
  warning: "#d4a24f",
  error: "#d27a70",
  text: "#f1e7d1",
  textSecondary: "#bcae94",
  bgBase: "#15120f",
  bgContainer: "#1f1914",
  bgElevated: "#261f18",
  fillAlter: "#31271d",
  border: "#5d4a35",
  borderSecondary: "#463727",
  borderStrong: "#5d4a35",
  cardShadow: "0 14px 30px rgba(0, 0, 0, 0.35)",
  menuHoverBg: "#2f241a",
  menuHoverColor: "#fff0d4",
  menuSelectedBg: "#5c4122",
  menuSelectedColor: "#fff0d4",
  tableRowHoverBg: "#2b2219",
  inputBg: "#261f18",
  infoAlertBg: "color-mix(in srgb, #d0a35b 10%, #1f1914)",
  infoAlertBorder: "color-mix(in srgb, #d0a35b 24%, #5d4a35)",
};

function createThemeConfig(palette: ThemePalette, algorithm?: ThemeConfig["algorithm"]): ThemeConfig {
  return {
    algorithm,
    token: {
      colorPrimary: palette.accent,
      colorInfo: palette.info,
      colorSuccess: palette.success,
      colorWarning: palette.warning,
      colorError: palette.error,
      colorText: palette.text,
      colorTextSecondary: palette.textSecondary,
      colorBgBase: palette.bgBase,
      colorBgContainer: palette.bgContainer,
      colorBgElevated: palette.bgElevated,
      colorFillAlter: palette.fillAlter,
      colorBorder: palette.border,
      colorBorderSecondary: palette.borderSecondary,
      colorInfoBg: palette.infoAlertBg,
      colorInfoBorder: palette.infoAlertBorder,
      borderRadius: 6,
      boxShadow: palette.cardShadow,
      fontFamily: '"IBM Plex Sans", "Segoe UI", sans-serif',
    },
    components: {
      Alert: {
        defaultPadding: "10px 14px",
        withDescriptionPadding: "12px 14px",
      },
      Button: {
        primaryShadow: "none",
        defaultShadow: "none",
        dangerShadow: "none",
        defaultBg: palette.bgElevated,
        defaultColor: palette.text,
        defaultBorderColor: palette.border,
        defaultHoverBg: palette.bgContainer,
        defaultHoverColor: palette.text,
        defaultHoverBorderColor: palette.accent,
        defaultActiveBg: palette.bgContainer,
        defaultActiveColor: palette.text,
        defaultActiveBorderColor: palette.accent,
      },
      Card: {
        colorBgContainer: palette.bgContainer,
        headerBg: palette.bgContainer,
      },
      DatePicker: {
        activeBg: palette.inputBg,
        hoverBg: palette.inputBg,
        activeBorderColor: palette.accent,
        hoverBorderColor: palette.accent,
        cellHoverBg: palette.fillAlter,
        cellActiveWithRangeBg: palette.menuSelectedBg,
        cellHoverWithRangeBg: palette.fillAlter,
      },
      Input: {
        addonBg: palette.inputBg,
        activeBg: palette.inputBg,
        hoverBg: palette.inputBg,
        activeBorderColor: palette.accent,
        hoverBorderColor: palette.accent,
      },
      Layout: {
        bodyBg: palette.bgBase,
        headerBg: palette.bgContainer,
        siderBg: palette.fillAlter,
      },
      Menu: {
        itemBg: "transparent",
        itemColor: palette.textSecondary,
        itemHoverBg: palette.menuHoverBg,
        itemHoverColor: palette.menuHoverColor,
        itemSelectedBg: palette.menuSelectedBg,
        itemSelectedColor: palette.menuSelectedColor,
        itemMarginInline: 0,
        itemMarginBlock: 2,
        itemBorderRadius: 8,
        itemHeight: 36,
        itemPaddingInline: 14,
        popupBg: palette.bgElevated,
      },
      Modal: {
        headerBg: palette.bgContainer,
        contentBg: palette.bgContainer,
        footerBg: palette.bgContainer,
        titleColor: palette.text,
      },
      Popover: {
        titleMinWidth: 160,
      },
      Select: {
        selectorBg: palette.inputBg,
        clearBg: palette.inputBg,
        optionActiveBg: palette.fillAlter,
        optionSelectedBg: palette.menuSelectedBg,
        optionSelectedColor: palette.text,
        activeBorderColor: palette.accent,
        hoverBorderColor: palette.accent,
        activeOutlineColor: "transparent",
      },
      Table: {
        borderColor: palette.borderSecondary,
        headerBg: palette.fillAlter,
        headerColor: palette.text,
        headerSplitColor: palette.borderStrong,
        rowHoverBg: palette.tableRowHoverBg,
        headerSortActiveBg: palette.fillAlter,
        headerSortHoverBg: palette.fillAlter,
      },
      Typography: {
        titleMarginBottom: 0,
      },
    },
  };
}

const lightTheme = createThemeConfig(lightPalette);
const darkTheme = createThemeConfig(darkPalette, antdTheme.darkAlgorithm);

function getInitialMode(): ThemeMode {
  const stored = localStorage.getItem(storageKey);
  return stored === "dark" ? "dark" : "light";
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<ThemeMode>(getInitialMode);

  const changeMode = useCallback((nextMode: ThemeMode) => {
    const transitionDocument = document as Document & {
      startViewTransition?: (updateCallback: () => void) => ViewTransition;
    };

    if (typeof transitionDocument.startViewTransition !== "function") {
      setMode(nextMode);
      return;
    }

    transitionDocument.startViewTransition(() => {
      setMode(nextMode);
    });
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = mode;
    localStorage.setItem(storageKey, mode);
  }, [mode]);

  const value = useMemo<ThemeContextValue>(
    () => ({
      mode,
      toggleMode: () => {
        const nextMode = mode === "light" ? "dark" : "light";
        changeMode(nextMode);
      },
    }),
    [changeMode, mode],
  );

  return (
    <ThemeContext.Provider value={value}>
      <ConfigProvider locale={ruRU} theme={mode === "dark" ? darkTheme : lightTheme}>
        {children}
      </ConfigProvider>
    </ThemeContext.Provider>
  );
}

export function ClientPortalThemeProvider({ children }: { children: ReactNode }) {
  return (
    <ConfigProvider locale={ruRU} theme={{ ...lightTheme, inherit: false }}>
      <div data-theme="light">{children}</div>
    </ConfigProvider>
  );
}
