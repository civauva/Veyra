import { useCallback, useEffect, useRef, useState } from "react";

import { ApiError, api } from "../api/client";
import type {
  Agent,
  IntegrationDetails,
  IntegrationList,
  IntegrationModule,
  IntegrationModuleInfo,
  Probe,
  ProbeInput,
  SqlServer,
  SqlServerInput,
  SqlServerState,
} from "../api/types";
import { useShell } from "../app/shellContext";
import { useMetricData } from "../metrics/useMetricData";

/** How often a pending probe is asked again (ADR 0028). */
export const probeEveryMs = 3_000;

/** How long a probe may take before the wizard stops waiting; the server removes its receiver then too. */
export const probeLimitMs = 120_000;

/** How often the installation asks how a saved server stands, until its figures arrive. */
export const installEveryMs = 5_000;

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof ApiError ? failure.message : failure instanceof Error ? failure.message : String(failure);
}

/** The window outside a shell, as in a component test: the last 24 hours. */
function outsideWindow(): { from: string; to: string } {
  return { from: new Date(Date.now() - 86_400_000).toISOString(), to: new Date().toISOString() };
}

/** What {@link useIntegrations} gives a screen. */
export interface IntegrationsView {
  /** The integrations; null before the first answer. */
  list: IntegrationList | null;
  /** Why the latest read failed, or null. */
  error: string | null;
}

/**
 * Every integration with its state, version, and modules (ADR 0028), read again on the shell's refresh.
 *
 * @returns The list and the latest error.
 */
export function useIntegrations(): IntegrationsView {
  const { data, error } = useMetricData("integrations", (signal) => api.integrations(signal));

  return { list: data, error };
}

/** What {@link useWizardData} gives the wizard. */
export interface WizardData {
  /** The agents that could read the server; null before they arrive. */
  agents: Agent[] | null;
  /** The template's modules; null before they arrive. */
  catalog: IntegrationModuleInfo[] | null;
  /** The template version a new server takes, from the integrations Veyra knows; null when none says. */
  templateVersion: string | null;
  /** Why a read failed, or null. */
  error: string | null;
}

/**
 * What the wizard needs before its first step: the agents, the template's modules, and the current template version.
 * The agents and the version follow the shell's refresh; the catalog is read once.
 *
 * @returns The agents, the catalog, the version, and the latest error.
 */
export function useWizardData(): WizardData {
  const agents = useMetricData("integration-agents", (signal) => api.agents(signal));
  const catalog = useMetricData("integration-modules", (signal) => api.integrationModules(signal), { paused: true });
  const { list, error } = useIntegrations();

  return {
    agents: agents.data,
    catalog: catalog.data,
    templateVersion: list?.items[0]?.latestVersion ?? null,
    error: agents.error ?? catalog.error ?? error,
  };
}

/** What {@link useModuleCatalog} gives a screen. */
export interface CatalogView {
  /** The template's modules; null before they arrive. */
  catalog: IntegrationModuleInfo[] | null;
  /** Why the read failed, or null. */
  error: string | null;
}

/**
 * The SQL Server template's modules with their default intervals and choices, read once.
 *
 * @returns The catalog and the latest error.
 */
export function useModuleCatalog(): CatalogView {
  const { data, error } = useMetricData("integration-modules", (signal) => api.integrationModules(signal), {
    paused: true,
  });

  return { catalog: data, error };
}

/** What {@link useProbe} gives a screen. */
export interface ProbeView {
  /** The latest probe as the server answered, or null before one starts. */
  probe: Probe | null;
  /** The key of the connection it tried, as {@link ProbeView.start} was given it. */
  key: string | null;
  /** Whether it is still pending. */
  running: boolean;
  /** Seconds since it started. */
  elapsedSeconds: number;
  /** Why it could not be started or followed, or null. */
  error: string | null;
  /**
   * Starts a probe and follows it every 3 seconds until it succeeds, fails, or 2 minutes pass.
   *
   * @param input - The connection, or a saved server's ID.
   * @param key - What the probe tried, to tell later whether its answer still applies.
   */
  start(input: ProbeInput, key?: string): void;
  /** Forgets the probe, as when the connection changed. */
  reset(): void;
}

