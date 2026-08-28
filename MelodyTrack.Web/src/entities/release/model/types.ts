import type { ApiJsonContract, RequiredApiContract } from "@/shared/api";
import type {
  ReleaseChanges as GeneratedReleaseChanges,
  ReleaseResponse as GeneratedReleaseResponse,
  ReleasesResponse as GeneratedReleasesResponse,
} from "@/shared/api/generated/models";

type GeneratedChanges = RequiredApiContract<GeneratedReleaseChanges, "newEscaped" | "improved" | "fixed" | "security">;

export type ReleaseChanges = Omit<GeneratedChanges, "newEscaped"> & {
  new: GeneratedChanges["newEscaped"];
};

type GeneratedEntry = RequiredApiContract<GeneratedReleaseResponse, "version" | "codename" | "date" | "changes">;

export type ReleaseEntry = Omit<GeneratedEntry, "changes" | "parentVersion"> & {
  changes: ReleaseChanges;
  parentVersion: Exclude<ApiJsonContract<GeneratedReleaseResponse>["parentVersion"], undefined>;
};

export type ReleaseHistory = Omit<
  RequiredApiContract<
    GeneratedReleasesResponse,
    "currentVersion" | "releases" | "page" | "pageSize" | "totalCount" | "totalPages" | "hasNextPage"
  >,
  "releases"
> & {
  releases: ReleaseEntry[];
};
