import { apiOptions, type applicationApi } from "@/shared/api";
import {
  type DirectoryQuery,
  readDirectoryOptions,
  readDirectoryPage,
} from "../model/clientDirectory";

type Api = ReturnType<typeof applicationApi>;
export async function getClientDirectory(api: Api, signal: AbortSignal, query: DirectoryQuery) {
  return readDirectoryPage(
    await api.clients.get({
      ...apiOptions(signal),
      queryParameters: {
        q: query.q.trim() || undefined,
        archive: query.archive,
        sourceId: query.sourceId || undefined,
        withoutSource: query.withoutSource,
        sort: query.sort,
        page: query.page,
        pageSize: query.pageSize,
      },
    }),
  );
}
export async function getClientDirectoryOptions(api: Api, signal: AbortSignal) {
  return readDirectoryOptions(await api.clients.optionsPath.get(apiOptions(signal)));
}
