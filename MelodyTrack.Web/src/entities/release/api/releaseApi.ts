import * as v from "valibot";

import { http } from "@/shared/api";

import type { ReleaseHistory } from "../model/types";

const releaseHistoryPageSize = 2;

const changesSchema = v.strictObject({
  new: v.array(v.string()),
  improved: v.array(v.string()),
  fixed: v.array(v.string()),
  security: v.array(v.string()),
});
const releaseHistorySchema = v.strictObject({
  currentVersion: v.string(),
  releases: v.array(
    v.strictObject({
      version: v.string(),
      codename: v.string(),
      date: v.string(),
      changes: changesSchema,
      parentVersion: v.nullable(v.string()),
    }),
  ),
  page: v.number(),
  pageSize: v.number(),
  totalCount: v.number(),
  totalPages: v.number(),
  hasNextPage: v.boolean(),
});

export const releaseQueryKeys = {
  all: ["releases"] as const,
  history: (page = 1) => [...releaseQueryKeys.all, "history", page] as const,
};

export async function getReleaseHistory(page = 1) {
  const response = await http.get<ReleaseHistory>("/releases", {
    params: { page, page_size: releaseHistoryPageSize },
  });
  return v.parse(releaseHistorySchema, response.data);
}
