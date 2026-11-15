import {
  IconArchive,
  IconArchiveRestore,
  IconArrowRight,
  IconBook2,
  IconCalendar,
  IconCheck,
  IconChevronLeft,
  IconChevronRight,
  IconHistory,
  IconHome,
  IconLogout,
  IconMail,
  IconMoon,
  IconPencil,
  IconPhone,
  IconPlus,
  IconSun,
  IconTrash,
  IconUser,
  IconUsers,
  IconX,
} from "@tabler/icons-react";

const icons = {
  home: IconHome,
  book: IconBook2,
  users: IconUsers,
  sun: IconSun,
  moon: IconMoon,
  arrow: IconArrowRight,
  logout: IconLogout,
  check: IconCheck,
  edit: IconPencil,
  archive: IconArchive,
  restore: IconArchiveRestore,
  history: IconHistory,
  plus: IconPlus,
  trash: IconTrash,
  profile: IconUser,
  phone: IconPhone,
  email: IconMail,
  calendar: IconCalendar,
  close: IconX,
  previous: IconChevronLeft,
  next: IconChevronRight,
};

export type IconName = keyof typeof icons;

export function Icon({ name, size }: { name: IconName; size?: number }) {
  const Component = icons[name];
  return (
    <Component
      className="icon"
      size={size ?? 20}
      style={size === undefined ? undefined : { width: size, height: size }}
      stroke={1.7}
      aria-hidden="true"
    />
  );
}
