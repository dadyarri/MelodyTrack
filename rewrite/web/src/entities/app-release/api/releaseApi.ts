import { apiOptions, applicationApi } from "@/shared/api";
import { readCurrentRelease, readReleaseHistory } from "../model/release";

export async function getCurrentRelease(signal: AbortSignal) {
  return readCurrentRelease(await applicationApi().releases.current.get(apiOptions(signal)));
}

export async function getReleaseHistory(page: number, signal: AbortSignal) {
  return readReleaseHistory(await applicationApi().releases.get({
    ...apiOptions(signal),
    queryParameters: { page, pageSize: 2 },
  }));
}
