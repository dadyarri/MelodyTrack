export type ReleaseChanges = {
  new: string[];
  improved: string[];
  fixed: string[];
  security: string[];
};

export type ReleaseEntry = {
  version: string;
  codename: string;
  date: string;
  changes: ReleaseChanges;
  parentVersion: string | null;
};

export type ReleaseHistory = {
  currentVersion: string;
  releases: ReleaseEntry[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
};
