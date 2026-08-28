import { http, type RecordActivity, type RequiredApiContract } from "@/shared/api";
import type {
  AuthenticateClientPortalLinkRequest,
  AuthenticateSavedClientPortalIdentityRequest,
  ChangePasswordRequest,
  ClientPortalAuthenticationResponse as GeneratedClientPortalAuthenticationResponse,
  CreateInviteRequest,
  CreateInviteResponse as GeneratedCreateInviteResponse,
  CreatePasswordResetLinkResponse as GeneratedCreatePasswordResetLinkResponse,
  GetClientPortalLinkStatusResponse,
  GetInviteCodeInformationResponse,
  GetSavedClientPortalIdentityStatusResponse,
  GetSessionsResponse,
  LoginAttemptResponse,
  LoginRequest,
  LoginResponse as GeneratedLoginResponse,
  MeResponse as GeneratedMeResponse,
  Recover2FaRequest,
  Recover2FaResponse as GeneratedRecover2FaResponse,
  RecoveryCodeDto,
  RecoveryCodesResponse as GeneratedRecoveryCodesResponse,
  RegisterRequest,
  RegisterResponse as GeneratedRegisterResponse,
  ResetPasswordRequest,
  SavedClientPortalIdentityResponse,
  SessionDto as GeneratedSessionDto,
  Setup2FaRequest,
  Setup2FaResponse as GeneratedSetup2FaResponse,
  Verify2FaRequest,
} from "@/shared/api/generated/models";

export type InviteInfo = RequiredApiContract<GetInviteCodeInformationResponse, never>;
export type RegisterInput = RequiredApiContract<RegisterRequest, "inviteCode" | "email" | "password" | "firstName" | "lastName">;
export type RegisterResponse = RequiredApiContract<GeneratedRegisterResponse, "totpRequired">;
export type Verify2FaInput = RequiredApiContract<Verify2FaRequest, "email" | "otp" | "otpSecret">;
export type Recover2FaInput = RequiredApiContract<Recover2FaRequest, "email" | "recoveryCode">;
export type RecoveryCodeItem = RequiredApiContract<RecoveryCodeDto, "code" | "wasUsed">;
export type RecoveryCodesResponse = Omit<RequiredApiContract<GeneratedRecoveryCodesResponse, "allCodes">, "allCodes"> & {
  allCodes: RecoveryCodeItem[];
};
export type Recover2FaResponse = Omit<
  RequiredApiContract<GeneratedRecover2FaResponse, "accessToken" | "secret" | "otpUrl" | "allCodes">,
  "allCodes"
> & { allCodes: RecoveryCodeItem[] };
export type LoginResponse = RequiredApiContract<GeneratedLoginResponse, "accessToken" | "firstName" | "lastName">;
export type ClientPortalLinkStatusResponse = RequiredApiContract<GetClientPortalLinkStatusResponse, "firstName" | "hasPin">;
export type ClientPortalPinAuthInput = RequiredApiContract<AuthenticateClientPortalLinkRequest, "token" | "pin">;
type SavedClientPortalPinAuthInput = RequiredApiContract<AuthenticateSavedClientPortalIdentityRequest, "reference" | "pin">;
export type SavedClientIdentityDto = RequiredApiContract<
  SavedClientPortalIdentityResponse,
  "identityId" | "reference" | "displayLabel" | "lastUsedAtUtc"
>;
export type ClientPortalAuthenticationResponse = Omit<
  RequiredApiContract<GeneratedClientPortalAuthenticationResponse, "accessToken" | "firstName" | "lastName" | "savedIdentity">,
  "savedIdentity"
> & { savedIdentity: SavedClientIdentityDto };
export type SavedClientPortalStatusResponse = RequiredApiContract<GetSavedClientPortalIdentityStatusResponse, "displayLabel">;
export type LoginChallengeResponse = RequiredApiContract<LoginAttemptResponse, "requiresTwoFactor" | "canUseOtp" | "canUseRecoveryCode">;

export type LoginAttemptResult = ({ kind: "success" } & LoginResponse) | ({ kind: "challenge" } & LoginChallengeResponse);

export type LoginInput = RequiredApiContract<LoginRequest, "email" | "password">;
export type ResetPasswordInput = RequiredApiContract<ResetPasswordRequest, "token" | "newPassword">;
export type Setup2FaInput = RequiredApiContract<Setup2FaRequest, "password">;
export type Setup2FaResponse = RequiredApiContract<GeneratedSetup2FaResponse, "secret" | "otpUrl">;
export type MeResponse = Omit<
  RequiredApiContract<
    GeneratedMeResponse,
    | "id"
    | "email"
    | "firstName"
    | "lastName"
    | "roleDisplayName"
    | "isAdmin"
    | "isSuperuser"
    | "isClientPortal"
    | "isTwoFactorEnabled"
    | "isTwoFactorRequired"
  >,
  "lastActivity"
