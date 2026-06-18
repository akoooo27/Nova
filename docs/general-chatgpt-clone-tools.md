# General ChatGPT Clone Tool Surface

This is the reference list for tools and product capabilities Nova should consider for a general-purpose ChatGPT-like assistant.

Developer-agent and research-agent capabilities should live in their own architectures. This document is for the ordinary chat assistant: broad, useful, safe, and low-friction.

## Recommended V1

1. **Web search**
   - Current status: already exists as `web_search`.
   - Purpose: answer questions that depend on current or changing information.
   - Keep citations visible when web results shape the answer.

2. **Open URL / read web page**
   - Add a companion to search, such as `open_url` or `read_web_page`.
   - Purpose: fetch and summarize a specific page, verify sources, and support better citations than snippets alone.
   - This should be read-only.

3. **File upload and file reader**
   - Support PDFs, images, text files, CSV, XLSX, DOCX, and PPTX over time.
   - Purpose: summarize, compare, extract, transform, and answer questions about user-provided files.
   - Start with text extraction and basic metadata before richer document-specific handling.

4. **Data analysis sandbox**
   - A restricted execution environment for calculations, tables, CSV/XLSX analysis, parsing, and chart preparation.
   - This is not the developer agent. It is the everyday "analyze this spreadsheet" tool.
   - Keep it isolated, quota-limited, and without unrestricted network access.

5. **Chart / visualization output**
   - Let the assistant return structured chart specs for bar, line, pie, scatter, and table views.
   - Product-wise, charts should feel like first-class response blocks, even if implementation uses the data analysis sandbox.

6. **Image understanding**
   - Let users upload screenshots, photos, diagrams, receipts, UI captures, and charts.
   - Purpose: describe, compare, extract text, explain diagrams, and reason over visual input.
   - This may be model input rather than an `IAgentTool`, but it belongs in the general assistant surface.

7. **Artifacts / writing blocks**
   - A side-panel or rich block for long-form documents, plans, reports, HTML snippets, tables, and reusable generated content.
   - Support versioning/export later.
   - This is the general-assistant equivalent of Claude Artifacts or ChatGPT writing blocks.

## Strong V2 Candidates

8. **Persistent file library**
   - Store uploaded and generated files beyond a single chat.
   - Purpose: let users reuse documents across conversations.
   - Needs storage quotas, delete controls, and clear temporary-chat behavior.

9. **Image generation and editing**
   - Text-to-image and image editing for ordinary creative tasks.
   - Keep it separate from image understanding.
   - Generated images should be saved into the file/library model when that exists.

10. **Memory controls**
    - Add explicit concepts or tools such as `remember_preference`, `forget_memory`, and `list_memory_sources`.
    - Users need visibility and control over personalization.
    - Temporary chats should not read or write memory.

11. **Scheduled tasks / reminders**
    - Examples: "remind me tomorrow", "every Monday ask me for a status update", "check this later".
    - Needs notification infrastructure, recurrence handling, and clear limits.

## Later / Permission-Heavy

12. **Connectors**
    - Gmail, Outlook, Google Drive, OneDrive, Calendar, Notion, Slack, etc.
    - Start read-only.
    - Each connector needs OAuth, scopes, audit logs, revocation, and per-source visibility in answers.

13. **Safe action tools**
    - Draft email, send email, create calendar event, create document, export file.
    - Actions should require user confirmation before external side effects.
    - The assistant should show exactly what it will do before doing it.

14. **Browser-use / computer-use style tools**
    - Powerful, but not a baseline general-chat feature.
    - Defer until permissions, sandboxing, audit trails, and prompt-injection defenses are mature.

## Recommended Build Order

1. Strengthen web: keep `web_search`, add `open_url`.
2. Add file upload reading for common document types.
3. Add data analysis sandbox and structured chart outputs.
4. Add image understanding.
5. Add artifacts / writing blocks.
6. Add persistent file library.
7. Add memory controls.
8. Add image generation/editing.
9. Add scheduled tasks.
10. Add connectors and safe external actions.

## Architecture Notes For Nova

- Keep using the existing provider-agnostic `IAgentTool` abstraction for backend tools.
- Do not make every capability an agent tool. Some capabilities are better modeled as message inputs or response blocks:
  - Image understanding: model input.
  - Attachments: context-building input.
  - Artifacts/charts: response block/output model.
  - Memory: retrieval/context plus explicit management endpoints.
- Move from search-specific options like `ForceUseSearch` toward per-tool availability and mode:
  - disabled
  - auto
  - required
  - requires confirmation
- Tool calls should produce user-visible events, not just hidden model plumbing.
- Every external or persistent capability needs auditability:
  - what tool ran
  - with which arguments
  - what source or file it accessed
  - what result summary was shown to the model
- Read-only tools can ship first. Write/action tools should wait for confirmation UX and permission design.

## Recommended Product Position

Start with a safe general assistant:

- Read the web.
- Read user files.
- Analyze data.
- Understand images.
- Produce charts and durable artifacts.

Then expand into a personal workspace:

- Persistent file library.
- Memory controls.
- Scheduled tasks.
- Connectors.
- Confirmed external actions.
