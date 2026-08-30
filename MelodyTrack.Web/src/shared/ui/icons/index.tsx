import "./icons.css";

import type { LucideIcon, LucideProps } from "lucide-react";
import BadgeInfo from "lucide-react/dist/esm/icons/badge-info";
import BadgeRussianRuble from "lucide-react/dist/esm/icons/badge-russian-ruble";
import Bell from "lucide-react/dist/esm/icons/bell";
import Blocks from "lucide-react/dist/esm/icons/blocks";
import BookOpenText from "lucide-react/dist/esm/icons/book-open-text";
import Calendar from "lucide-react/dist/esm/icons/calendar";
import CalendarCheck2 from "lucide-react/dist/esm/icons/calendar-check-2";
import ChartLine from "lucide-react/dist/esm/icons/chart-line";
import ChartPie from "lucide-react/dist/esm/icons/chart-pie";
import Check from "lucide-react/dist/esm/icons/check";
import ChevronDown from "lucide-react/dist/esm/icons/chevron-down";
import ChevronLeft from "lucide-react/dist/esm/icons/chevron-left";
import ChevronRight from "lucide-react/dist/esm/icons/chevron-right";
import ChevronUp from "lucide-react/dist/esm/icons/chevron-up";
import CircleCheck from "lucide-react/dist/esm/icons/circle-check";
import CircleDollarSign from "lucide-react/dist/esm/icons/circle-dollar-sign";
import CircleX from "lucide-react/dist/esm/icons/circle-x";
import Clock3 from "lucide-react/dist/esm/icons/clock-3";
import Cloud from "lucide-react/dist/esm/icons/cloud";
import CloudDownload from "lucide-react/dist/esm/icons/cloud-download";
import Coins from "lucide-react/dist/esm/icons/coins";
import ContactRound from "lucide-react/dist/esm/icons/contact-round";
import Copy from "lucide-react/dist/esm/icons/copy";
import CreditCard from "lucide-react/dist/esm/icons/credit-card";
import Download from "lucide-react/dist/esm/icons/download";
import FileSearch from "lucide-react/dist/esm/icons/file-search";
import Flame from "lucide-react/dist/esm/icons/flame";
import FolderOpen from "lucide-react/dist/esm/icons/folder-open";
import Goal from "lucide-react/dist/esm/icons/goal";
import Hourglass from "lucide-react/dist/esm/icons/hourglass";
import IdCard from "lucide-react/dist/esm/icons/id-card";
import KeyRound from "lucide-react/dist/esm/icons/key-round";
import Link from "lucide-react/dist/esm/icons/link";
import ListTodo from "lucide-react/dist/esm/icons/list-todo";
import Lock from "lucide-react/dist/esm/icons/lock";
import LogOut from "lucide-react/dist/esm/icons/log-out";
import Mail from "lucide-react/dist/esm/icons/mail";
import Menu from "lucide-react/dist/esm/icons/menu";
import Moon from "lucide-react/dist/esm/icons/moon";
import Pencil from "lucide-react/dist/esm/icons/pencil";
import Phone from "lucide-react/dist/esm/icons/phone";
import Plus from "lucide-react/dist/esm/icons/plus";
import Receipt from "lucide-react/dist/esm/icons/receipt";
import RefreshCcw from "lucide-react/dist/esm/icons/refresh-ccw";
import RefreshCw from "lucide-react/dist/esm/icons/refresh-cw";
import Save from "lucide-react/dist/esm/icons/save";
import Search from "lucide-react/dist/esm/icons/search";
import Send from "lucide-react/dist/esm/icons/send";
import Settings2 from "lucide-react/dist/esm/icons/settings-2";
import Shield from "lucide-react/dist/esm/icons/shield";
import Sun from "lucide-react/dist/esm/icons/sun";
import Tags from "lucide-react/dist/esm/icons/tags";
import Trash2 from "lucide-react/dist/esm/icons/trash-2";
import TriangleAlert from "lucide-react/dist/esm/icons/triangle-alert";
import Unplug from "lucide-react/dist/esm/icons/unplug";
import UserRound from "lucide-react/dist/esm/icons/user-round";
import Users from "lucide-react/dist/esm/icons/users";
import UsersRound from "lucide-react/dist/esm/icons/users-round";
import Wallet from "lucide-react/dist/esm/icons/wallet";
import WalletCards from "lucide-react/dist/esm/icons/wallet-cards";
import Wrench from "lucide-react/dist/esm/icons/wrench";
import X from "lucide-react/dist/esm/icons/x";

