/* eslint-disable no-console */
"use strict";

const fs = require("fs");
const path = require("path");

const target = path.join(
  __dirname,
  "..",
  "node_modules",
  "react-dev-utils",
  "checkRequiredFiles.js"
);

function run() {
  if (!fs.existsSync(target)) {
    console.log("[postinstall] react-dev-utils not found, skipping patch.");
    return;
  }

  const original = fs.readFileSync(target, "utf8");
  const from = "fs.accessSync(filePath, fs.F_OK);";
  const to = "fs.accessSync(filePath, fs.constants.F_OK);";
  if (!original.includes(from)) {
    console.log("[postinstall] react-dev-utils patch not needed.");
    return;
  }

  const updated = original.replace(from, to);
  fs.writeFileSync(target, updated, "utf8");
  console.log("[postinstall] patched react-dev-utils checkRequiredFiles.js");
}

run();
