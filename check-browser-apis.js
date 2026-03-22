const fs = require("fs");
const path = require("path");

// Files to ignore
const IGNORE_FILES = [
  "node_modules",
  ".next",
  "out",
  "build",
  "dist",
  "public",
  ".git",
  "check-browser-apis.js",
];

// Patterns to look for
const BROWSER_APIS = [
  "window.",
  "document.",
  "localStorage",
  "sessionStorage",
  "navigator",
  "alert(",
  "confirm(",
  "prompt(",
  "fetch(",
  "XMLHttpRequest",
  "IntersectionObserver",
  "MutationObserver",
  "ResizeObserver",
  "requestAnimationFrame",
  "customElements",
  "WebSocket",
  "indexedDB",
];

// Check if a file contains browser APIs
function checkFile(filePath) {
  try {
    const content = fs.readFileSync(filePath, "utf8");
    const lines = content.split("\n");
    const issues = [];

    lines.forEach((line, index) => {
      BROWSER_APIS.forEach((api) => {
        if (line.includes(api) && !line.includes("//") && !line.includes("*")) {
          // Skip if it's inside a comment or string
          if (!isInCommentOrString(content, line, index)) {
            issues.push({
              line: index + 1,
              column: line.indexOf(api) + 1,
              api,
              code: line.trim(),
            });
          }
        }
      });
    });

    return issues;
  } catch {
    return [];
  }
}

// Check if the API usage is inside a comment or string
function isInCommentOrString(content, line, lineIndex) {
  const lines = content.split("\n");
  const lineContent = lines[lineIndex];

  // Check for single-line comments
  if (lineContent.includes("//")) {
    const commentIndex = lineContent.indexOf("//");
    const apiIndex = lineContent.indexOf("window.");
    if (apiIndex > commentIndex) return true;
  }

  // Check for multi-line comments
  if (content.includes("/*") && content.includes("*/")) {
    const beforeLine = lines.slice(0, lineIndex).join("\n");
    const openComments = (beforeLine.match(/\/\*/g) || []).length;
    const closeComments = (beforeLine.match(/\*\//g) || []).length;

    if (openComments > closeComments) {
      return true;
    }
  }

  return false;
}

// Recursively find files in a directory
function findFiles(dir, fileList = []) {
  const files = fs.readdirSync(dir);

  files.forEach((file) => {
    const filePath = path.join(dir, file);
    const stat = fs.statSync(filePath);

    if (IGNORE_FILES.includes(file)) {
      return;
    }

    if (stat.isDirectory()) {
      findFiles(filePath, fileList);
    } else if (
      file.endsWith(".tsx") ||
      file.endsWith(".ts") ||
      file.endsWith(".js") ||
      file.endsWith(".jsx")
    ) {
      fileList.push(filePath);
    }
  });

  return fileList;
}

// Main function
function main() {
  const rootDir = process.cwd();
  const files = findFiles(rootDir);
  let hasIssues = false;

  files.forEach((file) => {
    const issues = checkFile(file);

    if (issues.length > 0) {
      hasIssues = true;
    }
  });
}

main();