type MelodyIconProps = LucideProps & {
  spin?: boolean;
};

function createIcon(Component: LucideIcon) {
  return function MelodyIcon({ className, spin = false, size = "1em", strokeWidth = 2, ...props }: MelodyIconProps) {
    return (
      <Component
        {...props}
        size={size}
        strokeWidth={strokeWidth}
        className={[className, spin ? "melody-icon-spin" : null].filter(Boolean).join(" ")}
      />
    );
  };
}

export const CalendarOutlined = createIcon(Calendar);
export const BellOutlined = createIcon(Bell);
export const CalendarCheckOutlined = createIcon(CalendarCheck2);
export const CheckCircleOutlined = createIcon(CircleCheck);
export const CheckOutlined = createIcon(Check);
export const BookOutlined = createIcon(BookOpenText);
export const CoinsOutlined = createIcon(Coins);
export const ClockCircleOutlined = createIcon(Clock3);
export const CloseCircleOutlined = createIcon(CircleX);
export const CloseOutlined = createIcon(X);
export const CloudDownloadOutlined = createIcon(CloudDownload);
export const CloudOutlined = createIcon(Cloud);
export const CloudSyncOutlined = createIcon(RefreshCw);
export const CopyOutlined = createIcon(Copy);
export const CreditCardOutlined = createIcon(CreditCard);
export const DashboardOutlined = createIcon(Blocks);
export const DeleteOutlined = createIcon(Trash2);
export const DisconnectOutlined = createIcon(Unplug);
export const DollarOutlined = createIcon(CircleDollarSign);
export const DownloadOutlined = createIcon(Download);
export const EditOutlined = createIcon(Pencil);
export const FileSearchOutlined = createIcon(FileSearch);
export const FireOutlined = createIcon(Flame);
export const FolderOpenOutlined = createIcon(FolderOpen);
export const FunnelTargetOutlined = createIcon(Goal);
export const HourglassOutlined = createIcon(Hourglass);
export const InfoCircleOutlined = createIcon(BadgeInfo);
export const KeyOutlined = createIcon(KeyRound);
export const LeftOutlined = createIcon(ChevronLeft);
export const DownOutlined = createIcon(ChevronDown);
export const LineChartOutlined = createIcon(ChartLine);
export const LinkOutlined = createIcon(Link);
export const ListTodoOutlined = createIcon(ListTodo);
export const LockOutlined = createIcon(Lock);
export const LogoutOutlined = createIcon(LogOut);
export const MailOutlined = createIcon(Mail);
export const MenuOutlined = createIcon(Menu);
export const MoneyWaveOutlined = createIcon(WalletCards);
export const MoonOutlined = createIcon(Moon);
export const PhoneOutlined = createIcon(Phone);
export const PieChartOutlined = createIcon(ChartPie);
export const PlusOutlined = createIcon(Plus);
export const ProfileOutlined = createIcon(ContactRound);
export const ReceiptOutlined = createIcon(Receipt);
export const ReloadOutlined = createIcon(RefreshCcw);
export const RightOutlined = createIcon(ChevronRight);
export const SaveOutlined = createIcon(Save);
export const UpOutlined = createIcon(ChevronUp);
export const SafetyCertificateOutlined = createIcon(Shield);
export const SearchOutlined = createIcon(Search);
export const SendOutlined = createIcon(Send);
export const SettingOutlined = createIcon(Settings2);
export const SunOutlined = createIcon(Sun);
export const SyncOutlined = createIcon(RefreshCw);
export const TagsOutlined = createIcon(Tags);
export const TeamOutlined = createIcon(Users);
export const TeamStatsOutlined = createIcon(UsersRound);
export const ToolOutlined = createIcon(Wrench);
export const UserBadgeOutlined = createIcon(IdCard);
export const UserOutlined = createIcon(UserRound);
export const WarningOutlined = createIcon(TriangleAlert);
export const WalletOutlined = createIcon(Wallet);
export const WalletStatsOutlined = createIcon(BadgeRussianRuble);
