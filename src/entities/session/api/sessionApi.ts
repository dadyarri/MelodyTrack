import { http, type RecordActivity } from "@/shared/api";

export interface InviteInfo {
  email?: string | null;
}

export interface RegisterInput {
  inviteCode: string;
  email: string;
  password: string;
  firstName: string;
  lastName: string;
}

export interface RegisterResponse {
  totpRequired: boolean;
  secret?: string | null;
  otpUrl?: string | null;
}

export interface Verify2FaInput {
  email: string;
  otp: string;
  otpSecret: string;
}

export interface Recover2FaInput {
  email: string;
  recoveryCode: string;
}

export interface RecoveryCodesResponse {
  allCodes: RecoveryCodeItem[];
}

export interface RecoveryCodeItem {
  code: string;
  wasUsed: boolean;
}

export interface Recover2FaResponse {
  accessToken: string;
  secret: string;
  otpUrl: string;
  allCodes: RecoveryCodeItem[];
}

export interface LoginResponse {
  accessToken: string;
  firstName: string;
  lastName: string;
}

export interface ClientPortalLinkStatusResponse {
  firstName: string;
  hasPin: boolean;
}

export interface ClientPortalPinAuthInput {
  token: string;
  pin: string;
  pinConfirmation?: string;
}

export interface SavedClientIdentityDto {
  identityId: string;
  reference: string;
  displayLabel: string;
  lastUsedAtUtc: string;
}

export interface ClientPortalAuthenticationResponse extends LoginResponse {
  savedIdentity: SavedClientIdentityDto;
}

export interface SavedClientPortalStatusResponse {
  displayLabel: string;
}

export interface LoginChallengeResponse {
  requiresTwoFactor: boolean;
  canUseOtp: boolean;
  canUseRecoveryCode: boolean;
}

export type LoginAttemptResult = ({ kind: "success" } & LoginResponse) | ({ kind: "challenge" } & LoginChallengeResponse);

export interface LoginInput {
  email: string;
  password: string;
  otp?: string;
  recoveryCode?: string;
}

export interface ResetPasswordInput {
  token: string;
  newPassword: string;
  otp?: string;
  recoveryCode?: string;
}

export interface Setup2FaInput {
  password: string;
}

export interface Setup2FaResponse {
  secret: string;
  otpUrl: string;
}

export interface MeResponse {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roleDisplayName: string;
  phone?: string | null;
  telegram?: string | null;
  vk?: string | null;
  lastActivity?: RecordActivity | null;
  isAdmin: boolean;
  isSuperuser: boolean;
  isClientPortal: boolean;
  linkedClientId?: string | null;
  balance?: number | null;
  isTwoFactorEnabled: boolean;
  isTwoFactorRequired: boolean;
}

export interface ChangePasswordInput {
  currentPassword: string;
  newPassword: string;
}

export interface SessionDto {
  id: string;
  deviceInfo: string;
  isCurrent: boolean;
  lastSeenAtUtc: string;
}

export interface SessionsResponse {
  data: SessionDto[];
}

export interface CreateInviteInput {
  email?: string;
  role: string;
}

export interface CreateInviteResponse {
  url: string;
}

export interface CreatePasswordResetLinkResponse {
  url: string;
}

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
  authenticateSavedClientPortalIdentity(input: { reference: string; pin: string }) {
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
