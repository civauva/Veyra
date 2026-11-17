import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { PermissionNotice } from "./PermissionNotice";
import { permissionsGranted, permissionsMissingJobSteps } from "./testDatabase";

describe("PermissionNotice", () => {
  it("says what the login lacks on the tab it blinds, and the statement that grants it", () => {
    render(<PermissionNotice permissions={permissionsMissingJobSteps} tab="jobs" server="sql-prod-01" />);

    const notice = screen.getByRole("list", { name: "What Veyra cannot read" });
    expect(notice).toHaveTextContent("Veyra cannot read the steps of each job run on sql-prod-01");
    expect(notice).toHaveTextContent("veyra_monitor lacks SELECT ON msdb.dbo.sysjobsteps.");
    expect(within(notice).getByText(/GRANT SELECT ON dbo\.sysjobsteps TO \[veyra_monitor\];/)).toBeInTheDocument();
  });

  it("keeps quiet on a tab the missing permission does not touch", () => {
    render(<PermissionNotice permissions={permissionsMissingJobSteps} tab="live" server="sql-prod-01" />);

    expect(screen.queryByRole("list", { name: "What Veyra cannot read" })).not.toBeInTheDocument();
  });

  it("keeps quiet when the login may read everything, or before the agent has reported", () => {
    const { rerender } = render(<PermissionNotice permissions={permissionsGranted} tab="jobs" server="sql-prod-01" />);
    expect(screen.queryByRole("list", { name: "What Veyra cannot read" })).not.toBeInTheDocument();

    rerender(<PermissionNotice permissions={null} tab="jobs" server="sql-prod-01" />);
    expect(screen.queryByRole("list", { name: "What Veyra cannot read" })).not.toBeInTheDocument();
  });
});
