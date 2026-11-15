export type IconName =
  | "home"
  | "book"
  | "users"
  | "sun"
  | "moon"
  | "arrow"
  | "logout"
  | "check"
  | "edit"
  | "archive"
  | "restore"
  | "history"
  | "plus"
  | "trash";

const paths: Record<IconName, string> = {
  users:
    "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2m20 0v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
  home: "m3 10 9-7 9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z",
  book: "M12 5v16M3 4h5a4 4 0 0 1 4 2 4 4 0 0 1 4-2h5v15h-5a4 4 0 0 0-4 2 4 4 0 0 0-4-2H3Z",
  sun: "M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1.5 1.5m11 11L19 19M5 19l1.5-1.5m11-11L19 5M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
  moon: "M20.9 13A9 9 0 0 1 11 3.1 9 9 0 1 0 20.9 13Z",
  arrow: "M5 12h14m-6-6 6 6-6 6",
  logout: "M10 4H4v16h6m5-13 5 5-5 5m-7-5h12",
  check: "m5 12 4 4L19 6",
  edit: "m4 16.5-.8 4.3 4.3-.8L20 7.5 16.5 4Zm10.8-10.8 3.5 3.5",
  archive: "M3 4h18v4H3Zm2 4v12h14V8m-10 4h6",
  restore: "M4 14v6h16v-6M12 16V3m-5 5 5-5 5 5",
  history: "M3 11a9 9 0 1 1 2 7M3 4v7h7m2-4v5l-3 2",
  plus: "M12 5v14M5 12h14",
  trash: "M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7",
};

export function Icon({ name }: { name: IconName }) {
  return (
    <svg
      className="icon"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name]} />
    </svg>
  );
}
