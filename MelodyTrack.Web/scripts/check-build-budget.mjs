import { brotliCompressSync, constants, gzipSync } from "node:zlib";
import { readFileSync, readdirSync } from "node:fs";
import { extname, join } from "node:path";

const distDirectory = new URL("../dist/", import.meta.url);
const indexHtml = readFileSync(new URL("index.html", distDirectory), "utf8");
const htmlInitialAssetPaths = [...indexHtml.matchAll(/(?:href|src)="\/([^"]+\.(?:css|js))"/g)].map((match) => match[1]);
const bootstrapDependencyPaths = htmlInitialAssetPaths
  .filter((assetPath) => extname(assetPath) === ".js")
  .flatMap((assetPath) => {
    const contents = readFileSync(new URL(assetPath, distDirectory), "utf8");
    return [...contents.matchAll(/["'`](assets\/[^"'`]+\.(?:css|js))["'`]/g)].map((match) => match[1]);
  });
const uniqueInitialAssetPaths = [...new Set([...htmlInitialAssetPaths, ...bootstrapDependencyPaths])];

const budget = {
  initialJavaScriptRaw: 1_300 * 1024,
  initialJavaScriptGzip: 420 * 1024,
  initialJavaScriptBrotli: 390 * 1024,
  initialCssRaw: 90 * 1024,
  initialCssGzip: 24 * 1024,
  initialCssBrotli: 20 * 1024,
  largestJavaScriptChunk: 500 * 1024,
};

const compress = (contents) => ({
  raw: contents.byteLength,
  gzip: gzipSync(contents, { level: 6 }).byteLength,
  brotli: brotliCompressSync(contents, {
    params: { [constants.BROTLI_PARAM_QUALITY]: 6 },
  }).byteLength,
});

const sumInitial = (extension) =>
  uniqueInitialAssetPaths
    .filter((assetPath) => extname(assetPath) === extension)
    .map((assetPath) => compress(readFileSync(new URL(assetPath, distDirectory))))
    .reduce(
      (total, size) => ({
        raw: total.raw + size.raw,
        gzip: total.gzip + size.gzip,
        brotli: total.brotli + size.brotli,
      }),
      { raw: 0, gzip: 0, brotli: 0 },
    );

const javascript = sumInitial(".js");
const css = sumInitial(".css");
const emittedJavaScript = readdirSync(new URL("assets/", distDirectory))
  .filter((filename) => filename.endsWith(".js"))
  .map((filename) => ({
    filename,
    bytes: readFileSync(join(new URL("assets/", distDirectory).pathname, filename)).byteLength,
  }));
const largestChunk = emittedJavaScript.reduce((largest, chunk) => (chunk.bytes > largest.bytes ? chunk : largest));

const checks = [
  ["initial JavaScript raw", javascript.raw, budget.initialJavaScriptRaw],
  ["initial JavaScript gzip", javascript.gzip, budget.initialJavaScriptGzip],
  ["initial JavaScript Brotli", javascript.brotli, budget.initialJavaScriptBrotli],
  ["initial CSS raw", css.raw, budget.initialCssRaw],
  ["initial CSS gzip", css.gzip, budget.initialCssGzip],
  ["initial CSS Brotli", css.brotli, budget.initialCssBrotli],
  [`largest JavaScript chunk (${largestChunk.filename})`, largestChunk.bytes, budget.largestJavaScriptChunk],
];

const formatKiB = (bytes) => `${(bytes / 1024).toFixed(1)} KiB`;
for (const [label, actual, maximum] of checks) {
  console.log(`${label}: ${formatKiB(actual)} / ${formatKiB(maximum)}`);
}

const failures = checks.filter(([, actual, maximum]) => actual > maximum);
if (failures.length > 0) {
  throw new Error(`Build budget exceeded: ${failures.map(([label]) => label).join(", ")}`);
}
