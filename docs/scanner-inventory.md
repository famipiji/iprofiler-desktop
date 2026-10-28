# Scanner Inventory (Task 0.1)

Interim inventory kept in the repo. The master copy for client sites is `TWAIN_Scanner_Inventory.xlsx`
(ScanStation Documentation folder). Raw script output: `docs/inventory/*.csv` (`tools/scanner-inventory.ps1`).

## Office (dev PC DESKTOP-68PG9DV, Windows 11 Pro 64-bit, build 26300), 2026-10-28

| Scanner | Type | TWAIN 32-bit | TWAIN 64-bit | WIA | Patch codes (hw) | Notes |
|---|---|---|---|---|---|---|
| **Avision AD7100** | Production sheet-fed, ADF 300 sheets, duplex, 100 ppm / 200 ipm, ~30k pages/day, USB 3.2 | ✅ 4.0.2.78 | ✅ 4.0.2.79 | ✅ 6.2.0.0 | ❓ not on the spec page; check the driver UI | Not connected when the script ran (PnP status Unknown). **Use for Task 1.1.** |
| **Avision FB6380E** | Flatbed | ✅ 4.0.2.78 | ✅ 4.0.2.78 | ✅ 6.2.2.0 | n/a (flatbed) | Not connected when the script ran |
| Brother DCP-T720DW | Network inkjet MFP | ❌ | ❌ | ✅ (WSD + eSCL) | ❌ | WIA-only; useful as a WIA test device, not a capture-station scanner |

- TWAIN DSM (`TWAINDSM.dll`): 32-bit ✅, 64-bit ✅. Microsoft's WIA-to-TWAIN bridge (`wiatwain.ds`, 32-bit) also present.
- **Scanners with no 64-bit TWAIN driver (office): 0.** Both Avision models have native 64-bit drivers, so the x64 app should reach them directly. The 32-bit worker (D2) stays as insurance for client sites.

## Client sites
Not collected yet. Ask support/installation for: site, scanner make/model, pages per day, patch-code sheets used? Then check each model's 64-bit driver / WIA / patch-code support.

## Legacy ScanStation config samples (D7)
Not collected yet. Needed before M5 (migration). Put them in `samples/legacy/` (git-ignored).
