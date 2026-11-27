import { expect, type Page, test } from "@playwright/test";

import { answerApi } from "./api";

/** The screens that draw charts, small lines, and tables in tiles, in both looks. */
const screens = [
  "/",
  "/logs",
  "/alerts",
  "/traces",
  "/metrics?tab=all&metric=http.server.request.duration&show=p95&split=service.name",
  "/metrics/hosts/web-01",
  "/metrics/services/payments-gateway?environment=production",
  "/metrics/databases/microsoft.sql_server/sql-prod-01",
  "/metrics/databases/microsoft.sql_server/sql-prod-01?tab=storage",
  "/metrics/databases/microsoft.sql_server/sql-prod-01?tab=backups",
  "/incidents/inc-42",
  "/integrations/sqlserver/s1",
  "/metrics",
  "/metrics/databases/microsoft.sql_server/sql-prod-01?tab=blocking",
  "/metrics/databases/microsoft.sql_server/sql-prod-01?tab=waits",
  "/metrics/databases/microsoft.sql_server/sql-prod-01?tab=queries",
  "/metrics/databases/microsoft.sql_server/sql-prod-01?tab=jobs",
  "/jobs",
  "/jobs/job-customersync",
  "/incidents",
  "/resources/services",
  "/resources/hosts",
  "/resources/databases",
  "/settings",
];

/** The least room, in pixels, between a chart's labels and legend and the edges of their tile. */
const inset = 6;

/** The most empty room, in pixels, below a chart that is the last thing in its tile. */
const slack = 28;

/**
 * Every chart's labels and legend inside its tile with room around them, and a chart that ends its tile filling it
 * (VEY-80). Returns the problems it finds, each naming the tile and the element.
 */
async function chartProblems(page: Page): Promise<string[]> {
  return page.evaluate(
    ({ inset, slack }) => {
      const problems: string[] = [];
      const bordered = (element: Element) => {
        const style = getComputedStyle(element);

        return parseFloat(style.borderTopWidth) > 0 && parseFloat(style.borderLeftWidth) > 0;
      };
      const name = (element: Element) =>
        element.getAttribute("aria-label") ??
        element.querySelector("h2, h3")?.textContent?.trim() ??
        element.className.toString();

      for (const chart of document.querySelectorAll(".line-chart, .ov-bars")) {
        if (chart.getBoundingClientRect().width === 0) {
          continue;
        }

        let tile = chart.parentElement;

        while (tile && !bordered(tile)) {
          tile = tile.parentElement;
        }

        if (!tile) {
          continue;
        }

        const box = tile.getBoundingClientRect();
        const header = tile.querySelector(":scope > header");
        const footer = tile.querySelector(":scope > footer");
        const top = header ? header.getBoundingClientRect().bottom : box.top;
        const bottom = footer ? footer.getBoundingClientRect().top : box.bottom;
        const where = `"${name(tile)}"`;

        for (const part of chart.querySelectorAll("text, li, figcaption > *, .ov-axis > span")) {
          const edge = part.getBoundingClientRect();

          if (edge.width === 0 || !part.textContent?.trim()) {
            continue;
          }

          const what = `"${part.textContent.trim()}" in ${where}`;

          if (edge.left < box.left + inset || edge.right > box.right - inset) {
            problems.push(`${what} is not inside the tile's sides`);
          }

          if (edge.top < top + inset) {
            problems.push(`${what} touches the top of the tile`);
          }

          if (edge.bottom > bottom - inset) {
            problems.push(`${what} touches the bottom of the tile`);
          }
        }

        const chartBottom = chart.getBoundingClientRect().bottom;
        const below = [...tile.querySelectorAll("*")].filter((element) => {
          const edge = element.getBoundingClientRect();

          return (
            edge.height > 0 &&
            edge.top >= chartBottom - 1 &&
            !footer?.contains(element) &&
            !chart.contains(element) &&
            !element.contains(chart)
          );
        });

        if (below.length === 0 && bottom - chartBottom > slack) {
          problems.push(`the chart in ${where} leaves ${Math.round(bottom - chartBottom)} px empty below it`);
        }
      }

      return problems;
    },
    { inset, slack },
  );
}

