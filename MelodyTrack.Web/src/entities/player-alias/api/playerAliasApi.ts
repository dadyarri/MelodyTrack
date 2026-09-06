import { http, type RequiredApiContract, type Ulid } from "@/shared/api";
import type {
  AliasDictionaryEntryDto,
  ChoosePortalAliasRequest,
  GetAliasDictionaryResponse,
  PortalAliasCandidateResponse,
  UpdateAliasDictionaryEntryRequest,
} from "@/shared/api/generated/models";

export type AliasDictionaryKind = "epithets" | "creatures" | "bardicTerms";
export type AliasDictionaryEntry = RequiredApiContract<AliasDictionaryEntryDto, "id" | "value" | "isEnabled">;
export type AliasCandidate = RequiredApiContract<
  PortalAliasCandidateResponse,
  "epithetId" | "nounKind" | "nounId" | "value" | "candidateToken"
>;
export type ChooseAliasInput = RequiredApiContract<
  ChoosePortalAliasRequest,
  "epithetId" | "nounKind" | "nounId" | "value" | "candidateToken"
>;
export type UpdateAliasEntryInput = RequiredApiContract<UpdateAliasDictionaryEntryRequest, "value" | "isEnabled">;

export const playerAliasApi = {
  list(dictionary: AliasDictionaryKind, search?: string) {
    return http
      .get<GetAliasDictionaryResponse>(`/alias-dictionaries/${dictionary}`, { params: search ? { search } : undefined })
      .then((response) => (response.data.items ?? []) as AliasDictionaryEntry[]);
  },
  create(dictionary: AliasDictionaryKind, value: string) {
    return http.post<AliasDictionaryEntry>(`/alias-dictionaries/${dictionary}`, { value }).then((response) => response.data);
  },
  update(dictionary: AliasDictionaryKind, id: Ulid, input: UpdateAliasEntryInput) {
    return http.patch(`/alias-dictionaries/${dictionary}/${id}`, input).then(() => undefined);
  },
  remove(dictionary: AliasDictionaryKind, id: Ulid) {
    return http.delete(`/alias-dictionaries/${dictionary}/${id}`).then(() => undefined);
  },
  getCandidate(previous?: AliasCandidate) {
    return http
      .get<AliasCandidate>("/client-portal/alias-candidates/next", {
        params: previous
          ? {
              previousEpithetId: previous.epithetId,
              previousNounKind: previous.nounKind,
              previousNounId: previous.nounId,
            }
          : undefined,
      })
      .then((response) => response.data);
  },
  choose(candidate: AliasCandidate) {
    return http.post("/client-portal/alias", candidate satisfies ChooseAliasInput).then(() => undefined);
  },
};
