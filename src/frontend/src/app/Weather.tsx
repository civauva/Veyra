import { useEffect, useRef } from "react";

import type { ShellScene } from "./shellContext";

/** Props for {@link Weather}. */
interface WeatherProps {
  /** The weather to draw: rain for showers, heavier rain and sheet lightning for a storm, nothing for clear skies. */
  scene: ShellScene | null;
  /** Whether the screen is a work screen, where the weather keeps to a band under the top bar. */
  calm: boolean;
}

/** One depth of rain: far drops are thin, faint, short, and slow; near drops the opposite. */
interface Layer {
  share: number;
  speed: [number, number];
  length: [number, number];
  width: number;
  alpha: [number, number];
}

interface Drop {
  layer: Layer;
  x: number;
  y: number;
  speed: number;
  length: number;
  alpha: number;
}

const layers: Layer[] = [
  { share: 0.55, speed: [520, 720], length: [7, 12], width: 0.8, alpha: [0.06, 0.12] },
  { share: 0.3, speed: [780, 1050], length: [12, 20], width: 1.1, alpha: [0.1, 0.18] },
  { share: 0.15, speed: [1150, 1500], length: [22, 34], width: 1.5, alpha: [0.14, 0.26] },
];

/** Drops on a 1440 × 900 screen; larger screens get more, up to three times as many. */
const density: Record<ShellScene, number> = { storm: 260, showers: 110, clear: 0 };

/** How far the wind tilts the rain, as horizontal pixels per vertical pixel. */
const wind: Record<ShellScene, number> = { storm: 0.24, showers: 0.12, clear: 0 };

/** How long the weather takes to settle into a band, or to spread out again, in seconds. */
const calmSeconds = 0.7;

const between = ([low, high]: [number, number]) => low + Math.random() * (high - low);

/**
 * The weather behind the shell, drawn on a canvas (VEY-23): three depths of rain that fall at their own speeds with a
 * little wind, and in a storm soft sheet lightning every 9 to 21 seconds, two gentle pulses and never a strobe. On
 * work screens the rain fades out below the top bar. The ink comes from the scene's `--weather-ink`, so it follows
 * the theme.
 *
 * Nothing moves for people who ask for reduced motion: they see one still frame. The animation pauses while the
 * page is hidden. It is decoration only and hidden from assistive technology.
 */
