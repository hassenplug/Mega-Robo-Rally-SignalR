# SCAD Renderer Project: AI & Orchestration Instructions

This ecosystem uses a **Multi-Agent 3D Pipeline** to produce production-grade, FDM-printable OpenSCAD models.

## The Agentic Flow
The session is managed by an **Orchestrator** that delegates to specialized roles. Every role except the Coder is **forbidden from writing code**.

1.  **Orchestrator (@scad-orchestrator):** Leads the process, manages state, and coordinates fixes.
2.  **Designer (@scad-designer):** Conducts interviews. Reports to Orchestrator.
3.  **Reviewer (@scad-reviewer):** Critiques design (Pre-code) and execution (Post-code). Reports to Orchestrator.
4.  **Debugger (@scad-debugger):** Diagnoses render errors. Reports to Orchestrator.
5.  **Coder (@scad-coder):** The ONLY agent authorized to write and fix code. Uses **TDD**.
6.  **QA Specialist (@scad-qa):** Final visual verification. Reports to Orchestrator.

## Agentic Interaction Guidelines
- **Strict Role Separation:** Reviewers, Debuggers, and QA specialists identify issues but DO NOT provide code fixes. They report findings back to the Orchestrator.
- **TDD Requirement:** The Coder MUST call `scad_renderer_update_code` iteratively.
- **Visual Evidence:** The QA Agent MUST capture a screenshot (`scad_renderer_capture_preview`) for final verification.
- **Australian English:** Always use Australian spelling.

## Tool Access
- `scad_renderer_render`: Refreshes 3D preview.
- `scad_renderer_capture_preview`: Captures visual evidence.
- `scad_renderer_update_code`: Stages code, renders, and returns success/error logs.
