# Design note: <feature slice name>

> Half a page is enough. Write it **before** coding the slice; the team lead signs it off.
> Copy this file to `docs/design-notes/<NN>-<slice-name>.md`.

| | |
|---|---|
| **Slice** | <e.g. M3: Barcode separation> |
| **Notion row** | <link> |
| **Author / date** | <name>, <yyyy-mm-dd> |
| **Status** | Draft · In review · ✅ Approved (<team lead>, <date>) |

## 1. Goal
What the user can do when this slice is done. One or two sentences.

## 2. Scope
- **In:** …
- **Out (later / never):** …
- Legacy behaviour to match (from the feature-review spreadsheet / Audit): …

## 3. Screens
Which views/viewmodels are added or changed. A rough sketch or list of controls is fine.

## 4. Data model
New or changed entities/fields (Core) and SQLite tables (Data). Migration needed? Y/N

## 5. Interfaces & providers touched
Which Core interfaces (`IOcrProvider`, `ISeparationEngine`, …) and which Infrastructure providers.
Any new third-party library? → must stay inside `IProfiler.Infrastructure`; licence checked?

## 6. Risks & open questions
Link any risk-register IDs (R…). Questions for the team lead.

## 7. Done when
- [ ] Acceptance checks (what to try by hand, with expected results)
- [ ] Tests added (unit / architecture / FlaUI)
- [ ] Regression checklist items still pass
- [ ] Installed build tested in Windows Sandbox (if it affects install/startup)
