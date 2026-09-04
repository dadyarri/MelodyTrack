import { http, type RequiredApiContract } from "@/shared/api";
import type { OnboardingStateResponse as GeneratedOnboardingStateResponse } from "@/shared/api/generated/models";

export type OnboardingStateResponse = RequiredApiContract<
  GeneratedOnboardingStateResponse,
  "status" | "currentStep" | "currentPath" | "definitionVersion" | "shouldLaunch" | "updatedAtUtc"
>;

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
