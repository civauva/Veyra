import { describe, expect, it } from "vitest";

import type { PostgresServer, SqlServer } from "../api/types";
import {
  ago,
  checkLines,
  installCommand,
  loginScript,
  newPassword,
  passwordScript,
  postgresLoginScript,
  postgresPasswordScript,
  serverExplanation,
  uninstallCommand,
} from "./monitoringFormat";

const now = new Date("2026-11-13T12:00:00Z");

const server: SqlServer = {
  id: "s1",
  name: "sql-prod-01",
  address: "sql-prod-01.corp.local",
  port: 1433,
  environment: "production",
  agentId: "a1",
  agentName: "Built in",
  login: "veyra_monitor",
  encryption: "Required",
  state: "Receiving",
  detail: null,
  since: "2026-11-13T11:59:52Z",
  createdAt: "2026-11-13T10:00:00Z",
  changedAt: "2026-11-13T10:00:00Z",
  platform: "SqlServer",
  database: null,
};

describe("ago", () => {
  it("says seconds, minutes, hours, and days", () => {
    expect(ago("2026-11-13T11:59:52Z", now)).toBe("8 s ago");
    expect(ago("2026-11-13T11:57:00Z", now)).toBe("3 min ago");
    expect(ago("2026-11-13T10:00:00Z", now)).toBe("2 h ago");
    expect(ago("2026-11-11T12:00:00Z", now)).toBe("2 d ago");
    expect(ago(null, now)).toBe("never");
  });
});

describe("serverExplanation", () => {
  it("tells what a failed login means and what to do", () => {
    expect(serverExplanation({ ...server, state: "LoginFailed" }, now)).toBe(
      "SQL Server refused the login veyra_monitor: a wrong password, or the login was never created. " +
        "Enter the password again, or run the login script again.",
    );
  });

  it("names the address and port of a server it cannot reach", () => {
    expect(serverExplanation({ ...server, state: "Unreachable" }, now)).toContain("sql-prod-01.corp.local:1433");
  });

  it("says when the last figures came", () => {
    expect(serverExplanation(server, now)).toBe("Last figures 8 s ago.");
  });
});

describe("checkLines", () => {
  it("passes every line once figures arrive", () => {
    expect(checkLines(server).map((line) => line.passed)).toEqual([true, true, true, true, true]);
  });

  it("knows nothing yet while waiting", () => {
    expect(checkLines({ ...server, state: "WaitingForData" }).map((line) => line.passed)).toEqual([
      null,
      null,
      null,
      null,
      null,
    ]);
  });

  it("stops at the login when it was refused", () => {
    const lines = checkLines({ ...server, state: "LoginFailed" });

    expect(lines.map((line) => line.passed)).toEqual([true, true, true, false, null]);
    expect(lines[3]).toEqual({
      text: "Logged in as veyra_monitor",
      passed: false,
      note: "wrong password, or the login does not exist",
    });
  });

  it("fails the last line for a missing grant", () => {
    expect(checkLines({ ...server, state: "GrantMissing" }).map((line) => line.passed)).toEqual([
      true,
      true,
      true,
      true,
      false,
    ]);
  });

  it("fails the first line when the agent is not there, naming it as the page does", () => {
    const lines = checkLines({ ...server, state: "AgentNotConnected" }, "the built-in agent");

    expect(lines[0]).toEqual({
      text: "The built-in agent has the settings",
      passed: false,
      note: "Agent not connected",
    });
  });

  it("says when the connection is not encrypted", () => {
    expect(checkLines({ ...server, encryption: "Off" })[2]?.text).toBe("Connected without encryption");
  });
});

