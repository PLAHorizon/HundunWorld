/**
 * 标签页与页面配置模型。
 *
 * HarmonyOS 原型包含 9 个 HTML 页面：
 * - 5 个主标签页（底部 TabBar 直接切换）
 * - 4 个次级页面（通过"更多"菜单进入）
 */

/** 主标签配置 */
export interface MainTab {
  /** 标签标题 */
  title: string;
  /** 图标符号（Unicode） */
  icon: string;
  /** rawfile 中的 HTML 文件名 */
  page: string;
}

/** 次级页面配置 */
export interface SecondaryPage {
  /** 页面标题 */
  title: string;
  /** 图标符号 */
  icon: string;
  /** rawfile 中的 HTML 文件名 */
  page: string;
  /** 页面简述 */
  description: string;
}

/** 底部导航 5 个主标签 */
export const MAIN_TABS: MainTab[] = [
  { title: '首页', icon: '⌂', page: 'harmony-home.html' },
  { title: '天气', icon: '☀', page: 'harmony-weather.html' },
  { title: '市场', icon: '✿', page: 'harmony-market.html' },
  { title: '发现', icon: '◎', page: 'harmony-discover.html' },
  { title: '我的', icon: '◙', page: 'harmony-profile.html' },
];

/** "更多"菜单中的 4 个次级页面 */
export const SECONDARY_PAGES: SecondaryPage[] = [
  {
    title: '即时消息',
    icon: '✉',
    page: 'harmony-chat.html',
    description: '花卉交易即时通讯与订单协商'
  },
  {
    title: '音乐播放器',
    icon: '♪',
    page: 'harmony-music.html',
    description: '在线音乐播放与歌单管理'
  },
  {
    title: '市场预报',
    icon: '◈',
    page: 'harmony-forecast.html',
    description: '花卉市场价格趋势预测'
  },
  {
    title: '商家预警',
    icon: '⚡',
    page: 'harmony-alerts.html',
    description: '花卉商家实时预警通知'
  },
];

/** 构造 rawfile 资源 URI */
export function rawfileUri(fileName: string): string {
  return `resource://rawfile/${fileName}`;
}
