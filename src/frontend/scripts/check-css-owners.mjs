// Checks that every class a screen styles on its own belongs to that screen alone.
//
// Vite bundles every stylesheet into one, so a rule written for one screen applies on every screen. When two
// stylesheets use the same class name, one screen's rules land on the other's elements: the Users screen's green
// "live" dot once moved the Alerts screen's bars off their track, and its person badge stretched the home's round
// alert numbers into pills. This check fails when a class that one stylesheet styles on its own (as the first part of
// a selector) appears in any selector of another feature's stylesheet. Stylesheets in one feature folder, such as
// home/ and home/large/, count as one. Each look under looks/ is an owner of its own, so two looks never style the
// same class (ADR 0021). The shared stylesheet, styles/tokens.css, is meant to be built on and is exempt; deliberate
// sharing between features is listed below with its reason.
//
// Run: node scripts/check-css-owners.mjs (part of npm run lint).
import { readdirSync, readFileSync } from "node:fs";
import { join, relative } from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const source = fileURLToPath(new URL("../src", import.meta.url));
const shared = "styles/tokens.css";

/** Classes more than one feature may use, each with the reason it is shared. */
const allowed = {
    shell: "The home paints the app shell with the weather of its scene.",
    "log-days": "The Alerts screen's weather log is also drawn on the wall.",
    "people-dialog": "The Account screen opens the Users screen's dialog, wider.",
    avatar: "The Account screen shows the Users screen's avatar.",
    "detail-close": "The trace view's span panel reuses the Log Explorer's detail panel.",
    "section-title": "The trace view's span panel reuses the Log Explorer's detail panel.",
    cap: "The alert screens use the home's small-capitals headings.",
    fine: "The alert screens use the home's fine print.",
    "step-button": "The alert screens use the home's step buttons.",
    "screen-bar": "Each look fits the shared screen bar into its own frame (ADR 0021).",
    "screen-trail": "Each look sets the shared screen bar's trail in its own type (ADR 0021).",
};

/** The feature a stylesheet belongs to: its first folder under src, or for a look its own folder under looks/. */
const featureOf = (file) => {
    const [first, second] = file.split("/");

    return first === "looks" ? `looks/${second}` : first;
};

/** Every stylesheet under src, by its path relative to src. */
function stylesheets(directory) {
    return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
        const path = join(directory, entry.name);

        if (entry.isDirectory()) {
            return stylesheets(path);
        }

        return entry.name.endsWith(".css") ? [path] : [];
    });
}

/** The selectors of a stylesheet, without comments, at-rule preludes, or keyframe steps. */
function selectorsOf(css) {
    return [...css.replace(/\/\*[\s\S]*?\*\//g, "").matchAll(/([^{};]+)\{/g)]
        .map((match) => match[1].trim())
        .filter((group) => !group.startsWith("@") && !/^(from|to|\d+%)/.test(group))
        .flatMap((group) => group.split(","))
        .map((selector) => selector.trim());
}

const owners = new Map();
const mentions = new Map();

for (const path of stylesheets(source)) {
    const file = relative(source, path).replaceAll("\\", "/");

    for (const selector of selectorsOf(readFileSync(path, "utf8"))) {
        const subject = selector.split(/[\s>+~]+/)[0].match(/^\.([\w-]+)/);

        if (subject) {
            owners.set(subject[1], new Set([...(owners.get(subject[1]) ?? []), file]));
        }

        for (const [, name] of selector.matchAll(/\.([a-zA-Z_][\w-]*)/g)) {
            mentions.set(name, new Set([...(mentions.get(name) ?? []), file]));
        }
    }
}

const problems = [];

for (const [name, files] of owners) {
    const owner = [...files].filter((file) => file !== shared);

    if (owner.length === 0 || name in allowed) {
        continue;
    }

    const others = [...(mentions.get(name) ?? [])].filter(
        (file) => file !== shared && featureOf(file) !== featureOf(owner[0]),
    );

    if (others.length > 0) {
        problems.push(`.${name} is styled in ${owner[0]} and also used in ${others.join(", ")}`);
    }
}

if (problems.length > 0) {
    console.error("Classes shared between stylesheets leak from one screen to another. Rename one, or move the");
    console.error("class to styles/tokens.css if it is meant to be shared:");
    problems.sort().forEach((problem) => console.error(`  ${problem}`));
    process.exit(1);
}

console.log(`Every class belongs to one stylesheet (${owners.size} checked).`);
