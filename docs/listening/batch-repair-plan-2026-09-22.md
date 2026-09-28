# Batch repair plan: second-extract boundary and preparation window (NOT APPLIED)

Generated 2026-09-22 from the fleet audit (evidence runs 35662307434, 35664076043 (Whisper small.en on GitHub Actions)). **Nothing in this document has been applied to production.** Each item below needs your approval first (owner rule: non-urgent production changes are reported before they are applied).

## What is wrong

- The second extract of Part A (and Part C) must start at its introduction ("Extract 2 / Now look at Extract 2 ..."), followed by the recording's own full preparation pause, then the beep and the dialogue.
- In these papers the introduction still sits at the end of the previous section (ST11 to ST15, Nova 14, Nova 19), and/or the preparation pause is roughly half of the original recording's (ST6, ST7, ST11 to ST15, Nova 13): Part A 15 s instead of 30 s, Part C 45 s instead of 90 s (ST13 30 s instead of 60 s).
- The missing seconds are audible in the numbers: the two sections add up to less than the original source file (for example ST7 Part A: 697.7 s against 712.4 s).

## What each repair does (identical for every item)

1. Cut the replacement file(s) from the ORIGINAL source audio (the pair's cut point sits in the silence just before the second extract's introduction, 0.35 s of pre-roll; no audio is added, retimed or re-worded).
2. Upload them as unattached media, then run the same automated check on GitHub Actions that verified ST8 and ST9 (introduction at the head of the destination, preparation pause equal to the source's, no repeated speech, complete sentences at the ends). A candidate that fails is discarded.
3. Only after that passes: attach as the primary audio for the section (the old file stays as a non-primary row; rollback = re-attach it by id, recorded below).
4. Raise the learner timer to ceil(new audio) + 15 s where the new audio is longer than the current timer allows (direct SQL for papers with learner attempts, the authoring API for JSON-only Nova papers). The new read-time guard on the branch would also cover this once deployed.
5. No question, answer key, transcript or attempt is touched.

## The 18 items

| # | Paper | Pair | Issue found | Source used | Current A1/C1 | Current A2/C2 | Proposed A1/C1 | Proposed A2/C2 | Cut in source (s) | Source prep vs now | Timer floor | Timer route | Rollback (current asset ids) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Nova Test 19 | C1/C2 | head_cue, next_intro_in_tail | <source>\Nova Practice Series_\Audio\LISTENING TEST 19.mp3 | 413.4 | 395.3 | 395.2 | 413.5 | 1616.9 | 90 vs 90 | 429 | API | C1: fa9c5098, C2: c5054e1b |
| 2 | Nova Test 14 | C1/C2 | head_cue, next_intro_in_tail | <source>\Nova Practice Series_\Audio\LISTENING TEST 14.mp3 | 408.4 | 460.2 | 391.9 | 476.7 | 1661.69 | 89.9 vs 89.9 | 492 | SQL | C1: d6b848d9, C2: 668659e0 |
| 3 | Nova Test 13 | C1/C2 | prep_window | <source>\Nova Practice Series_\Audio\LISTENING TEST 13.mp3 | 378 | 320.8 | unchanged | 381.3 | 1237.44 | 90.9 vs 30.5 | 397 | SQL | C1: 79f3bcb5, C2: 0235527c |
| 4 | Atlas ST15 | A1/A2 | head_cue, source_duration, next_intro_in_tail | <source>\Atlas Practice Series\Audio\16- Sample Test 15\14-Part A.mp3 | 255.9 | 235.7 | 244.5 | 261.9 | 244.53 | 30.4 vs 15.1 | 277 | SQL | A1: 91d0056d, A2: daafb6fa |
| 5 | Atlas ST15 | C1/C2 | head_cue, source_duration, next_intro_in_tail | <source>\Atlas Practice Series\Audio\16- Sample Test 15\14-Part C.mp3 | 442.1 | 303.4 | 430.6 | 359.7 | 430.61 | 90.2 vs 45.1 | 375 | SQL | C1: 4c8dafad, C2: c8708caf |
| 6 | Atlas ST14 | A1/A2 | head_cue, source_duration, next_intro_in_tail | <source>\Atlas Practice Series\Audio\15- Sample Test 14\13-Part A.mp3 | 272.6 | 253.3 | 262.1 | 278.4 | 262.13 | 30.1 vs 15 | 294 | SQL | A1: 8538e581, A2: 482ed6ed |
| 7 | Atlas ST14 | C1/C2 | head_cue, source_duration | <source>\Atlas Practice Series\Audio\15- Sample Test 14\13-Part C.mp3 | 439 | 328.2 | MANUAL | MANUAL | MANUAL | 90.05 vs - |  | SQL | C1: c9a3b481, C2: 189904f9 |
| 8 | Atlas ST13 | A1/A2 | head_cue, source_duration, next_intro_in_tail | <source>\Atlas Practice Series\Audio\14- Sample Test 13\12-Part A.mp3 | 284.1 | 233.9 | 271.9 | 261.6 | 271.85 | 31.5 vs 15.7 | 277 | SQL | A1: 2e2398b9, A2: ccc0181a |
| 9 | Atlas ST13 | C1/C2 | head_cue, source_duration | <source>\Atlas Practice Series\Audio\14- Sample Test 13\12-Part C.mp3 | 378 | 305.3 | MANUAL | MANUAL | MANUAL | 60.57 vs - |  | SQL | C1: e9607ff3, C2: 553762f4 |
| 10 | Atlas ST12 | A1/A2 | head_cue, source_duration, next_intro_in_tail | <source>\Atlas Practice Series\Audio\13- Sample Test 12\11-Part A.mp3 | 238.2 | 239.8 | 226.2 | 267 | 226.23 | 31.2 vs 15.5 | 282 | SQL | A1: 0e7970f6, A2: 81669bbd |
| 11 | Atlas ST12 | C1/C2 | head_cue, source_duration, next_intro_in_tail | <source>\Atlas Practice Series\Audio\13- Sample Test 12\11-Part C.mp3 | 302.7 | 266 | 292.4 | 306.2 | 292.38 | 60.4 vs 30.1 | 322 | SQL | C1: 31999bac, C2: f6715223 |
| 12 | Atlas ST11 | A1/A2 | head_cue, source_duration, next_intro_in_tail | <source>\Atlas Practice Series\Audio\12- Sample Test 11\10-Part A.mp3 | 357 | 271 | 342.5 | 300.3 | 342.5 | 30.4 vs 15.1 | 316 | SQL | A1: ce1c536a, A2: f19cc649 |
| 13 | Atlas ST11 | C1/C2 | prep_window, source_duration | <source>\Atlas Practice Series\Audio\12- Sample Test 11\10-Part C.mp3 | 388.2 | 510.2 | 386.3 | 556.8 | 386.28 | 90 vs 45.3 | 572 | SQL | C1: aa43e7a2, C2: 21c7c3aa |
| 14 | Atlas ST9 | C1/C2 | next_intro_in_tail | <source>\Atlas Practice Series\Audio\10- Sample Test 9\Part C.mp3 | 400 | - | 387.7 | 422.6 | 387.66 | 90 vs - | 438 | SQL | C1: f75f19a7, C2: - |
| 15 | Atlas ST7 | A1/A2 | prep_window, source_duration | <source>\Atlas Practice Series\Audio\8- Sample Test 7\6-Part A.mp3 | 352.5 | 345.1 | unchanged | 359.8 | 352.58 | 30.2 vs 15.4 | 375 | SQL | A1: dab4ed79, A2: 0ea1f369 |
| 16 | Atlas ST7 | C1/C2 | prep_window, source_duration | <source>\Atlas Practice Series\Audio\8- Sample Test 7\6-Part C.mp3 | 441.5 | 592.4 | unchanged | 637.5 | 441.22 | 90.3 vs 45.4 | 653 | SQL | C1: fbe7f227, C2: 78d82be2 |
| 17 | Atlas ST6 | A1/A2 | prep_window, source_duration | <source>\Atlas Practice Series\Audio\7- Sample Test 6\5-Part A.mp3 | 333.1 | 285.1 | unchanged | 299.5 | 333.46 | 30.2 vs 15.4 | 315 | SQL | A1: c05f20d9, A2: ca298404 |
| 18 | Atlas ST6 | C1/C2 | prep_window, source_duration | <source>\Atlas Practice Series\Audio\7- Sample Test 6\5-Part C.mp3 | 474.6 | 535.1 | 475.3 | 579.2 | 475.31 | 90.3 vs 45.4 | 595 | SQL | C1: a18602e7, C2: 1bf76dff |

## Notes on specific items

- **Nova Test 19 C1/C2**: cut point = end of the 10.2 s transition silence (1607.09-1617.25 s) minus 0.35 s; C1 section starts at source 1221.71 s; moving the 18.2 s introduction from the end of C1 to the head of C2.
- **Nova Test 14 C1/C2**: cut point = end of the 10.2 s transition silence (1651.87-1662.04 s) minus 0.35 s; C1 section starts at source 1269.77 s; moving the 16.5 s introduction from the end of C1 to the head of C2.
- **Nova Test 13 C1/C2**: cut point = end of the 10.1 s transition silence (1227.72-1237.79 s) minus 0.35 s; restores 60.4 s of preparation silence (source 90.9 s vs current 30.5 s).
- **Atlas ST15 A1/A2**: cut point = middle of the 0.12 s pause (244.47-244.59 s) nearest the intro onset (prep starts 255.52 s minus 10.8 s intro); new A1+A2 = 506.4 s = source 506.4 s (nothing dropped).
- **Atlas ST15 C1/C2**: cut point = middle of the 0.67 s pause (430.27-430.94 s) nearest the intro onset (prep starts 441.95 s minus 11.1 s intro); new C1+C2 = 790.3 s = source 790.3 s (nothing dropped).
- **Atlas ST14 A1/A2**: cut point = middle of the 0.23 s pause (262.01-262.24 s) nearest the intro onset (prep starts 272.17 s minus 9.3 s intro); new A1+A2 = 540.5 s = source 540.5 s (nothing dropped).
- **Atlas ST14 C1/C2**: MANUAL: boundary before the second extract could not be located automatically. Source long silences: 10.77-100.74 (89.98); 438.62-528.67 (90.05). The introduction could not be located automatically; the boundary will be picked from the source's fine pause map and confirmed by the automated check before anything is attached.
- **Atlas ST13 A1/A2**: cut point = middle of the 0.2 s pause (271.74-271.95 s) nearest the intro onset (prep starts 283.84 s minus 12 s intro); new A1+A2 = 533.4 s = source 533.4 s (nothing dropped).
- **Atlas ST13 C1/C2**: MANUAL: boundary before the second extract could not be located automatically. Source long silences: 8.15-69.16 (61.01); 377.7-438.27 (60.57). The introduction could not be located automatically; the boundary will be picked from the source's fine pause map and confirmed by the automated check before anything is attached.
- **Atlas ST12 A1/A2**: cut point = middle of the 0.96 s pause (225.74-226.71 s) nearest the intro onset (prep starts 237.79 s minus 11.8 s intro); new A1+A2 = 493.2 s = source 493.2 s (nothing dropped).
- **Atlas ST12 C1/C2**: cut point = middle of the 0.34 s pause (292.21-292.55 s) nearest the intro onset (prep starts 302.39 s minus 10.1 s intro); new C1+C2 = 598.6 s = source 598.6 s (nothing dropped).
- **Atlas ST11 A1/A2**: cut point = end of the 10.7 s transition silence (332.16-342.85 s) minus 0.35 s; new A1+A2 = 642.8 s = source 642.8 s (nothing dropped).
- **Atlas ST11 C1/C2**: cut point = end of the 10.1 s transition silence (376.56-386.63 s) minus 0.35 s; new C1+C2 = 943.1 s = source 943.1 s (nothing dropped).
- **Atlas ST9 C1/C2**: cut point = end of the 11 s transition silence (376.99-388.01 s) minus 0.35 s; new C1+C2 = 810.2 s = source 810.2 s (nothing dropped).
- **Atlas ST7 A1/A2**: cut point = end of the 10.8 s transition silence (342.16-352.93 s) minus 0.35 s; new A1+A2 = 712.4 s = source 712.4 s (nothing dropped).
- **Atlas ST7 C1/C2**: cut point = end of the 10.4 s transition silence (431.16-441.57 s) minus 0.35 s; new C1+C2 = 1078.7 s = source 1078.7 s (nothing dropped).
- **Atlas ST6 A1/A2**: cut point = end of the 10.6 s transition silence (323.19-333.81 s) minus 0.35 s; new A1+A2 = 633 s = source 633 s (nothing dropped).
- **Atlas ST6 C1/C2**: cut point = end of the 10.8 s transition silence (464.84-475.66 s) minus 0.35 s; new C1+C2 = 1054.5 s = source 1054.5 s (nothing dropped).

## Also proposed (optional, needs your decision)

- **Atlas ST9 C1**: the published C1 (400 s) still ends with the introduction of the unpublished C2 ("Now look at Extract 2 ..."). Trimming C1 to 387.7 s removes that dangling introduction. Not applied.
- **Nova 11 Part B** and **Kaplan Part B**: flagged for a listen only (an ending that may be cut mid-sentence; a long trailing silence to confirm as intentional).

## Approve by number

Reply with the item numbers to apply (for example "1-3, 4-16") or "all". I will apply them in that order with the steps above and report each result.