/**
 * A probe through the agent (ADR 0028): Test connection before saving, and Validate now on a saved server. The agent's
 * Collector applies the probe's configuration twice, so a screen says so before it starts one.
 *
 * @param onDone - Called once a probe ends, with how it ended.
 * @returns The probe, its time, and the start and reset.
 */
export function useProbe(onDone?: (probe: Probe) => void): ProbeView {
  const [probe, setProbe] = useState<Probe | null>(null);
  const [key, setKey] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [startedAt, setStartedAt] = useState<number | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [error, setError] = useState<string | null>(null);
  const run = useRef(0);
  const done = useRef(onDone);

  useEffect(() => {
    done.current = onDone;
  }, [onDone]);

  // Stops following when the screen closes.
  useEffect(
    () => () => {
      run.current += 1;
    },
    [],
  );

  // The elapsed time ticks every second while the probe runs.
  useEffect(() => {
    if (!running) {
      return;
    }

    const timer = window.setInterval(() => setNow(Date.now()), 1_000);

    return () => window.clearInterval(timer);
  }, [running]);

  const finish = useCallback((ended: Probe) => {
    setProbe(ended);
    setRunning(false);
    setNow(Date.now());
    done.current?.(ended);
  }, []);

  const start = useCallback(
    (input: ProbeInput, nextKey?: string) => {
      const mine = ++run.current;
      const began = Date.now();

      setProbe(null);
      setKey(nextKey ?? null);
      setError(null);
      setRunning(true);
      setStartedAt(began);
      setNow(began);

      async function follow(id: string) {
        while (run.current === mine) {
          await new Promise((resolve) => window.setTimeout(resolve, probeEveryMs));

          if (run.current !== mine) {
            return;
          }

          try {
            const answer = await api.probe(id);

            if (run.current !== mine) {
              return;
            }

            setProbe(answer);

            if (answer.state !== "pending") {
              finish(answer);

              return;
            }
          } catch (failure) {
            // One failed read is not the end: the next one may answer.
            setError(messageOf(failure));
          }

          if (Date.now() - began >= probeLimitMs) {
            finish({
              id,
              state: "failed",
              startedAt: new Date(began).toISOString(),
              finishedAt: new Date().toISOString(),
              error: { reason: "timed-out", message: "The agent did not answer within 2 minutes." },
              result: null,
            });

            return;
          }
        }
      }

      api.startProbe(input).then(
        ({ id }) => {
          if (run.current === mine) {
            setProbe({
              id,
              state: "pending",
              startedAt: new Date(began).toISOString(),
              finishedAt: null,
              error: null,
              result: null,
            });
            void follow(id);
          }
        },
        (failure: unknown) => {
          if (run.current === mine) {
            setError(messageOf(failure));
            setRunning(false);
          }
        },
      );
    },
    [finish],
  );

  const reset = useCallback(() => {
    run.current += 1;
    setProbe(null);
    setKey(null);
    setError(null);
    setRunning(false);
    setStartedAt(null);
  }, []);

  return {
    probe,
    key,
    running,
    elapsedSeconds: startedAt === null ? 0 : Math.max(0, (now - startedAt) / 1000),
    error,
    start,
    reset,
  };
}

/** What {@link useInstall} gives the wizard's last step. */
export interface InstallView {
  /** The saved server, or null before saving. */
  saved: SqlServer | null;
  /** How its monitoring stands, followed until its figures arrive; null before saving. */
  state: SqlServerState | null;
  /** Whether the save is under way. */
  busy: boolean;
  /** Why saving failed, or null. */
  error: string | null;
  /**
   * Saves the server; its agent starts reading it within half a minute.
   *
   * @param input - The server.
   */
  install(input: SqlServerInput): Promise<void>;
}

/**
 * The wizard's installation: saves the server, then follows its state every 5 seconds until it is Receiving.
 *
 * @returns The saved server, its state, and the save.
 */