describe("loginScript", () => {
  it("creates the login with exactly the grants of the SQL Server guide", () => {
    expect(loginScript("veyra_monitor", "Abc123")).toBe(
      [
        "USE master;",
        "CREATE LOGIN [veyra_monitor] WITH PASSWORD = N'Abc123';",
        "GRANT VIEW SERVER STATE TO [veyra_monitor];",
        "GRANT VIEW ANY DEFINITION TO [veyra_monitor];",
        "GRANT CONNECT ANY DATABASE TO [veyra_monitor];",
        "GO",
        "",
        "USE msdb;",
        "CREATE USER [veyra_monitor] FOR LOGIN [veyra_monitor];",
        "GRANT SELECT ON dbo.backupset TO [veyra_monitor];",
        "GRANT SELECT ON dbo.sysjobs TO [veyra_monitor];",
        "GRANT SELECT ON dbo.sysjobhistory TO [veyra_monitor];",
        "GRANT EXECUTE ON dbo.agent_datetime TO [veyra_monitor];",
        "GRANT SELECT ON dbo.backupmediafamily TO [veyra_monitor];",
        "GRANT SELECT ON dbo.sysjobsteps TO [veyra_monitor];",
        "GRANT SELECT ON dbo.sysjobactivity TO [veyra_monitor];",
        "GRANT SELECT ON dbo.sysjobschedules TO [veyra_monitor];",
        "GRANT SELECT ON dbo.sysschedules TO [veyra_monitor];",
        "GRANT SELECT ON dbo.suspect_pages TO [veyra_monitor];",
        "GRANT SELECT ON dbo.log_shipping_monitor_primary TO [veyra_monitor];",
        "GRANT SELECT ON dbo.log_shipping_monitor_secondary TO [veyra_monitor];",
        "GO",
      ].join("\n"),
    );
  });

  it("quotes a name and a password that would end their quotes", () => {
    expect(loginScript("odd]name", "it's")).toContain("CREATE LOGIN [odd]]name] WITH PASSWORD = N'it''s';");
    expect(passwordScript("odd]name", "it's")).toBe("ALTER LOGIN [odd]]name] WITH PASSWORD = N'it''s';");
  });
});

describe("loginScript for Azure SQL Database", () => {
  it("creates a contained user in the database itself, without USE", () => {
    const script = loginScript("veyra_monitor", "Abc123", "AzureSqlDatabase", "orders");

    expect(script).not.toContain("USE ");
    expect(script).toBe(
      [
        "-- Run this in the database orders, connected as its administrator.",
        "CREATE USER [veyra_monitor] WITH PASSWORD = N'Abc123';",
        "GRANT VIEW DATABASE STATE TO [veyra_monitor];",
        "GRANT VIEW DEFINITION TO [veyra_monitor];",
        "GO",
        "",
        "-- Azure SQL Database records no deadlocks unless asked.",
        "CREATE EVENT SESSION [veyra_deadlocks] ON DATABASE ADD EVENT sqlserver.xml_deadlock_report " +
          "ADD TARGET package0.ring_buffer (SET max_memory = 4096) WITH (STARTUP_STATE = ON);",
        "ALTER EVENT SESSION [veyra_deadlocks] ON DATABASE STATE = START;",
        "GO",
      ].join("\n"),
    );
  });

  it("quotes a name and a password that would end their quotes", () => {
    expect(loginScript("odd]name", "it's", "AzureSqlDatabase", "orders")).toContain(
      "CREATE USER [odd]]name] WITH PASSWORD = N'it''s';",
    );
    expect(passwordScript("odd]name", "it's", "AzureSqlDatabase")).toBe(
      "ALTER USER [odd]]name] WITH PASSWORD = N'it''s';",
    );
  });

  it("gives a Managed Instance the same script as SQL Server", () => {
    expect(loginScript("veyra_monitor", "Abc123", "ManagedInstance")).toBe(loginScript("veyra_monitor", "Abc123"));
  });
});

