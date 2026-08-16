import { readFile } from "node:fs/promises";

const [indexHtml, nginxConfig, securityHeaders] = await Promise.all([
  readFile(new URL("../index.html", import.meta.url), "utf8"),
  readFile(new URL("../nginx/nginx.conf", import.meta.url), "utf8"),
  readFile(new URL("../nginx/security-headers.inc", import.meta.url), "utf8"),
]);

const problems = [];
const requiredHeaders = ["Content-Security-Policy", "X-Content-Type-Options", "X-Frame-Options", "Referrer-Policy", "Permissions-Policy"];

for (const header of requiredHeaders) {
  if (!securityHeaders.includes(`add_header ${header} `)) {
    problems.push(`nginx/security-headers.inc does not set ${header}.`);
  }
}

if (!securityHeaders.includes("script-src 'self'")) {
  problems.push("The CSP does not restrict scripts to the application origin.");
}

if (securityHeaders.includes("script-src 'self' 'unsafe-inline'") || securityHeaders.includes("'unsafe-eval'")) {
  problems.push("The CSP permits inline or evaluated JavaScript.");
}

if (/<script(?![^>]*\bsrc=)(?![^>]*\btype=["']module["'])[^>]*>/i.test(indexHtml) || /<style(?:\s|>)/i.test(indexHtml)) {
  problems.push("index.html contains inline script or style blocks that prevent a strict script policy.");
}

const locationsWithOwnHeaders = [...nginxConfig.matchAll(/location[^{]*\{[\s\S]*?^\s*}/gm)]
  .map((match) => match[0])
  .filter((location) => location.includes("add_header"));

for (const location of locationsWithOwnHeaders) {
  if (!location.includes("include /etc/nginx/security-headers.inc;")) {
    problems.push("An nginx location overrides inherited headers without restoring the security-header include.");
  }
}

if (problems.length > 0) {
  console.error(["Security baseline check failed:", ...problems.map((problem) => `- ${problem}`)].join("\n"));
  process.exitCode = 1;
} else {
  console.log("Security baseline: strict script CSP and required browser headers are configured.");
}
