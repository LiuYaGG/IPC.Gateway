import type { ThemeConfig } from 'ant-design-vue/es/config-provider/context'

// Shared palette for the approved light Monet blue interface.
export const gatewayTheme: ThemeConfig = {
  token: {
    colorPrimary: '#567b9f',
    colorInfo: '#567b9f',
    colorSuccess: '#519778',
    colorWarning: '#bf9147',
    colorError: '#cb6464',
    colorText: '#000000',
    colorTextSecondary: '#7a8a9e',
    colorBgLayout: '#f0f6fc',
    colorBgContainer: '#ffffff',
    colorBorder: '#dce6f0',
    colorBorderSecondary: '#e8eef5',
    colorFillAlter: '#f5f8fc',
    fontFamily: '"Cascadia Code", "Microsoft YaHei", sans-serif',
    fontSize: 14.333,
    fontSizeSM: 13.333,
    fontSizeLG: 15.333,
    controlHeight: 30,
    controlHeightSM: 26,
    borderRadius: 5,
    borderRadiusLG: 6,
    boxShadow: 'none',
    boxShadowSecondary: '0 4px 18px rgba(52, 67, 89, 0.08)'
  },
  components: {
    Menu: {
      colorItemBg: 'transparent',
      colorSubItemBg: 'transparent',
      colorItemText: '#000000',
      colorItemTextHover: '#567b9f',
      colorItemTextSelected: '#567b9f',
      colorItemBgSelected: '#e3edf7',
      colorItemBgHover: '#f0f6fc',
      colorGroupTitle: '#7a8a9e',
      itemMarginInline: 8,
      radiusItem: 4
    },
    Table: { fontSize: 13.333 },
    Card: { fontSize: 14.333 }
  }
}
