// Checks that every import goes the way ADR 0021 allows, so several looks of the app stay apart and the features stay
// free of any one look.
//
// The layers, and what each may import:
//
//   util      src/time/           itself only; every layer may use it
//   api       src/api/            the API and util
//   ui        src/ui/             ui, API types, and the shell contract (app/shellContext)
//   feature   src/<feature>/      features, ui, the API, and the shell contract
//   look      src/looks/<look>/   its own look, features, ui, API types, and app/; never another look or the API client
//   app       src/app/, main.tsx  everything: it is the composition root
//
// Tests are exempt: they put the whole app together. Only relative imports are checked; packages are not layers.
//
// Run: node scripts/check-boundaries.mjs (part of npm run lint).
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, posix, relative } from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const source = fileURLToPath(new URL("../src", import.meta.url));
const shellContract = "app/shellContext.ts";
const apiTypes = "api/types.ts";

/** Every source file under src, by its path relative to src, with forward slashes. */
function sources(directory) {
    return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
        const path = join(directory, entry.name);

        if (entry.isDirectory()) {
            return sources(path);
        }

        return /\.tsx?$/.test(entry.name) ? [relative(source, path).split("\\").join("/")] : [];
    });
}

/** Whether a file is a test or a test helper, which may import anything. */
const isTest = (file) => /\.test\.tsx?$/.test(file) || file.startsWith("test/");

/** The layer of a file: "util", "api", "ui", "app", "look:<name>", or "feature:<folder>". */
function layerOf(file) {
    const [first, second] = file.split("/");

    if (first === "time") {
        return "util";
    }

    if (first === "api" || first === "ui" || first === "app") {
        return first;
    }

    if (file === "main.tsx" || !file.includes("/")) {
        return "app";
    }

    return first === "looks" ? `look:${second}` : `feature:${first}`;
}

/** The file a relative import names, or null when it names nothing under src. */
function resolveImport(file, specifier, known) {
    const base = posix.normalize(posix.join(posix.dirname(file), specifier));

    for (const candidate of [base, `${base}.ts`, `${base}.tsx`, `${base}/index.ts`, `${base}/index.tsx`]) {
        if (known.has(candidate)) {
            return candidate;
        }
    }

    // Stylesheets and other assets belong to the folder they sit in; only code is layered.
    return null;
}

/**
 * Why an import breaks the rules, or null when it is allowed.
 *
 * @param from - The importing file.
 * @param to - The imported file.
 */
function breach(from, to) {
    const source = layerOf(from);
    const target = layerOf(to);

    if (source === "app") {
        return null;
    }

    if (source === "util") {
        return target === "util" ? null : "a utility imports only utilities";
    }

    // Every layer may use the utilities.
    if (target === "util") {
        return null;
    }

    if (source === "api") {
        return target === "api" ? null : "the API layer imports only itself and the utilities";
    }

    if (source === "ui") {
        return target === "ui" || to === apiTypes || to === shellContract
            ? null
            : "ui pieces import only ui, API types, and the shell contract";
    }

    if (source.startsWith("feature:")) {
        if (target.startsWith("look:")) {
            return "a feature never imports a look";
        }

        return target === "app" && to !== shellContract
            ? "a feature imports nothing in app/ but the shell contract"
            : null;
    }

    // A look.
    if (target.startsWith("look:") && target !== source) {
        return "a look never imports another look";
    }

    return target === "api" && to !== apiTypes ? "a look never calls the API client; a feature hook does" : null;
}

const files = sources(source).filter((file) => statSync(join(source, file)).isFile());
const known = new Set(files);
const pattern =
    /(?:^|[\s;])(?:import|export)\s[^"';]*?from\s*["'](\.{1,2}\/[^"']+)["']|import\s*\(?\s*["'](\.{1,2}\/[^"']+)["']/g;
const problems = [];

for (const file of files.filter((candidate) => !isTest(candidate))) {
    const text = readFileSync(join(source, file), "utf8");

    for (const match of text.matchAll(pattern)) {
        const specifier = match[1] ?? match[2];
        const target = resolveImport(file, specifier, known);
        const reason = target ? breach(file, target) : null;

        if (reason) {
            // Counted at the path itself: the pattern may start on the line break before the import.
            const line = text.slice(0, match.index + match[0].indexOf(specifier)).split("\n").length;
            problems.push(`src/${file}:${line} imports src/${target}: ${reason} (ADR 0021).`);
        }
    }
}

if (problems.length > 0) {
    console.error(problems.join("\n"));
    process.exit(1);
}

console.log(`Every import keeps to its layer (${files.length} files checked).`);