/**
 * Text that runs out of its tile, and small lines drawn over text (VEY-80). A tile is the nearest box with a border all
 * round; text inside a box that scrolls or clips is that box's to show, and text that is not shown, as in a closed
 * details element, is left out.
 */
async function spillProblems(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const problems = new Set<string>();
    const bordered = (element: Element) => {
      const style = getComputedStyle(element);

      return ["Top", "Right", "Bottom", "Left"].every(
        (side) => parseFloat(style.getPropertyValue(`border-${side.toLowerCase()}-width`)) > 0,
      );
    };
    const clips = (element: Element) => {
      const style = getComputedStyle(element);

      return style.overflowX !== "visible" || style.overflowY !== "visible";
    };
    // The box of an element's own words, leaving out its children, which are measured on their own.
    const glyphs = (element: Element) => {
      const boxes = [...element.childNodes]
        .filter((node) => node.nodeType === Node.TEXT_NODE && node.textContent?.trim())
        .map((node) => {
          const range = document.createRange();
          range.selectNodeContents(node);

          return range.getBoundingClientRect();
        })
        .filter((box) => box.width > 0);
      const left = Math.min(...boxes.map((box) => box.left));
      const top = Math.min(...boxes.map((box) => box.top));

      return boxes.length === 0
        ? new DOMRect()
        : new DOMRect(
            left,
            top,
            Math.max(...boxes.map((box) => box.right)) - left,
            Math.max(...boxes.map((box) => box.bottom)) - top,
          );
    };
    const words = (element: Element) => (element.textContent ?? "").trim().replace(/\s+/g, " ").slice(0, 40);
    const main = document.querySelector("main") ?? document.body;
    const texts = [...main.querySelectorAll("*")].filter(
      (element) =>
        [...element.childNodes].some((node) => node.nodeType === Node.TEXT_NODE && node.textContent?.trim()) &&
        element.checkVisibility({ visibilityProperty: true }) &&
        glyphs(element).width > 0,
    );

    for (const text of texts) {
      let tile = text.parentElement;
      let clipped = clips(text);

      while (tile && tile !== main && !bordered(tile)) {
        clipped ||= clips(tile);
        tile = tile.parentElement;
      }

      if (!tile || tile === main || clipped || clips(tile)) {
        continue;
      }

      const box = tile.getBoundingClientRect();
      const edge = glyphs(text);

      if (edge.left < box.left - 1 || edge.right > box.right + 1 || edge.bottom > box.bottom + 1) {
        problems.add(`"${words(text)}" runs out of its tile`);
      }
    }

    for (const line of main.querySelectorAll("svg.rs-line")) {
      const edge = line.getBoundingClientRect();

      for (const text of texts) {
        const other = glyphs(text);
        const across = Math.min(edge.right, other.right) - Math.max(edge.left, other.left);
        const down = Math.min(edge.bottom, other.bottom) - Math.max(edge.top, other.top);

        if (across > 4 && down > 4) {
          problems.add(`a small line is drawn over "${words(text)}"`);
        }
      }
    }

    return [...problems];
  });
}

for (const look of ["console", "forecast"] as const) {
  test.describe(`Charts in their tiles, ${look} (VEY-80)`, () => {
    for (const path of screens) {
      test(`${path} keeps its charts inside their tiles and fills them`, async ({ page }) => {
        await answerApi(page, { look });
        await page.goto(path);
        await page.waitForLoadState("networkidle");

        expect.soft(await chartProblems(page)).toEqual([]);
        expect.soft(await spillProblems(page)).toEqual([]);
      });
    }
  });
}