export function useInstall(): InstallView {
  const [saved, setSaved] = useState<SqlServer | null>(null);
  const [state, setState] = useState<SqlServerState | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!saved || state === "Receiving") {
      return;
    }

    const controller = new AbortController();
    const timer = window.setInterval(() => {
      api.integrations(controller.signal).then(
        (list) => {
          const found = list.items.find((item) => item.id === saved.id);

          if (found) {
            setState(found.state);
          }
        },
        () => undefined,
      );
    }, installEveryMs);

    return () => {
      window.clearInterval(timer);
      controller.abort();
    };
  }, [saved, state]);

  const install = useCallback(async (input: SqlServerInput) => {
    setBusy(true);
    setError(null);

    try {
      const stored = await api.createSqlServer(input);
      setSaved(stored);
      setState(stored.state);
    } catch (failure) {
      setError(messageOf(failure));
    } finally {
      setBusy(false);
    }
  }, []);

  return { saved, state, busy, error, install };
}

/** What {@link useIntegration} gives the package page. */
export interface IntegrationView {
  /** The package page's read; null before it arrives. */
  details: IntegrationDetails | null;
  /** Whether the server knows no integration by this ID. */
  notFound: boolean;
  /** Why the latest read failed, or null. */
  error: string | null;
  /** Whether an action is under way. */
  busy: boolean;
  /** Why the latest action failed, or null. */
  actionError: string | null;
  /** The Validate now probe. */
  validation: ProbeView;
  /** Runs Validate now: a probe of the saved server's stored connection. */
  validate(): void;
  /**
   * Moves the integration to another template version: Update or Roll back.
   *
   * @param version - The version.
   */
  setVersion(version: string): Promise<void>;
  /**
   * Saves the modules' switches and intervals; the agent applies them within a minute.
   *
   * @param modules - The modules.
   * @returns Whether it saved.
   */
  saveModules(modules: IntegrationModule[]): Promise<boolean>;
  /**
   * Removes the integration; the data it sent stays until retention removes it.
   *
   * @returns Whether it was removed.
   */
  remove(): Promise<boolean>;
}

/**
 * The server as it is stored, ready to save again with a change: its password stays as stored.
 *
 * @param server - The stored server.
 * @returns The request of `PUT /api/v1/sql-servers/{id}`.
 */
export function inputOfServer(server: SqlServer): SqlServerInput {
  return {
    name: server.name,
    address: server.address,
    port: server.port,
    environment: server.environment,
    agentId: server.agentId,
    login: server.login,
    password: null,
    encryption: server.encryption,
    platform: server.platform,
    database: server.database,
    instance: server.instance ?? null,
    authentication: server.authentication ?? "sql",
    modules: server.modules,
  };
}

/**
 * One integration's package page over the shell's time range (ADR 0028): the template, the Collector, validation, the
 * modules' figures, the checks, the next steps, and the history, read again on the shell's refresh, with Validate now,
 * Update, Roll back, the modules' save, and Remove.
 *
 * @param id - The server's ID.
 * @returns The page's read and its actions.
 */
export function useIntegration(id: string): IntegrationView {
  const { time } = useShell();
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const key = `integration-${id}-${JSON.stringify(time?.choice ?? null)}`;
  const { data, notFound, error } = useMetricData(
    key,
    (signal) => api.sqlServerIntegration(id, time ? time.window() : outsideWindow(), signal),
    { choices: String(revision) },
  );
  const reload = useCallback(() => setRevision((value) => value + 1), []);
  const validation = useProbe(reload);
  const { start } = validation;

  const act = useCallback(async (action: () => Promise<unknown>): Promise<boolean> => {
    setBusy(true);
    setActionError(null);

    try {
      await action();

      return true;
    } catch (failure) {
      setActionError(messageOf(failure));

      return false;
    } finally {
      setBusy(false);
    }
  }, []);

  const setVersion = useCallback(
    async (version: string) => {
      if (await act(() => api.setIntegrationVersion(id, version))) {
        reload();
      }
    },
    [act, id, reload],
  );

  const saveModules = useCallback(
    async (modules: IntegrationModule[]) => {
      const saved = await act(async () => {
        const server = (await api.sqlServers()).find((one) => one.id === id);

        if (!server) {
          throw new Error("This server was removed.");
        }

        await api.updateSqlServer(id, { ...inputOfServer(server), modules });
      });

      if (saved) {
        reload();
      }

      return saved;
    },
    [act, id, reload],
  );

  const remove = useCallback(() => act(() => api.deleteSqlServer(id)), [act, id]);
  const validate = useCallback(() => start({ serverId: id }), [start, id]);

  return { details: data, notFound, error, busy, actionError, validation, validate, setVersion, saveModules, remove };
}