export function Weather({ scene, calm }: WeatherProps) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const target = useRef({ scene, calm });
  const redraw = useRef<(() => void) | null>(null);

  useEffect(() => {
    target.current = { scene, calm };
    redraw.current?.();
  }, [scene, calm]);

  useEffect(() => {
    const element = canvas.current;
    const context = element?.getContext("2d");

    if (!element || !context) {
      return;
    }

    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
    let drops: Drop[] = [];
    let width = 0;
    let height = 0;
    let drawn: ShellScene | null = null;
    let calmness = target.current.calm ? 1 : 0;
    let ink = "255,255,255";
    let flash: { at: number; x: number; power: number } | null = null;
    let nextFlash = performance.now() + 4_000 + Math.random() * 6_000;
    let last = performance.now();
    let frame = 0;

    function readInk() {
      if (element) {
        ink = getComputedStyle(element).getPropertyValue("--weather-ink").trim() || ink;
      }
    }

    function seed() {
      const kind = target.current.scene;
      const count = kind ? Math.round(density[kind] * Math.min(3, (width * height) / (1440 * 900))) : 0;

      drawn = kind;
      drops = layers.flatMap((layer) =>
        Array.from({ length: Math.round(count * layer.share) }, () => ({
          layer,
          x: Math.random() * width * 1.2 - width * 0.1,
          y: Math.random() * height,
          speed: between(layer.speed) * (kind === "showers" ? 0.8 : 1),
          length: between(layer.length),
          alpha: between(layer.alpha),
        })),
      );
    }

    function resize() {
      if (!element || !context) {
        return;
      }

      const ratio = Math.min(window.devicePixelRatio || 1, 2);

      width = element.clientWidth;
      height = element.clientHeight;
      element.width = Math.round(width * ratio);
      element.height = Math.round(height * ratio);
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
      readInk();
      seed();
    }

    function draw(now: number) {
      if (!context) {
        return;
      }

      const seconds = Math.min(0.05, (now - last) / 1000);
      const { scene: kind, calm: wanted } = target.current;

      last = now;

      if (kind !== drawn) {
        readInk();
        seed();
      }

      // The band settles in, rather than jumping, when the person moves between the home and a work screen.
      const step = reduced ? 1 : seconds / calmSeconds;
      calmness = wanted ? Math.min(1, calmness + step) : Math.max(0, calmness - step);

      const reach = height * (1 - calmness * 0.72);
      const tilt = kind ? wind[kind] : 0;

      context.clearRect(0, 0, width, height);

      if (flash) {
        const t = (now - flash.at) / 900;

        if (t >= 1) {
          flash = null;
        } else {
          // Two soft pulses, like lightning behind clouds.
          const pulse =
            t < 0.18
              ? t / 0.18
              : t < 0.32
                ? 1 - (t - 0.18) * 3
                : t < 0.42
                  ? 0.6 + (t - 0.32) * 3
                  : 0.9 * (1 - (t - 0.42) / 0.58);
          const glow = context.createRadialGradient(flash.x, -40, 0, flash.x, -40, Math.max(width, height) * 0.75);

          glow.addColorStop(0, `rgba(210,200,255,${0.32 * pulse * flash.power * (1 - calmness * 0.6)})`);
          glow.addColorStop(1, "rgba(210,200,255,0)");
          context.fillStyle = glow;
          context.fillRect(0, 0, width, reach);
        }
      } else if (kind === "storm" && !reduced && now > nextFlash) {
        flash = { at: now, x: width * (0.15 + Math.random() * 0.7), power: 0.6 + Math.random() * 0.4 };
        nextFlash = now + 9_000 + Math.random() * 12_000;
      }

      context.lineCap = "round";

      for (const drop of drops) {
        if (!reduced) {
          drop.y += drop.speed * seconds;
          drop.x += drop.speed * tilt * seconds;

          if (drop.y - drop.length > reach || drop.x > width + 40) {
            drop.y = -drop.length - Math.random() * 120;
            drop.x = Math.random() * width * 1.2 - width * 0.2;
          }
        }

        // Drops fade as they fall into the calm part of the screen.
        const fade = drop.y > reach * 0.7 ? Math.max(0, 1 - (drop.y - reach * 0.7) / (reach * 0.3)) : 1;

        context.strokeStyle = `rgba(${ink},${drop.alpha * fade})`;
        context.lineWidth = drop.layer.width;
        context.beginPath();
        context.moveTo(drop.x, drop.y);
        context.lineTo(drop.x - drop.length * tilt, drop.y - drop.length);
        context.stroke();
      }

      if (!reduced && !document.hidden) {
        frame = requestAnimationFrame(draw);
      }
    }

    function resume() {
      if (!document.hidden && !reduced) {
        cancelAnimationFrame(frame);
        last = performance.now();
        frame = requestAnimationFrame(draw);
      }
    }

    // The theme can change while the page is open; the ink follows it.
    const theme = new MutationObserver(readInk);
    theme.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });

    function onResize() {
      resize();

      if (reduced) {
        draw(performance.now());
      }
    }

    // Without motion there is one still frame, drawn again when the scene, the band, or the size changes.
    redraw.current = reduced ? () => draw(performance.now()) : null;

    window.addEventListener("resize", onResize);
    document.addEventListener("visibilitychange", resume);
    resize();
    frame = requestAnimationFrame(draw);

    return () => {
      redraw.current = null;
      cancelAnimationFrame(frame);
      theme.disconnect();
      window.removeEventListener("resize", onResize);
      document.removeEventListener("visibilitychange", resume);
    };
  }, []);

  return <canvas ref={canvas} className="shell-weather" aria-hidden="true" />;
}
