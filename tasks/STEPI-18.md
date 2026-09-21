# STEPI-18 — Initial MVP Domain Entities and PostgreSQL Data Layer

## Project

StepIn.Api

Technology: C#, .NET 10, Entity Framework Core, PostgreSQL.

Existing solution projects:
- StepIn.Api
- StepIn.Business
- StepIn.Data
- StepIn.Domain

## Objective

Implement the initial MVP domain entities and PostgreSQL data layer for StepIn.

StepIn is a personal development application for setting goals, planning recurring activities, tracking progress, receiving reminders, and reflecting on achievements.

## Required entities

1. User — owns the user's data.
2. Goal — defines what the user wants to achieve, why, how, when, and how progress is measured.
3. Activity — defines work associated with a goal and how frequently it should be performed.
4. ActivityRecord — records an actual completed activity.
5. Note — records additional information associated with an activity record.
6. Reminder — supports fixed-time and activity-relative reminders, such as one hour before a scheduled activity.

## Requirements

- Define suitable entity relationships.
- Implement persistence using Entity Framework Core and PostgreSQL.
- Create the initial database migration.
- Implement automated tests.
- Document the domain model and data layer.

## Out of scope

- API endpoints
- Authentication
- Frontend development
- Background scheduling
- Notification delivery

## Team Lead assignment

Analyze this task and prepare an implementation plan.

Identify:
- Objectives and requirements.
- Proposed smaller development tasks and their dependencies.
- Suitable specialist assignments.
- Acceptance criteria for each proposed task.
- Testing and review requirements.
- Significant risks, ambiguities, open questions, and blockers.

Do not implement the task, modify repository files, create YouTrack issues, change ticket statuses, or execute other agents.

Do not invent missing business requirements. Clearly identify questions that require clarification.

This is an analysis-only pilot. Report proposed actions rather than executing them.