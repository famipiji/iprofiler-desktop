# OCR models (shipped with the app, never downloaded at runtime)

Copied to `<install dir>\models\` by `IProfiler.Infrastructure` and read through `AppPaths.Models`.
The client site may be offline (CLAUDE.md rule 8), so the models are part of the repo and the installer.

| Folder | Model | Source archive (SHA-256 of the .tar) |
|---|---|---|
| `ppocrv5-mobile/det` | PP-OCRv5 mobile text detection | `PP-OCRv5_mobile_det_infer.tar` (`50446e5d01ac2a73d5319c89513281f6578414c888c602f9af13f93feefffc58`) |
| `ppocrv5-mobile/rec-en` | PP-OCRv5 mobile English recognition (dictionary is inside `inference.yml`) | `en_PP-OCRv5_mobile_rec_infer.tar` (`e595b4cf2ffad19fbb5a61ba345d63939577a3ab8717b6e5995642590c9101b4`) |

Both archives come from PaddlePaddle's official model store
(`https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/<name>.tar`),
downloaded 2026-10-28. Licence: Apache-2.0 (PaddleOCR). Format: Paddle 3 PIR (`inference.json` +
`inference.pdiparams`), loaded by Sdcb.PaddleOCR 3.3.1.

To update: download the new archives, check their hashes, replace the folder contents, update this table,
and re-run the OCR tests and the spike (`spike/IProfiler.Spike`).