describe("newPassword", () => {
  it("makes 24 characters with capitals, small letters, and digits", () => {
    const password = newPassword();

    expect(password).toHaveLength(24);
    expect(password).toMatch(/[A-Z]/);
    expect(password).toMatch(/[a-z]/);
    expect(password).toMatch(/[0-9]/);
    expect(password).toMatch(/^[A-Za-z0-9]+$/);
  });

  it("tries again until the policy is met", () => {
    let calls = 0;
    // The first batch gives only capitals; the next ones are honest.
    const random = (bytes: Uint8Array<ArrayBuffer>) => {
      calls += 1;

      return calls === 1 ? bytes.fill(0) : crypto.getRandomValues(bytes);
    };

    expect(newPassword(random)).toMatch(/[a-z]/);
    expect(calls).toBeGreaterThan(1);
  });
});

describe("installCommand and uninstallCommand", () => {
  it("writes the PowerShell line that installs the Windows service with the code", () => {
    expect(installCommand("Windows", "https://veyra.example.com", "K7QM-4TWX-9PRA")).toBe(
      "irm https://veyra.example.com/v1/agents/install.ps1 | iex; Install-VeyraAgent -Code K7QM-4TWX-9PRA",
    );
    expect(uninstallCommand("Windows", "https://veyra.example.com")).toBe(
      "irm https://veyra.example.com/v1/agents/install.ps1 | iex; Uninstall-VeyraAgent",
    );
  });

  it("writes the shell line that installs the systemd service with the code", () => {
    expect(installCommand("Linux", "https://veyra.example.com", "K7QM-4TWX-9PRA")).toBe(
      "curl -fsSL https://veyra.example.com/v1/agents/install.sh | sudo sh -s -- --code K7QM-4TWX-9PRA",
    );
    expect(uninstallCommand("Linux", "https://veyra.example.com")).toBe(
      "curl -fsSL https://veyra.example.com/v1/agents/install.sh | sudo sh -s -- --uninstall",
    );
  });
});

describe("PostgreSQL servers", () => {
  const postgres: PostgresServer = { ...server, name: "pg-main", address: "10.0.0.5", port: 5432 };

  it("writes the role with pg_monitor, and says where pg_hba.conf must let the agent in", () => {
    const script = postgresLoginScript("veyra_monitor", "S3cret-Pass");

    expect(script).toContain(`CREATE ROLE "veyra_monitor" LOGIN PASSWORD 'S3cret-Pass';`);
    expect(script).toContain(`GRANT pg_monitor TO "veyra_monitor";`);
    expect(script).toContain("pg_hba.conf");
    expect(script).toContain("scram-sha-256");
  });

  it("doubles quotes in the role and the password", () => {
    expect(postgresLoginScript('odd"role', "it's")).toContain(`CREATE ROLE "odd""role" LOGIN PASSWORD 'it''s';`);
  });

  it("gives an existing role a new password in one line", () => {
    expect(postgresPasswordScript("veyra_monitor", "N3w-Pass")).toBe(`ALTER ROLE "veyra_monitor" PASSWORD 'N3w-Pass';`);
  });

  it("tells what a refused login means for PostgreSQL, pg_hba.conf included", () => {
    expect(serverExplanation({ ...postgres, state: "LoginFailed" }, now, "the built-in agent", "PostgreSQL")).toBe(
      "PostgreSQL refused the role veyra_monitor: a wrong password, the role was never created, or pg_hba.conf " +
        "does not let the agent in. Enter the password again, or run the script again.",
    );
  });

  it("tells a missing grant and a refused certificate in PostgreSQL's terms", () => {
    expect(serverExplanation({ ...postgres, state: "GrantMissing" }, now, "the agent", "PostgreSQL")).toContain(
      "pg_monitor",
    );
    expect(serverExplanation({ ...postgres, state: "CertificateRejected" }, now, "the agent", "PostgreSQL")).toContain(
      "does not take TLS",
    );
  });

  it("checks the role rather than a SQL login", () => {
    const lines = checkLines({ ...postgres, state: "LoginFailed" }, "the agent", "PostgreSQL");

    expect(lines[3]).toEqual({
      text: "Logged in as veyra_monitor",
      passed: false,
      note: "wrong password, no such role, or pg_hba.conf turns the agent away",
    });
  });
});
