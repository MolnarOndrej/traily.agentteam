# Traily Agent Team

Traily is a locally controlled AI software development orchestration system designed to coordinate a team of specialized AI agents throughout the software development lifecycle.

The project is currently a personal pilot focused on learning how to manage AI agents, automate development workflows, and maintain human oversight before potentially introducing the system into professional development environments.

## Vision

Traily acts as an AI development team that works alongside a human project owner.

Development tasks are managed through YouTrack, while source code, pull requests, and code reviews are managed through GitHub.

The intended workflow is:

1. **Task analysis:** The Team Lead picks up a ticket from YouTrack, analyzes its requirements, and delegates work to appropriate specialist agents.
2. **Implementation:** Development agents implement assigned tasks in isolated Git branches and worktrees and create pull requests.
3. **Code review:** Specialist reviewer agents independently review implementations and provide feedback through GitHub.
4. **Human approval:** The project owner reviews pull requests, requests changes when necessary, and retains exclusive control over merges and releases.
5. **Testing:** The Testing Expert verifies completed work and reports the results.
6. **Completion:** The project owner reviews the testing report and decides whether to close the ticket or request additional work.

Traily will eventually operate as a service that monitors task progress, coordinates agent execution, and manages workflow transitions.

## Agent Team

Traily is designed to support seven specialized agents:

| Agent          | Responsibility                                        |
| -------------- | ----------------------------------------------------- |
| Team Lead      | Task analysis, planning, delegation, and coordination |
| .NET Expert    | Backend development                                   |
| React Expert   | Frontend development                                  |
| UI/UX Expert   | Interface design and user experience                  |
| Testing Expert | Automated testing and quality assurance               |
| .NET Reviewer  | Independent backend code review                       |
| React Reviewer | Independent frontend code review                      |

Each agent has its own role instructions and can use reusable skills appropriate to its responsibilities.

## Technology Stack

* C# and .NET 10
* Microsoft.Extensions.DependencyInjection
* Codex CLI for AI agent execution
* YouTrack Cloud for task management
* Git and GitHub for source control and pull requests
* Windows as the initial development and execution environment

Traily's orchestration layer is intended to remain independent of individual AI providers, agent runtimes, and managed software projects.

## Initial Pilot

The first milestone is to implement and execute a functional Team Lead agent.

The initial managed project is **StepIn**, a personal application for goal setting, activity planning, progress tracking, and reflection.

The first pilot task is `STEPI-18`, which concerns the initial domain model and PostgreSQL data layer for StepIn's backend.

During this pilot, the Team Lead will analyze the task, identify requirements and dependencies, propose smaller tasks, recommend specialist assignments, and report open questions or blockers.

The initial execution is analysis-only. Automatic task creation, implementation, pull requests, and workflow transitions will be introduced incrementally.

## Project Status

Traily is in early development.

The current pilot registers the Team Lead agent with its role instructions
and task-analysis skill. Tasks are invoked through a catalog-driven
orchestration component and executed through a reusable agent runner.

Codex CLI execution is operational in read-only mode. Raw standard output
and standard error are streamed to correlated local trace files for
diagnostics and troubleshooting.

The current pilot reads `STEPI-18` from a local task file and asks the Team
Lead to perform analysis only. YouTrack automation, specialist-agent
execution, pull-request workflows, persistent workflow state, and automatic
task transitions have not yet been implemented.

## Guiding Principles

* Human approval remains mandatory for merges and releases.
* Agents operate within explicitly configured permissions.
* Agent roles, reusable skills, orchestration, and runtime execution remain separate.
* Components should be reusable across different agents and managed projects.
* Maintainability, testability, clean code, SOLID principles, and dependency injection guide implementation decisions.
* Start with a minimal working system and extend it incrementally.
