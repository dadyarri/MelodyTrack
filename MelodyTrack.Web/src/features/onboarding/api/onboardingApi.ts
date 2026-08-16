import { http } from "@/shared/api";

export interface OnboardingStateResponse {
  status: "active" | "completed" | "skipped";
  currentStep: string;
  currentPath: string;
  definitionVersion: number;
  shouldLaunch: boolean;
  updatedAtUtc: string;
  completedAtUtc?: string | null;
}

export const onboardingApi = {
  getState() {
    return http.get<OnboardingStateResponse>("/onboarding").then((response) => response.data);
  },
  updateProgress(input: { currentStep: string; currentPath: string }) {
    return http.patch<OnboardingStateResponse>("/onboarding", input).then((response) => response.data);
  },
  complete() {
    return http.post<OnboardingStateResponse>("/onboarding/completion").then((response) => response.data);
  },
  skip() {
    return http.post<OnboardingStateResponse>("/onboarding/skip").then((response) => response.data);
  },
  reset() {
    return http.delete<OnboardingStateResponse>("/onboarding").then((response) => response.data);
  },
};
