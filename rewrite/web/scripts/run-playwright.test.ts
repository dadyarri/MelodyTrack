import { describe, expect, it } from "vitest";
import { containerOptions } from "./run-playwright";

describe("Docker browser launcher", () => {
  it("keeps direct npm artifacts on the host and forwards project arguments intact", () => {
    const result = containerOptions(["--project=webkit*", "a title with spaces"], {}, "/repo/web");

    expect(result.artifactDir).toBe("/repo/web/.artifacts/browser");
    expect(result.options).toEqual([
      "--volume",
      "/repo/web/.artifacts/browser:/workspace/web/.artifacts/browser",
    ]);
    expect(result.forwarded).toEqual(["--project=webkit*", "a title with spaces"]);
  });

  it("preserves runner report paths without forwarding unrelated environment", () => {
    const result = containerOptions(
      [],
      {
        AGENT_CHECKS_BROWSER_DIR: "/repo/.artifacts/run/browser",
        AGENT_CHECKS_BROWSER_PORT: "5189",
        CI: "true",
        SECRET: "private",
      },
      "/repo/web",
    );

    expect(result.options).toEqual([
      "--volume",
      "/repo/.artifacts/run/browser:/repo/.artifacts/run/browser",
      "--env",
      "AGENT_CHECKS_BROWSER_DIR=/repo/.artifacts/run/browser",
      "--env",
      "AGENT_CHECKS_BROWSER_PORT=5189",
      "--env",
      "CI=true",
    ]);
  });

  it.each([["--test-list", "../failed tests.txt"], ["--test-list=../failed tests.txt"]])(
    "mounts rerun selection files read-only for %j",
    (...args) => {
      const result = containerOptions(args, {}, "/repo/web");

      expect(result.options.at(-1)).toMatch(
        /^\/repo\/failed tests.txt:\/tmp\/playwright-test-list-\d.txt:ro$/,
      );
      expect(result.forwarded.join(" ")).toMatch(
        /--test-list[ =]\/tmp\/playwright-test-list-\d.txt$/,
      );
    },
  );
});
