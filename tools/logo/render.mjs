// Draws SVG files to PNG with resvg. Input (stdin): {"jobs":[{"svg","out","width"}],"fonts":[paths],"default":"family"}
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";
import { Resvg } from "@resvg/resvg-js";

const input = JSON.parse(readFileSync(0, "utf8"));
for (const job of input.jobs) {
  const svg = readFileSync(job.svg, "utf8");
  const resvg = new Resvg(svg, {
    fitTo: { mode: "width", value: job.width },
    font: { fontFiles: input.fonts, loadSystemFonts: false, defaultFontFamily: input.default },
  });
  mkdirSync(dirname(job.out), { recursive: true });
  writeFileSync(job.out, resvg.render().asPng());
}
console.log(`rendered ${input.jobs.length} PNG files`);