> & {
  lastActivity?: RecordActivity | null;
};
export type ChangePasswordInput = RequiredApiContract<ChangePasswordRequest, "currentPassword" | "newPassword">;
export type SessionDto = RequiredApiContract<GeneratedSessionDto, "id" | "deviceInfo" | "isCurrent" | "createdAtUtc">;
export type SessionsResponse = Omit<RequiredApiContract<GetSessionsResponse, "data">, "data"> & { data: SessionDto[] };
export type CreateInviteInput = RequiredApiContract<CreateInviteRequest, "role">;
export type CreateInviteResponse = RequiredApiContract<GeneratedCreateInviteResponse, "url">;
export type CreatePasswordResetLinkResponse = RequiredApiContract<GeneratedCreatePasswordResetLinkResponse, "url">;

export const authApi = {
  getInviteInfo(inviteCode: string) {
    return http.get<InviteInfo>("/auth/invites", { params: { inviteCode } }).then((response) => response.data);
  },
  createInvite(input: CreateInviteInput) {
    return http.post<CreateInviteResponse>("/auth/invites", input).then((response) => response.data);
  },
  createPasswordResetLink(userId: string) {
    return http.post<CreatePasswordResetLinkResponse>(`/users/${userId}/password-reset-links`, {}).then((response) => response.data);
  },
  register(input: RegisterInput) {
    return http.post<RegisterResponse>("/auth/register", input).then((response) => response.data);
  },
  login(input: LoginInput) {
    return http
      .post<LoginResponse | LoginChallengeResponse>("/auth/login", input, {
        validateStatus: (status) => status === 200 || status === 202,
      })
      .then((response): LoginAttemptResult => {
        if (response.status === 202) {
          return {
            kind: "challenge",
            ...(response.data as LoginChallengeResponse),
          };
        }

        return {
          kind: "success",
          ...(response.data as LoginResponse),
        };
      });
  },
  getClientPortalLinkStatus(token: string) {
    return http.get<ClientPortalLinkStatusResponse>("/client-portal/auth/link", { params: { token } }).then((response) => response.data);
  },
  authenticateClientPortalLink(input: ClientPortalPinAuthInput) {
    return http.post<ClientPortalAuthenticationResponse>("/client-portal/auth/link", input).then((response) => response.data);
  },
  getSavedClientPortalStatus(reference: string) {
    return http
      .get<SavedClientPortalStatusResponse>("/client-portal/auth/saved", { params: { reference } })
      .then((response) => response.data);
  },
  authenticateSavedClientPortalIdentity(input: SavedClientPortalPinAuthInput) {
    return http.post<ClientPortalAuthenticationResponse>("/client-portal/auth/saved", input).then((response) => response.data);
  },
  verify2Fa(input: Verify2FaInput) {
    return http.post<RecoveryCodesResponse>("/auth/2fa/verify", input).then((response) => response.data);
  },
  recover2Fa(input: Recover2FaInput) {
    return http.post<Recover2FaResponse>("/auth/2fa/recover", input).then((response) => response.data);
  },
  resetPassword(input: ResetPasswordInput) {
    return http.post<unknown>("/auth/password-reset", input).then(() => undefined);
  },
  setup2Fa(input: Setup2FaInput) {
    return http.post<Setup2FaResponse>("/auth/2fa/setup", input).then((response) => response.data);
  },
  getRecoveryCodes() {
    return http.get<RecoveryCodesResponse>("/auth/recovery-codes").then((response) => response.data);
  },
  regenerateRecoveryCodes() {
    return http.post<RecoveryCodesResponse>("/auth/recovery-codes").then((response) => response.data);
  },
  remove2Fa() {
    return http.delete<unknown>("/auth/2fa").then(() => undefined);
  },
  getMe() {
    return http.get<MeResponse>("/auth/me").then((response) => response.data);
  },
  changePassword(input: ChangePasswordInput) {
    return http.post<unknown>("/auth/password-change", input).then(() => undefined);
  },
  getSessions() {
    return http.get<SessionsResponse>("/auth/sessions").then((response) => response.data);
  },
  revokeSession(id: string) {
    return http.delete<unknown>(`/auth/sessions/${id}`).then(() => undefined);
  },
  logoutAll() {
    return http.post<unknown>("/auth/logout-all").then(() => undefined);
  },
};
